namespace NetSdrMonitor.Communication.Server;

/// <summary>
/// Налаштування мок-сервера: темп видачі сигналів і ймовірності «хаосу» (биті кадри,
/// чужі control item-и, обриви), якими доводимо стійкість монітора. За замовчуванням хаос
/// вимкнено (нулі) — детерміновано для тестів; застосунок/демо вмикає за потреби.
/// </summary>
public sealed record MockSignalServerOptions
{
    /// <summary>
    /// Пауза між сигналами, коли приймач у стані Run.
    /// </summary>
    public TimeSpan SendInterval { get; init; } = TimeSpan.FromMilliseconds(800);

    /// <summary>
    /// Імовірність підмішати кадр Data Item з тілом неправильного розміру (парсер має відхилити).
    /// </summary>
    public double MalformedFrameProbability { get; init; }

    /// <summary>
    /// Імовірність надіслати непідтримуваний Control Item (монітор має відповісти NAK).
    /// </summary>
    public double UnknownControlProbability { get; init; }

    /// <summary>
    /// Імовірність імітувати обрив з'єднання на черговому такті.
    /// </summary>
    public double DropProbability { get; init; }

    /// <summary>
    /// Сід для «хаос»-генератора рішень; null — недетерміновано.
    /// </summary>
    public int? ChaosSeed { get; init; }
}
