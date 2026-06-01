namespace NetSdrMonitor.Domain.Signals;

/// <summary>
/// Один прийнятий сигнал (детекція), розібраний з payload повідомлення NetSDR.
/// Незмінний: значення приходять з байтів і далі не модифікуються.
/// Одиниці виміру закладено в назви полів, щоб не плутати Hz/MHz/kHz.
/// </summary>
public sealed record Signal
{
    /// <summary>
    /// Час детекції у мілісекундах Unix epoch (UTC).
    /// </summary>
    public required long TimestampUnixMs { get; init; }

    /// <summary>
    /// Центральна частота сигналу в герцах.
    /// </summary>
    public required ulong FrequencyHz { get; init; }

    /// <summary>
    /// Ширина смуги сигналу в герцах.
    /// </summary>
    public required uint BandwidthHz { get; init; }

    /// <summary>
    /// Відношення сигнал/шум у децибелах.
    /// </summary>
    public required double SnrDb { get; init; }

    /// <summary>
    /// Момент детекції як <see cref="DateTimeOffset"/> (UTC) для зручності UI.
    /// </summary>
    public DateTimeOffset Timestamp => DateTimeOffset.FromUnixTimeMilliseconds(TimestampUnixMs);
}
