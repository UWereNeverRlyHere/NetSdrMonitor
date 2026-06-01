using NetSdrMonitor.Domain.Signals;

namespace NetSdrMonitor.Application.Abstractions.Communication;

/// <summary>
/// Монітор однієї лінії NetSDR: володіє фоновою петлею з'єднання (Start/Stop), сам підключається
/// й відновлюється після обриву, віддає потік розібраних сигналів і повідомляє про зміни стану.
/// Це єдиний порт, з яким працює решта застосунку — про транспорт/протокол вона не знає.
/// </summary>
public interface ISdrMonitor : IAsyncDisposable
{
    /// <summary>
    /// Поточний стан лінії (знімок для UI).
    /// </summary>
    ConnectionStatus Status { get; }

    /// <summary>
    /// Здіймається на кожну зміну стану. Передплатник з UI маршалить у свій потік сам —
    /// монітор UI-агностичний і нічого не знає про Dispatcher.
    /// </summary>
    event EventHandler<ConnectionStatus>? StatusChanged;

    /// <summary>
    /// Запускає петлю з'єднання: ставить намір і починає підключення з ретраями.
    /// Не блокує й не кидає на невдалому коннекті — результат видно через <see cref="Status"/>.
    /// </summary>
    void Start();

    /// <summary>
    /// Гасить петлю: просить таргет зупинити приймач і розриває з'єднання. Після цього знову можна Start.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Асинхронний потік розібраних сигналів (канал) — джерело для агрегатора/UI.
    /// Споживають один раз; потік триває крізь паузи Stop/Start і завершується на Dispose.
    /// </summary>
    IAsyncEnumerable<Signal> Signals(CancellationToken ct = default);
}
