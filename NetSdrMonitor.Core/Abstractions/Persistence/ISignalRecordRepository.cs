using NetSdrMonitor.Domain.Aggregation;

namespace NetSdrMonitor.Core.Abstractions.Persistence;

/// <summary>
/// Порт сховища агрегованих записів детекцій: застосунок зберігає закриті записи
/// й читає історію для таблиці. Конкретику (пам'ять, SQLite) знає лише композиційний корінь.
/// </summary>
public interface ISignalRecordRepository
{
    /// <summary>
    /// Зберігає (зазвичай уже закритий) запис у сховище.
    /// </summary>
    Task AddAsync(SignalRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Повертає знімок усіх збережених записів у порядку додавання.
    /// </summary>
    Task<IReadOnlyList<SignalRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Повертає щонайбільше <paramref name="limit"/> найновіших записів — стартовий «хвіст» для таблиці.
    /// </summary>
    Task<IReadOnlyList<SignalRecord>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Повертає всі записи, чий час першої детекції потрапляє в напіввідкритий проміжок [from; to).
    /// </summary>
    Task<IReadOnlyList<SignalRecord>> GetInRangeAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Кількість збережених записів.
    /// </summary>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Очищає сховище (наприклад, перед новою сесією моніторингу).
    /// </summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
