using System.Net;
using Microsoft.Extensions.Logging;
using NetSdrMonitor.Communication.Monitor;
using NetSdrMonitor.Communication.Server;
using NetSdrMonitor.Core.Abstractions.Communication;
using NetSdrMonitor.Desktop.Settings;

namespace NetSdrMonitor.Desktop.Shell;

/// <summary>
/// Реалізація порту монітора в композиційному корені: на кожен Create() підіймає свіжий мок-сервер на
/// loopback і клієнтський монітор під поточні настройки (таймаути, «хаос» мока). Читання настройок саме тут
/// робить так, що рестарт сесії застосовує змінені опції. Бойова заміна — TcpClientTransportFactory.
/// </summary>
public sealed class SdrMonitorFactory(JsonSettingsStore store, ILoggerFactory loggerFactory) : ISdrMonitorFactory
{
    public ISdrMonitor Create()
    {
        AppSettings settings = store.Load();

        var generator = new RandomSignalGenerator();
        var mock = new MockSignalServer(
            new IPEndPoint(IPAddress.Loopback, 0),
            generator,
            loggerFactory.CreateLogger<MockSignalServer>(),
            settings.Mock);

        // фабрика володіє моком: монітор діспозить її разом із собою -> мок гаситься на Stop
        var transportFactory = new MockLoopbackTransportFactory(mock);
        return new SdrMonitor(loggerFactory.CreateLogger<SdrMonitor>(), transportFactory, settings.Monitor);
    }
}
