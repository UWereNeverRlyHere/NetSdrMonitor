using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using NetSdrMonitor.Core.Abstractions.Communication;
using NetSdrMonitor.Domain.Signals;
using NetSdrMonitor.Protocol;
using NetSdrMonitor.Protocol.ControlItems;
using NetSdrMonitor.Protocol.Messages;

namespace NetSdrMonitor.Communication.Server;

/// <summary>
/// Самодостатній імітатор таргета NetSDR: сам тримає <see cref="TcpListener"/>, приймає клієнтів,
/// реагує на Run/Stop і стрімить згенеровані сигнали. Кодує сигнали тим самим парсером, що й «справжній»
/// бік (<see cref="ISdrMessageParser.FromSignal"/>), а за налаштуваннями підмішує биті/чужі кадри та
/// обриви — щоб довести стійкість монітора в рантаймі. Технічні логи цієї сторони — тут.
/// </summary>
public sealed class MockSignalServer : ISignalServer
{
   private readonly IPEndPoint _bindEndPoint;
   private readonly RandomSignalGenerator _generator;
   private readonly ILogger<MockSignalServer> _logger;
   private readonly MockSignalServerOptions _options;

   // протокол/парсер — єдина реалізація, без стану: створюємо самі (як і монітор)
   private readonly ISdrProtocol _protocol = new SdrProtocol();
   private readonly ISdrMessageParser _parser = new SdrMessageParser();

   private readonly Random _chaos;
   private readonly Lock _chaosLock = new(); // _chaos і _generator не потокобезпечні — серіалізуємо
   private readonly Lock _generatorLock = new();

   private TcpListener? _listener;

   public MockSignalServer(
         IPEndPoint                bindEndPoint,
         RandomSignalGenerator     generator,
         ILogger<MockSignalServer> logger,
         MockSignalServerOptions?  options = null)
   {
      _bindEndPoint = bindEndPoint;
      _generator    = generator;
      _logger       = logger;
      _options      = options ?? new MockSignalServerOptions();
      _chaos        = new Random(_options.ChaosSeed ?? Random.Shared.Next());
   }

   /// <summary>
   /// Піднімає слухач (bind + listen) і повертає реальний порт — корисно, коли біндились на 0.
   /// Ідемпотентний.
   /// </summary>
   public int Start()
   {
      if (_listener is null)
      {
         _listener = new TcpListener(_bindEndPoint);
         _listener.Start();
         _logger.LogInformation("Mock server listening on {@EndPoint}", _listener.LocalEndpoint);
      }

      return ((IPEndPoint)_listener.LocalEndpoint).Port;
   }

   /// <inheritdoc />
   public async Task RunAsync(CancellationToken cancellationToken = default)
   {
      Start();

      var sessions = new List<Task>();
      try
      {
         while (!cancellationToken.IsCancellationRequested)
         {
            TcpClient client = await _listener!.AcceptTcpClientAsync(cancellationToken);
            sessions.Add(ServeClientAsync(client, cancellationToken));
         }
      }
      catch (OperationCanceledException)
      {
         // штатна зупинка сервера
      }

      await Task.WhenAll(sessions);
   }

   /// <inheritdoc />
   public ValueTask DisposeAsync()
   {
      _listener?.Stop();
      _listener = null;
      return ValueTask.CompletedTask;
   }

   // Обслуговування одного клієнта: паралельно слухаємо команди й женемо помпу сигналів.
   private async Task ServeClientAsync(TcpClient client, CancellationToken serverToken)
   {
      using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
      CancellationToken ct = sessionCts.Token;
      var run = new RunFlag();
      var sendGate = new SemaphoreSlim(1, 1); // помпа й відповіді шлють в один сокет — серіалізуємо

      _logger.LogInformation("Client connected: {@Remote}", client.Client.RemoteEndPoint);
      try
      {
         client.NoDelay = true;
         NetworkStream stream = client.GetStream();
         PipeReader reader = PipeReader.Create(stream);

         Task receive = ReceiveCommandsAsync(reader, stream, sendGate, run, ct);
         Task pump = PumpSignalsAsync(stream, sendGate, run, ct);

         await Task.WhenAny(receive, pump);
         await sessionCts.CancelAsync();
         await Task.WhenAll(Quiet(receive), Quiet(pump));
         await reader.CompleteAsync();
      }
      finally
      {
         sendGate.Dispose();
         client.Dispose(); // закриває потік і сокет
         _logger.LogInformation("Client disconnected");
      }
   }

   // Читання команд клієнта тим самим framing-циклом, що й у моніторі.
   private async Task ReceiveCommandsAsync(
         PipeReader        reader,
         NetworkStream     stream,
         SemaphoreSlim     sendGate,
         RunFlag           run,
         CancellationToken ct)
   {
      while (!ct.IsCancellationRequested)
      {
         ReadResult result = await reader.ReadAsync(ct);
         ReadOnlySequence<byte> buffer = result.Buffer;

         while (!buffer.IsEmpty)
         {
            SdrAnalyzeContext context = _protocol.Analyze(buffer);
            MessageStatus status = context.Status;
            if (status == MessageStatus.Incomplete)
               break;

            if (status == MessageStatus.Corrupt)
            {
               buffer = buffer.Slice(1);
               continue;
            }

            SdrMessage message = _protocol.Extract(context, buffer);
            buffer = buffer.Slice(message.Header.Length);
            await HandleCommandAsync(stream, sendGate, message, run, ct);
         }

         reader.AdvanceTo(buffer.Start, buffer.End);

         if (result.IsCompleted)
            return; // клієнт закрив з'єднання
      }
   }

   private async Task HandleCommandAsync(
         NetworkStream     stream,
         SemaphoreSlim     sendGate,
         SdrMessage        message,
         RunFlag           run,
         CancellationToken ct)
   {
      // Run/Stop — єдина команда, на яку реагуємо поведінкою
      if (_protocol.TryGetReceiverState(message, out ReceiverState state))
      {
         run.On = state == ReceiverState.Running;
         _logger.LogInformation("Receiver state requested: {State}", state);
         return;
      }

      // решту вирішує протокол: NAK на непідтримуваний Control Item
      if (_protocol.GetReply(message) is {} reply)
      {
         await SendAsync(stream, sendGate, reply.Raw, ct);
         _logger.LogWarning("Replied {Type} to unsupported control {Code}", reply.Header.Type, message.ControlCode);
      }
   }

   // Помпа сигналів: поки приймач у Run — періодично шле сигнал, зрідка підмішуючи хаос.
   private async Task PumpSignalsAsync(NetworkStream stream, SemaphoreSlim sendGate, RunFlag run, CancellationToken ct)
   {
      while (!ct.IsCancellationRequested)
      {
         await Task.Delay(_options.SendInterval, ct);
         if (!run.On)
            continue;

         if (Roll(_options.DropProbability))
         {
            _logger.LogWarning("Simulating connection drop");
            return; // вихід із помпи => сесія згортається, сокет закривається
         }

         if (Roll(_options.MalformedFrameProbability))
         {
            await SendAsync(stream, sendGate, MakeMalformedSignalFrame(), ct);
            _logger.LogDebug("Injected malformed data frame");
            continue;
         }

         if (Roll(_options.UnknownControlProbability))
         {
            await SendAsync(stream, sendGate, MakeUnknownControlFrame(), ct);
            _logger.LogDebug("Injected unsupported control item");
            continue;
         }

         Signal signal = NextSignal();
         await SendAsync(stream, sendGate, _parser.FromSignal(signal).Raw, ct);
         _logger.LogTrace("Signal sent: {Hz} Hz", signal.FrequencyHz);
      }
   }

   private async static Task SendAsync(
         NetworkStream        stream,
         SemaphoreSlim        sendGate,
         ReadOnlyMemory<byte> bytes,
         CancellationToken    ct)
   {
      await sendGate.WaitAsync(ct);
      try
      {
         await stream.WriteAsync(bytes, ct);
      }
      finally
      {
         sendGate.Release();
      }
   }

   private bool Roll(double probability)
   {
      if (probability <= 0)
         return false;

      lock (_chaosLock)
         return _chaos.NextDouble() < probability;
   }

   private Signal NextSignal()
   {
      lock (_generatorLock)
         return _generator.Next();
   }

   // Кадр Data Item 0 з валідним обрамленням, але тілом не на 28 байтів:
   // монітор має прийняти кадр на рівні framing, а парсер — відхилити як «не сигнал».
   private byte[] MakeMalformedSignalFrame()
   {
      const int wrongPayload = 8; // навмисно не 28
      SdrMessageHeader header = SdrMessageHeader.FromPayload(SdrMessageType.DataItem0, wrongPayload);

      var raw = new byte[header.Length];
      header.WriteTo(raw);
      lock (_chaosLock)
         _chaos.NextBytes(raw.AsSpan(SdrMessageHeader.Size));
      return raw;
   }

   // Set Control Item з кодом, який ми не підтримуємо — монітор має відповісти NAK.
   private static byte[] MakeUnknownControlFrame()
   {
      const int controlCodeSize = 2;
      SdrMessageHeader header = SdrMessageHeader.FromPayload(SdrMessageType.SetControlItem, controlCodeSize);

      var raw = new byte[header.Length];
      header.WriteTo(raw);
      BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(SdrMessageHeader.Size), (ushort)ControlItemCode.RfGain);
      return raw;
   }

   private async static Task Quiet(Task task)
   {
      try
      {
         await task;
      }
      catch
      {
         // під час згортання сесії гасимо все: відміну, обрив сокета тощо
      }
   }

   // Прапорець «приймач у Run»: ставить цикл прийому, читає помпа (різні задачі).
   private sealed class RunFlag
   {
      public volatile bool On;
   }
}
