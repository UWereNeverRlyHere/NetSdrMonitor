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
    // TODO(decide): для великої історії знадобиться пагінація/фільтр — уточнити сигнатуру під SQLite
    Task<IReadOnlyList<SignalRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Кількість збережених записів.
    /// </summary>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Очищає сховище (наприклад, перед новою сесією моніторингу).
    /// </summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
