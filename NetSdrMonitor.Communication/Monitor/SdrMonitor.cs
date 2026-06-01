using System.Buffers;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using NetSdrMonitor.Application.Abstractions.Communication;
using NetSdrMonitor.Domain.Signals;
using NetSdrMonitor.Protocol;
using NetSdrMonitor.Protocol.Messages;

namespace NetSdrMonitor.Communication.Monitor;

/// <summary>
/// Монітор лінії NetSDR — основна реалізація застосунку. Створює транспорт через фабрику й зводить
/// його (байти) з протоколом (framing/відповіді) та парсером (повідомлення-сигнал), віддаючи назовні
/// чистий потік <see cref="Signal"/>. Сам вирішує, КОЛИ перепідключитись (простій/обрив), делегуючи
/// транспорту, ЯК саме (<see cref="ITransport.RestartAsync"/>). Єдине місце технічних логів цієї лінії.
/// </summary>
public sealed class SdrMonitor : ISdrMonitor
{
    private readonly ILogger<SdrMonitor> _logger;
    private readonly ITransportFactory   _transportFactory;
    private readonly SdrMonitorOptions   _options;

    // протокол і парсер — єдина реалізація, без стану й залежностей: створюємо самі, не інжектимо
    private readonly ISdrProtocol      _protocol = new SdrProtocol();
    private readonly ISdrMessageParser _parser   = new SdrMessageParser();

    private readonly ReadOnlyMemory<byte> _runCommand;
    private readonly ReadOnlyMemory<byte> _stopCommand;

    private ITransport? _transport;

    public SdrMonitor(ILogger<SdrMonitor> logger, ITransportFactory transportFactory, SdrMonitorOptions? options = null)
    {
        _logger = logger;
        _transportFactory = transportFactory;
        _options = options ?? new SdrMonitorOptions();

        // команди Run/Stop незмінні — збираємо байти один раз
        _runCommand  = _protocol.CreateReceiverStateMessage(ReceiverState.Running).Raw;
        _stopCommand = _protocol.CreateReceiverStateMessage(ReceiverState.Stopped).Raw;
    }

    public bool IsRunning { get; private set; }

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _transport = _transportFactory.Create();
        Status = ConnectionStatus.Connecting;

        await ConnectWithTimeoutAsync(isRestart: false, cancellationToken); // перша спроба; кине, якщо невдало
        await _transport.SendAsync(_runCommand, cancellationToken);

        IsRunning = true;
        Status = ConnectionStatus.Connected;
        _logger.LogInformation("Receiver started (Run sent)");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        IsRunning = false;
        try
        {
            if (_transport is { IsConnected: true })
                await _transport.SendAsync(_stopCommand, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or SocketException)
        {
            _logger.LogWarning(ex, "Stop send failed (already disconnected?)");
        }

        _logger.LogInformation("Receiver stopped (Stop sent)");
    }

    public async ValueTask DisposeAsync()
    {
        IsRunning = false;
        Status = ConnectionStatus.Disconnected;
        if (_transport is not null)
            await _transport.DisposeAsync();
    }

    public async IAsyncEnumerable<Signal> ReceiveSignalsAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            // читання/декодування ізольовані в хелпері (там try/catch); тут лише yield — інакше iterator не дозволяє
            ReadBatch batch = await ReadBatchSafelyAsync(ct);

            foreach (Signal signal in batch.Signals)
                yield return signal;

            if (batch.Outcome == ReadOutcome.Cancelled)
                yield break;

            if (batch.Outcome != ReadOutcome.Data)
                await ReconnectAsync(batch.Outcome, ct); // PeerClosed | Idle | Faulted
        }

        Status = ConnectionStatus.Disconnected;
    }

    // Один цикл «прочитати порцію → розібрати всі готові кадри». Тут вся ризикована робота (await + виключення).
    private async Task<ReadBatch> ReadBatchSafelyAsync(CancellationToken ct)
    {
        try
        {
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (IsRunning)
                readCts.CancelAfter(_options.IdleTimeout); // idle-таймер лише коли реально чекаємо дані

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

                HandledMessage handled = await HandleReadyAsync(context,buffer, ct);
                buffer = buffer.Slice(handled.ConsumedLength);

                if (handled.Signal is { } signal)
                    signals.Add(signal);
            }

            _transport.AdvanceTo(buffer.Start, buffer.End);
            return ReadBatch.Of(signals);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return ReadBatch.Of(ReadOutcome.Cancelled); // нас зупинили (Dispose/скасування ззовні)
        }
        catch (OperationCanceledException)
        {
            // спрацював idle-таймер; якщо за цей час нас уже зупинили — це не привід перепідключатись
            return IsRunning ? ReadBatch.Of(ReadOutcome.Idle) : ReadBatch.Of(ReadOutcome.Data);
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

        if (_protocol.GetReply(message) is { } reply)
            await _transport!.SendAsync(reply.Raw, ct);

        Signal? signal = null;
        if (_parser.TryToSignal(message, out Signal decoded))
        {
            _logger.LogDebug("Signal decoded: {FrequencyHz} Hz", decoded.FrequencyHz);
            signal = decoded;
        }

        return new HandledMessage(message.Header.Length, signal);
    }

    // Відновлення з'єднання з backoff: монітор вирішує КОЛИ, транспорт знає ЯК (RestartAsync).
    private async Task ReconnectAsync(ReadOutcome reason, CancellationToken ct)
    {
        IsRunning = false;
        Status = ConnectionStatus.Reconnecting;
        _logger.LogWarning("Connection lost ({Reason}); reconnecting", reason);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.ReconnectDelay, ct);
                await ConnectWithTimeoutAsync(isRestart: true, ct); // транспорт сам перевстановлює з'єднання
                await _transport!.SendAsync(_runCommand, ct);        // новому з'єднанню знову потрібен Run

                IsRunning = true;
                Status = ConnectionStatus.Connected;
                _logger.LogInformation("Reconnected");
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return; // нас зупинили під час відновлення
            }
            catch (Exception ex) when (ex is SocketException or TimeoutException or IOException)
            {
                _logger.LogWarning("Reconnect failed ({Error}); retry in {Delay}s", ex.Message, _options.ReconnectDelay.TotalSeconds);
                // лишаємось у циклі — наступна спроба після паузи
            }
        }
    }

    // Підключення/перепідключення під обмеженням часу: окремий токен скасовує саме спробу,
    // не плутаючи її з користувацьким скасуванням (ct).
    private async Task ConnectWithTimeoutAsync(bool isRestart, CancellationToken ct)
    {
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(_options.ConnectTimeout);
        try
        {
            if (isRestart)
                await _transport!.RestartAsync(connectCts.Token);
            else
                await _transport!.ConnectAsync(connectCts.Token);

            _logger.LogInformation("Transport connected");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Підключення не вклалося в {_options.ConnectTimeout.TotalSeconds:0.#} с.");
        }
    }

    private readonly record struct HandledMessage(int ConsumedLength, Signal? Signal);

    private enum ReadOutcome
    {
        Data,       // прочитали порцію (можливо, з 0 сигналів) — читаємо далі
        PeerClosed, // пір закрив з'єднання
        Idle,       // тиша довша за дозволену під час Run
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
