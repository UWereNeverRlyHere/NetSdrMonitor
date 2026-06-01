namespace NetSdrMonitor.Communication.Monitor;

/// <summary>
/// Налаштування лінії монітора: коли вважати з'єднання завислим і як наполегливо відновлюватись.
/// </summary>
public sealed record SdrMonitorOptions
{
    /// <summary>
    /// Тиша довша за це під час Run => вважаємо пір завислим і перепідключаємось.
    /// </summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Пауза між спробами відновлення з'єднання.
    /// </summary>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Ліміт на одну спробу підключення, щоб не висіти на дефолтному таймауті ОС (~20 с).
    /// </summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
