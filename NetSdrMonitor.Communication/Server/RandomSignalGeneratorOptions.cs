namespace NetSdrMonitor.Communication.Server;

/// <summary>
/// Налаштування генератора сигналів мок-сервера: діапазон центральних частот «станцій» і шанс
/// лишитися біля поточної станції (щоб кілька детекцій злились в один запис).
/// </summary>
public sealed record RandomSignalGeneratorOptions
{
    /// <summary>
    /// Нижня межа центральної частоти станції, Гц (за замовчуванням 1 МГц).
    /// </summary>
    public ulong MinCenterHz { get; init; } = 1_000_000;

    /// <summary>
    /// Верхня межа центральної частоти станції, Гц (за замовчуванням 120 МГц).
    /// </summary>
    public ulong MaxCenterHz { get; init; } = 120_000_000;

    /// <summary>
    /// Шанс (0..1), що наступний сигнал лишиться біля тієї ж станції й потрапить у той самий запис
    /// (за замовчуванням 0.9 = 90%). Інакше генератор перестроюється на нову частоту — новий запис.
    /// </summary>
    public double SameStationProbability { get; init; } = 0.9;
}
