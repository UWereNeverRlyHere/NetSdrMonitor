namespace NetSdrMonitor.Core.Abstractions.Communication;

/// <summary>
/// Створює транспорт для монітора. Монітор не знає конкретики (TCP/serial/mock) і не тримає
/// параметрів підключення — їх інкапсулює фабрика, налаштована в композиційному корені.
/// </summary>
public interface ITransportFactory
{
    /// <summary>
    /// Повертає новий, ще не під'єднаний транспорт.
    /// </summary>
    ITransport Create();
}
