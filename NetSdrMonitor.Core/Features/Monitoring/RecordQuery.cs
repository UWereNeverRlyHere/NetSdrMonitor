namespace NetSdrMonitor.Core.Features.Monitoring;

/// <summary>
/// Опис набору записів для завантаження в таблицю: розмір «хвоста» та (необов'язково) межі за датою.
/// Інкапсулює переклад «днів» користувача в напіввідкритий проміжок [from; to) у локальному часі.
/// </summary>
public readonly record struct RecordQuery
{
    /// <summary>
    /// Скільки найновіших записів брати, коли діапазон дат не заданий.
    /// </summary>
    public required int MaxRecords { get; init; }

    /// <summary>
    /// Нижня межа (включно) або null — без нижньої межі.
    /// </summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>
    /// Верхня межа (виключно) або null — без верхньої межі.
    /// </summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>
    /// Чи задано хоч одну межу за датою (тоді читаємо діапазон, а не «хвіст»).
    /// </summary>
    public bool HasRange => From is not null || To is not null;

    public DateTimeOffset FromInclusive => From ?? DateTimeOffset.MinValue;

    public DateTimeOffset ToExclusive => To ?? DateTimeOffset.MaxValue;

    /// <summary>
    /// Запит за днями фільтра: кожна межа — локальна північ (верхня зсувається на наступний день,
    /// щоб «по» було включно за календарним днем). Обидва null — звичайний «хвіст» на <paramref name="maxRecords"/>.
    /// </summary>
    public static RecordQuery ForDays(int maxRecords, DateTime? fromDay, DateTime? toDay) => new()
    {
        MaxRecords = maxRecords,
        From = fromDay is { } f ? new DateTimeOffset(DateTime.SpecifyKind(f.Date, DateTimeKind.Local)) : null,
        To   = toDay   is { } t ? new DateTimeOffset(DateTime.SpecifyKind(t.Date.AddDays(1), DateTimeKind.Local)) : null,
    };
}
