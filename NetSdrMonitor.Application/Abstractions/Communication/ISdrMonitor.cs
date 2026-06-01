using NetSdrMonitor.Domain.Signals;

namespace NetSdrMonitor.Application.Abstractions.Communication;

/// <summary>
/// Монітор однієї лінії NetSDR: піднімає транспорт, керує приймачем (Run/Stop),
/// віддає потік розібраних сигналів і рулить життєвим циклом з'єднання.
/// Це єдиний порт, з яким працює решта застосунку — про транспорт/протокол вона не знає.
/// </summary>
public interface ISdrMonitor : IAsyncDisposable
{
    /// <summary>
    /// Чи активне з'єднання й триває прийом сигналів.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Підключається до таргета й надсилає команду Run, щоб почати потік даних.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Надсилає команду Stop і зупиняє прийом, не розриваючи транспорт.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Асинхронний потік розібраних сигналів — джерело для агрегатора/UI.
    /// </summary>
    IAsyncEnumerable<Signal> ReceiveSignalsAsync(CancellationToken ct = default);
}
