using NetSdrMonitor.Core.Abstractions.Communication;
using NetSdrMonitor.Communication.Tcp;

namespace NetSdrMonitor.Communication.Server;

/// <summary>
/// Демо/тестова фабрика: піднімає <see cref="MockSignalServer"/> на loopback і видає
/// <see cref="TcpClientTransport"/>, під'єднаний саме до нього. Зв'язує клієнта й мок в один пакет —
/// зручно для запуску застосунку без «живого» таргета та для інтеграційних тестів.
/// Бойова заміна — <see cref="TcpClientTransportFactory"/>, що дивиться на реальний пристрій.
/// </summary>
public sealed class MockLoopbackTransportFactory(MockSignalServer mock) : ITransportFactory, IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _gate = new();

    private int   _port;
    private Task? _serverLoop;
    private bool  _disposed;

    public ITransport Create()
    {
        EnsureServerStarted();
        return new TcpClientTransport("127.0.0.1", _port);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) // монітор і DI можуть звільнити фабрику обидва — гасимо один раз
                return;

            _disposed = true;
        }

        await _cts.CancelAsync();
        if (_serverLoop is not null)
        {
            try
            {
                await _serverLoop;
            }
            catch
            {
                // зупинка сервера — будь-що тут гасимо
            }
        }

        await mock.DisposeAsync();
        _cts.Dispose();
    }

    // Піднімаємо мок один раз: bind+listen дає реальний порт, accept-loop крутиться у фоні.
    private void EnsureServerStarted()
    {
        lock (_gate)
        {
            if (_serverLoop is not null)
                return;

            _port = mock.Start();
            _serverLoop = mock.RunAsync(_cts.Token);
        }
    }
}
