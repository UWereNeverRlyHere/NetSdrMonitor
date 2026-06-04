using NetSdrMonitor.Core.Abstractions.Persistence;
using NetSdrMonitor.Domain.Aggregation;

namespace NetSdrMonitor.Core.Features.Monitoring;

/// <summary>
/// Читач історії записів для таблиці: за запитом віддає або свіжий «хвіст» (останні N), або всі записи
/// діапазону дат. Діапазон читається запитом до сховища лише в персистентному режимі (SQLite); у леткій
/// пам'яті завжди повертаємо «хвіст», а фільтр за датою накладає вже UI клієнтськи (даних понад «хвіст» там нема).
/// </summary>
public sealed class RecordFeed(RecordSession session)
{
    /// <summary>
    /// Завантажує набір записів під поточний запит. До першого старту сесії повертає порожньо.
    /// </summary>
    public async Task<IReadOnlyList<SignalRecord>> LoadAsync(RecordQuery query, CancellationToken cancellationToken = default)
    {
        if (session.Repository is not { } repository)
            return [];

        return session.IsPersistent && query.HasRange
            ? await repository.GetInRangeAsync(query.FromInclusive, query.ToExclusive, cancellationToken)
            : await repository.GetRecentAsync(query.MaxRecords, cancellationToken);
    }
}
