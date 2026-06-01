using System.Buffers;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using NetSdrMonitor.Application.Abstractions.Communication;
using NetSdrMonitor.Domain.Signals;
using NetSdrMonitor.Protocol;
using NetSdrMonitor.Protocol.Messages;

namespace NetSdrMonitor.Communication.Monitor;

/// <summary>
/// Монітор лінії NetSDR — основна реалізація застосунку. Зводить разом транспорт (байти + буфер),
/// протокол (framing і правила відповіді) та парсер (повідомлення-сигнал), віддаючи назовні чистий
/// потік <see cref="Signal"/>. Єдине місце технічних логів цієї лінії.
/// </summary>
public sealed class SdrMonitor(
      ITransport            transport,
      ISdrMessageParser     parser,
      ISdrProtocol          protocol,
      ILogger<SdrMonitor>   logger) : ISdrMonitor
{
   public bool IsRunning { get; private set; }

   public async Task StartAsync(CancellationToken cancellationToken = default)
   {
      if (!transport.IsConnected)
      {
         await transport.ConnectAsync(cancellationToken);
         logger.LogInformation("Transport connected");
      }

      // просимо таргет почати потік даних
      SdrMessage runCommand = protocol.CreateReceiverStateMessage(ReceiverState.Running);
      await transport.SendAsync(runCommand.Raw, cancellationToken);
      IsRunning = true;
      logger.LogInformation("Receiver started (Run sent)");
   }

   public async Task StopAsync(CancellationToken cancellationToken = default)
   {
      SdrMessage stopCommand = protocol.CreateReceiverStateMessage(ReceiverState.Stopped);
      await transport.SendAsync(stopCommand.Raw, cancellationToken);
      IsRunning = false;
      logger.LogInformation("Receiver stopped (Stop sent)");
   }

   public async ValueTask DisposeAsync()
   {
      IsRunning = false;
      await transport.DisposeAsync();
   }

   // ReSharper disable once CognitiveComplexity
   public async IAsyncEnumerable<Signal> ReceiveSignalsAsync([EnumeratorCancellation] CancellationToken ct = default)
   {
      while (!ct.IsCancellationRequested)
      {
         ReadOnlySequence<byte> buffer = await transport.ReceiveAsync(ct);
         if (buffer.IsEmpty)
         {
            logger.LogWarning("Peer closed the connection");
            yield break;
         }

         // у буфері може бути 0..N повідомлень — вичитуємо всі, що зібрались
         while (!buffer.IsEmpty)
         {
            MessageStatus status = protocol.Analyze(buffer);

            if (status == MessageStatus.Incomplete)
               break; // чекаємо ще байтів, буфер не чіпаємо

            if (status == MessageStatus.Corrupt)
            {
               logger.LogWarning("Corrupt frame, resynchronizing by 1 byte");
               buffer = buffer.Slice(1); // зсув на байт — спроба ресинку
               continue;
            }

            HandledMessage handled = await HandleReadyAsync(buffer, ct);
            buffer = buffer.Slice(handled.ConsumedLength);

            if (handled.Signal is {} signal)
               yield return signal;
         }

         transport.AdvanceTo(buffer.Start, buffer.End);
      }
   }

   // Складає повідомлення, шле відповідь (якщо треба) і декодує сигнал.
   private async Task<HandledMessage> HandleReadyAsync(ReadOnlySequence<byte> buffer, CancellationToken ct)
   {
      SdrMessage message = protocol.Extract(buffer);
      logger.LogTrace("Message read: type={Type}, length={Length}", message.Header.Type, message.Header.Length);

      SdrMessage? reply = protocol.GetReply(message);
      if (reply is {} replyMessage)
         await transport.SendAsync(replyMessage.Raw, ct);

      Signal? signal = null;
      if (parser.TryToSignal(message, out Signal decoded))
      {
         logger.LogDebug("Signal decoded: {FrequencyHz} Hz", decoded.FrequencyHz);
         signal = decoded;
      }

      return new HandledMessage(message.Header.Length, signal);
   }
   
   private readonly record struct HandledMessage(int ConsumedLength, Signal? Signal);
}
