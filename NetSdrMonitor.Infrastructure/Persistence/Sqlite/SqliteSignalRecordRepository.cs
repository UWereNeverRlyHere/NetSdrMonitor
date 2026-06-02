using Microsoft.EntityFrameworkCore;
using NetSdrMonitor.Core.Abstractions.Persistence;
using NetSdrMonitor.Domain.Aggregation;
using NetSdrMonitor.Domain.Signals;

namespace NetSdrMonitor.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Сховище записів у файлі SQLite через EF Core. Кожна операція працює на своєму короткоживучому
/// контексті з фабрики — тож сховище потокобезпечне (фоновий інжест і UI звертаються паралельно).
/// </summary>
public sealed class SqliteSignalRecordRepository(IDbContextFactory<SignalRecordDbContext> contextFactory) : ISignalRecordRepository
{
    /// <summary>
    /// Зберігає запис разом із його сигналами.
    /// </summary>
    public async Task AddAsync(SignalRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using SignalRecordDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken);
        db.Records.Add(ToEntity(record));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Повертає всі записи в порядку додавання, відновлюючи багату модель із збережених сигналів.
    /// </summary>
    public async Task<IReadOnlyList<SignalRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using SignalRecordDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken);

        List<SignalRecordEntity> entities = await db.Records
                                                    .AsNoTracking()
                                                    .Include(r => r.Signals)
                                                    .OrderBy(r => r.Id)
                                                    .ToListAsync(cancellationToken);

        var result = new List<SignalRecord>(entities.Count);
        foreach (SignalRecordEntity entity in entities)
            result.Add(ToDomain(entity));

        return result;
    }

    /// <summary>
    /// Кількість збережених записів.
    /// </summary>
    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using SignalRecordDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Records.CountAsync(cancellationToken);
    }

    /// <summary>
    /// Прибирає всі записи та їх сигнали.
    /// </summary>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using SignalRecordDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken);
        // спершу дочірні рядки, потім батьківські — не покладаємось на режим зовнішніх ключів
        await db.Signals.ExecuteDeleteAsync(cancellationToken);
        await db.Records.ExecuteDeleteAsync(cancellationToken);
    }

    private static SignalRecordEntity ToEntity(SignalRecord record)
    {
        var entity = new SignalRecordEntity
        {
                    IsClosed = record.IsClosed
        };

        int ordinal = 0;
        foreach (Signal signal in record.Signals)
            entity.Signals.Add(new SignalEntity
            {
                        Ordinal         = ordinal++,
                        TimestampUnixMs = signal.TimestampUnixMs,
                        FrequencyHz     = signal.FrequencyHz,
                        BandwidthHz     = signal.BandwidthHz,
                        SnrDb           = signal.SnrDb,
            });

        return entity;
    }

    private static SignalRecord ToDomain(SignalRecordEntity entity)
    {
        List<SignalEntity> ordered = entity.Signals.OrderBy(s => s.Ordinal).ToList();

        var record = new SignalRecord(ToSignal(ordered[0]));
        for (int i = 1; i < ordered.Count; i++)
            record.TryAppend(ToSignal(ordered[i]));

        if (entity.IsClosed)
            record.Close();

        return record;
    }

    private static Signal ToSignal(SignalEntity entity) => new()
    {
                TimestampUnixMs = entity.TimestampUnixMs,
                FrequencyHz     = entity.FrequencyHz,
                BandwidthHz     = entity.BandwidthHz,
                SnrDb           = entity.SnrDb,
    };
}
