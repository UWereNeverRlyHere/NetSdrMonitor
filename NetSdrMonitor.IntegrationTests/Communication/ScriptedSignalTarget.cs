using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using NetSdrMonitor.Domain.Signals;
using NetSdrMonitor.Protocol;
using NetSdrMonitor.Protocol.Messages;

namespace NetSdrMonitor.IntegrationTests.Communication;

/// <summary>
/// Керований із тесту імітатор таргета NetSDR поверх справжнього TCP (loopback). На відміну від
/// бойового мок-сервера, шле рівно ті байти, що звелить тест, і дає прочитати все, що надіслав
/// монітор. Це дозволяє детерміновано перевірити happy-path, биті/чужі кадри, склейку TCP та реконект.
/// </summary>
internal sealed class ScriptedSignalTarget : IAsyncDisposable
{
    private readonly TcpListener _listener;

    public ScriptedSignalTarget()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0); // порт 0 => ОС обере вільний
        _listener.Start();
    }

    /// <summary>
    /// Реальний порт слухача — монітор під'єднується саме сюди.
    /// </summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>
    /// Чекає на чергове підключення монітора й повертає сесію для обміну байтами.
    /// Підходить і для першого коннекту, і для перепідключення після обриву.
    /// </summary>
    public async Task<TargetConnection> AcceptAsync(CancellationToken ct = default)
    {
        TcpClient client = await _listener.AcceptTcpClientAsync(ct);
        client.NoDelay = true; // дрібні кадри без затримки Нейгла — як і бойовий бік
        return new TargetConnection(client);
    }

    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Одна сесія таргета з монітором: дозволяє надіслати монітору сигнали/сирі кадри
/// та прочитати кадри, які монітор шле у відповідь (Run/Stop, NAK). Розбір — справжнім протоколом.
/// </summary>
internal sealed class TargetConnection : IAsyncDisposable
{
    // протокол/парсер без стану — створюємо самі, як і обидва бойові боки
    private readonly ISdrProtocol _protocol = new SdrProtocol();
    private readonly ISdrMessageParser _parser = new SdrMessageParser();

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly PipeReader _reader;

    public TargetConnection(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
        _reader = PipeReader.Create(_stream);
    }

    /// <summary>
    /// Кодує сигнал у кадр Data Item 0 (тим самим парсером, що й бойовий бік) і надсилає монітору.
    /// </summary>
    public Task SendSignalAsync(Signal signal, CancellationToken ct = default) => SendRawAsync(_parser.FromSignal(signal).Raw, ct);

    /// <summary>
    /// Надсилає монітору довільні байти «як є» — для биття кадрів і навмисної склейки/розриву на стику.
    /// </summary>
    public async Task SendRawAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct = default) => await _stream.WriteAsync(bytes, ct);

    /// <summary>
    /// Читає й розбирає наступне ціле повідомлення, надіслане монітором.
    /// Кидає, якщо монітор закрив потік раніше, ніж кадр зібрався.
    /// </summary>
    public async Task<SdrMessage> ReadMessageAsync(CancellationToken ct = default)
    {
        while (true)
        {
            ReadResult result = await _reader.ReadAsync(ct);
            ReadOnlySequence<byte> buffer = result.Buffer;

            SdrAnalyzeContext context = _protocol.Analyze(buffer);
            switch (context.Status)
            {
                case MessageStatus.Ready:
                {
                    SdrMessage message = _protocol.Extract(context, buffer);
                    _reader.AdvanceTo(buffer.GetPosition(message.Header.Length)); // спожили рівно один кадр
                    return message;
                }

                case MessageStatus.Corrupt:
                    _reader.AdvanceTo(buffer.GetPosition(1)); // ресинк на 1 байт (монітор сюди не приводить, але хай буде)
                    break;

                default: // Incomplete — кадр ще не зібрався: переглянули все, чекаємо ще байтів
                    _reader.AdvanceTo(buffer.Start, buffer.End);
                    if (result.IsCompleted)
                        throw new InvalidOperationException("Монітор закрив потік до завершення повідомлення.");
                    break;
            }
        }
    }

    /// <summary>
    /// Читає вхідні кадри, доки не натрапить на запитаний стан приймача (Run/Stop). Інші кадри пропускає.
    /// Зручно «з'їсти» Run, який монітор шле одразу після коннекту, перш ніж стрімити сигнали.
    /// </summary>
    public async Task WaitForReceiverStateAsync(ReceiverState expected, CancellationToken ct = default)
    {
        while (true)
        {
            SdrMessage message = await ReadMessageAsync(ct);
            if (_protocol.TryGetReceiverState(message, out ReceiverState state) && state == expected)
                return;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _reader.CompleteAsync();
        await _stream.DisposeAsync();
        _client.Dispose(); // закриває сокет => монітор побачить розрив (PeerClosed)
    }
}
