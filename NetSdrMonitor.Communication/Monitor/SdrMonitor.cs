using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NetSdrMonitor.Core.Abstractions.Communication;
using NetSdrMonitor.Domain.Signals;
using NetSdrMonitor.Protocol;
using NetSdrMonitor.Protocol.Messages;

namespace NetSdrMonitor.Communication.Monitor;

/// <summary>
/// Монітор лінії NetSDR — основна реалізація застосунку. Сам володіє фоновою петлею з'єднання:
/// Start запускає її, Stop гасить. Перше підключення й відновлення після обриву — один шлях із backoff.
/// Поточний стан віддає назовні через <see cref="Status"/> і подію <see cref="StatusChanged"/>,
/// а розібрані сигнали — через канал (<see cref="Signals"/>). Єдине місце технічних логів цієї лінії.
/// </summary>
public sealed class SdrMonitor : ISdrMonitor
{
   private readonly ILogger<SdrMonitor> _logger;
   private readonly ITransportFactory _transportFactory;
   private readonly SdrMonitorOptions _options;

   private readonly ISdrProtocol _protocol = new SdrProtocol();
   private readonly ISdrMessageParser _parser = new SdrMessageParser();

   private readonly ReadOnlyMemory<byte> _runCommand;
   private readonly ReadOnlyMemory<byte> _stopCommand;

   // канал розв'язує виробника (петля) і споживача (агрегатор/UI): пишемо в одному темпі, читаємо в іншому
   private readonly Channel<Signal> _channel = Channel.CreateUnbounded<Signal>(
         new UnboundedChannelOptions
         {
               SingleReader = true,
               SingleWriter = true
         });

   private readonly Lock _sync = new(); // серіалізує перемикання життєвого циклу (Start/Stop/Dispose)

   private volatile ConnectionStatus _status = ConnectionStatus.Disconnected;

   private ITransport? _transport;
   private CancellationTokenSource? _lifetimeCts;
   private Task? _loopTask;

   public SdrMonitor(
         ILogger<SdrMonitor> logger,
         ITransportFactory   transportFactory,
         SdrMonitorOptions?  options = null)
   {
      _logger           = logger;
      _transportFactory = transportFactory;
      _options          = options ?? new SdrMonitorOptions();

      // команди Run/Stop незмінні — збираємо байти один раз
      _runCommand  = _protocol.CreateReceiverStateMessage(ReceiverState.Running).Raw;
      _stopCommand = _protocol.CreateReceiverStateMessage(ReceiverState.Stopped).Raw;
   }

   public event EventHandler<ConnectionStatus>? StatusChanged;

   public ConnectionStatus Status => _status;

   public IAsyncEnumerable<Signal> Signals(CancellationToken ct = default) => _channel.Reader.ReadAllAsync(ct);

   public void Start()
   {
      lock (_sync)
      {
         if (_loopTask is { IsCompleted: false })
            return; // вже працює — Start ідемпотентний

         _lifetimeCts = new CancellationTokenSource();
         _loopTask    = Task.Run(() => RunLoopAsync(_lifetimeCts.Token));
      }
   }

   public async Task StopAsync(CancellationToken cancellationToken = default)
   {
      Task? loop;
      CancellationTokenSource? cts;
      lock (_sync)
      {
         loop = _loopTask;
         cts  = _lifetimeCts;
      }

      if (loop is null)
      {
         SetStatus(ConnectionStatus.Stopped);
         return;
      }

      await TrySendStopAsync(); //  просимо таргет зупинити приймач, поки сокет ще живий
      if (cts is not null)
         await cts.CancelAsync(); // обриваємо петлю: connect-backoff чи receive-loop вийдуть як Cancelled

      try
      {
         await loop;
      }
      catch
      {
         // петля сама гасить свої винятки; тут лише дочекатись виходу
      }

      lock (_sync)
      {
         // знімаємо посилання, лише якщо ніхто не перезапустив петлю, поки ми чекали
         if (ReferenceEquals(_loopTask, loop))
         {
            _loopTask    = null;
            _lifetimeCts = null;
         }
      }

      cts?.Dispose();
   }

   public async ValueTask DisposeAsync()
   {
      await StopAsync();
      _channel.Writer.TryComplete(); // споживач Signals() завершить свій await foreach

      // у loopback-режимі фабрика володіє мок-сервером — гасимо його разом з монітором
      if (_transportFactory is IAsyncDisposable disposableFactory)
         await disposableFactory.DisposeAsync();
   }

   // Серце життєвого циклу: вся історія «підключитись → приймати → перепідключитись».
   private async Task RunLoopAsync(CancellationToken ct)
   {
      bool everConnected = false;
      try
      {
         _transport ??= _transportFactory.Create();

         while (!ct.IsCancellationRequested)
         {
            SetStatus(everConnected ? ConnectionStatus.Reconnecting : ConnectionStatus.Connecting);
            while (true)
            {
               try
               {
                  await ConnectWithTimeoutAsync(ct);           // завжди RestartAsync
                  await _transport.SendAsync(_runCommand, ct); // новому з'єднанню знову потрібен Run
                  break;
               }
               catch (OperationCanceledException) when (ct.IsCancellationRequested)
               {
                  return; // зупинили під час спроби підключення
               }
               catch (Exception ex) when (ex is SocketException or TimeoutException or IOException)
               {
                  _logger.LogWarning("Connect failed ({Error}); retry in {Delay}s",
                                     ex.Message, _options.ReconnectDelay.TotalSeconds);
                  try
                  {
                     await Task.Delay(_options.ReconnectDelay, ct);
                  }
                  catch (OperationCanceledException) when (ct.IsCancellationRequested)
                  {
                     return; // зупинили під час паузи backoff — виходимо чисто, без винятку назовні
                  }
               }
            }

            everConnected = true;
            SetStatus(ConnectionStatus.Connected);
            _logger.LogInformation("Connected (Run sent)");

            ReadOutcome outcome = await ReceiveUntilBreakAsync(ct);
            if (outcome == ReadOutcome.Cancelled)
               return; // нас зупинили — виходимо в Stopped

            _logger.LogWarning("Connection lost ({Reason}); reconnecting", outcome);
            // інакше (PeerClosed | Idle | Faulted) — новий виток циклу => reconnect
         }
      }
      finally
      {
         if (_transport is not null)
         {
            await _transport.DisposeAsync(); // закриваємо сокет; у loopback це згортає сесію мока під цього клієнта
            _transport = null;
         }

         SetStatus(ct.IsCancellationRequested ? ConnectionStatus.Stopped : ConnectionStatus.Disconnected);
      }
   }

   // Приймає кадри, доки йдуть дані; повертає причину розриву (або Cancelled, якщо нас зупинили).
   private async Task<ReadOutcome> ReceiveUntilBreakAsync(CancellationToken ct)
   {
      while (!ct.IsCancellationRequested)
      {
         ReadBatch batch = await ReadBatchSafelyAsync(ct);

         foreach (Signal signal in batch.Signals)
            _channel.Writer.TryWrite(signal); // unbounded-канал: запис не блокує й не кидає

         if (batch.Outcome != ReadOutcome.Data)
            return batch.Outcome; // PeerClosed | Idle | Faulted | Cancelled
      }

      return ReadOutcome.Cancelled;
   }

   // Один цикл «прочитати порцію → розібрати всі готові кадри». Тут вся ризикована робота (await + виключення).
   [SuppressMessage("ReSharper", "CognitiveComplexity")]
   private async Task<ReadBatch> ReadBatchSafelyAsync(CancellationToken ct)
   {
      try
      {
         using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
         readCts.CancelAfter(_options.IdleTimeout); // тиша довша за дозволену => вважаємо пір завислим

         ReadOnlySequence<byte> buffer = await _transport!.ReceiveAsync(readCts.Token);
         if (buffer.IsEmpty)
            return ReadBatch.Of(ReadOutcome.PeerClosed);

         var signals = new List<Signal>();
         while (!buffer.IsEmpty)
         {
            SdrAnalyzeContext context = _protocol.Analyze(buffer);
            MessageStatus status = context.Status;
            if (status == MessageStatus.Incomplete)
               break; // кадр ще не зібрався — чекаємо байтів, буфер не чіпаємо

            if (status == MessageStatus.Corrupt)
            {
               _logger.LogWarning("Corrupt frame, resynchronizing by 1 byte");
               buffer = buffer.Slice(1);
               continue;
            }

            HandledMessage handled = await HandleReadyAsync(context, buffer, ct);
            buffer = buffer.Slice(handled.ConsumedLength);

            if (handled.Signal is {} signal)
               signals.Add(signal);
         }

         _transport.AdvanceTo(buffer.Start, buffer.End);
         return ReadBatch.Of(signals);
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
         return ReadBatch.Of(ReadOutcome.Cancelled); // нас зупинили (Stop/Dispose)
      }
      catch (OperationCanceledException)
      {
         return ReadBatch.Of(ReadOutcome.Idle); // спрацював idle-таймер
      }
      catch (Exception ex) when (ex is IOException or SocketException)
      {
         _logger.LogWarning(ex, "Transport read failed");
         return ReadBatch.Of(ReadOutcome.Faulted);
      }
   }

   // Складає повідомлення, шле відповідь (якщо треба) і декодує сигнал.
   private async Task<HandledMessage> HandleReadyAsync(SdrAnalyzeContext context, ReadOnlySequence<byte> buffer, CancellationToken ct)
   {
      SdrMessage message = _protocol.Extract(context, buffer);
      _logger.LogTrace("Message read: type={Type}, length={Length}", message.Header.Type, message.Header.Length);

      if (_protocol.GetReply(message) is {} reply)
         await _transport!.SendAsync(reply.Raw, ct);

      Signal? signal = null;
      if (_parser.TryToSignal(message, out Signal decoded))
      {
         _logger.LogDebug("Signal decoded: {FrequencyHz} Hz", decoded.FrequencyHz);
         signal = decoded;
      }

      return new HandledMessage(message.Header.Length, signal);
   }

   // Підключення під обмеженням часу: окремий токен скасовує саме спробу, не плутаючи з користувацьким скасуванням.
   private async Task ConnectWithTimeoutAsync(CancellationToken ct)
   {
      using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      connectCts.CancelAfter(_options.ConnectTimeout);
      try
      {
         await _transport!.RestartAsync(connectCts.Token); // tear down + establish: годиться і для першого коннекту
         _logger.LogInformation("Transport connected");
      }
      catch (OperationCanceledException) when (!ct.IsCancellationRequested)
      {
         throw new TimeoutException($"Підключення не вклалося в {_options.ConnectTimeout.TotalSeconds:0.#} с.");
      }
   }

   // Ввічлива зупинка приймача перед розривом: якщо сокет ще живий — шлемо Stop, помилки ковтаємо.
   private async Task TrySendStopAsync()
   {
      try
      {
         if (_transport is { IsConnected: true })
         {
            using var sendCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await _transport.SendAsync(_stopCommand, sendCts.Token);
            _logger.LogInformation("Stop sent");
         }
      }
      catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
      {
         _logger.LogWarning(ex, "Stop send failed (already disconnected?)");
      }
   }

   private void SetStatus(ConnectionStatus next)
   {
      if (_status == next)
         return;

      _status = next;
      _logger.LogDebug("Status -> {Status}", next);
      StatusChanged?.Invoke(this, next);
   }

   private readonly record struct HandledMessage(int ConsumedLength, Signal? Signal);

   private enum ReadOutcome
   {
      Data,       // прочитали порцію (можливо, з 0 сигналів) — читаємо далі
      PeerClosed, // пір закрив з'єднання
      Idle,       // тиша довша за дозволену
      Faulted,    // помилка сокета/IO
      Cancelled,  // нас зупинили ззовні
   }

   private readonly record struct ReadBatch(IReadOnlyList<Signal> Signals, ReadOutcome Outcome)
   {
      private static readonly IReadOnlyList<Signal> None = [];

      public static ReadBatch Of(IReadOnlyList<Signal> signals) => new(signals, ReadOutcome.Data);

      public static ReadBatch Of(ReadOutcome outcome) => new(None, outcome);
   }
}
