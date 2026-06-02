using NetSdrMonitor.Core.Abstractions.Communication;

namespace NetSdrMonitor.Communication.Tcp;

/// <summary>
/// Бойова фабрика: видає <see cref="TcpClientTransport"/> на наперед задані host:port.
/// Параметри підключення тримає вона, не монітор.
/// </summary>
public sealed class TcpClientTransportFactory(string host, int port) : ITransportFactory
{
    public ITransport Create() => new TcpClientTransport(host, port);
}
