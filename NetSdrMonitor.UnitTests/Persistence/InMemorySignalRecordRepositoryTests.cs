using NetSdrMonitor.Core.Abstractions.Persistence;
using NetSdrMonitor.Domain.Aggregation;
using NetSdrMonitor.Domain.Signals;
using NetSdrMonitor.Infrastructure.Persistence.InMemory;

namespace NetSdrMonitor.UnitTests.Persistence;

/// <summary>
/// Тести сховища записів у пам'яті: порядок додавання, лічильник, очищення,
/// незалежність повернутого знімка та потокобезпечність паралельних додавань.
/// </summary>
public sealed class InMemorySignalRecordRepositoryTests
{
    [Fact]
    public async Task AddAsync_ThenGetAll_ReturnsRecordsInInsertionOrder()
    {
        ISignalRecordRepository repository = new InMemorySignalRecordRepository();
        SignalRecord first  = RecordAt(10_000_000);
        SignalRecord second = RecordAt(20_000_000);

        await repository.AddAsync(first);
        await repository.AddAsync(second);

        IReadOnlyList<SignalRecord> all = await repository.GetAllAsync();
        Assert.Equal(new[] { first, second }, all);
    }

    [Fact]
    public async Task CountAsync_ReflectsNumberOfAddedRecords()
    {
        ISignalRecordRepository repository = new InMemorySignalRecordRepository();
        Assert.Equal(0, await repository.CountAsync());

        await repository.AddAsync(RecordAt(10_000_000));
        await repository.AddAsync(RecordAt(20_000_000));

        Assert.Equal(2, await repository.CountAsync());
    }

    [Fact]
    public async Task ClearAsync_RemovesAllRecords()
    {
        ISignalRecordRepository repository = new InMemorySignalRecordRepository();
        await repository.AddAsync(RecordAt(10_000_000));

        await repository.ClearAsync();

        Assert.Equal(0, await repository.CountAsync());
        Assert.Empty(await repository.GetAllAsync());
    }

    [Fact]
    public async Task GetAllAsync_ReturnsSnapshot_NotAffectedByLaterAdds()
    {
        ISignalRecordRepository repository = new InMemorySignalRecordRepository();
        await repository.AddAsync(RecordAt(10_000_000));

        IReadOnlyList<SignalRecord> snapshot = await repository.GetAllAsync();
        await repository.AddAsync(RecordAt(20_000_000)); // пізніше додавання не торкається вже відданого знімка

        Assert.Single(snapshot);
        Assert.Equal(2, await repository.CountAsync());
    }

    [Fact]
    public async Task AddAsync_Null_ThrowsArgumentNullException()
    {
        ISignalRecordRepository repository = new InMemorySignalRecordRepository();

        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.AddAsync(null!));
    }

    [Fact]
    public async Task AddAsync_ConcurrentWriters_KeepsEveryRecord()
    {
        ISignalRecordRepository repository = new InMemorySignalRecordRepository();
        const int writers   = 8;
        const int perWriter = 500;

        // імітуємо фоновий інжест із кількох потоків — жоден запис не має загубитися чи зламати список
        IEnumerable<Task> writes = Enumerable.Range(0, writers).Select(_ => Task.Run(async () =>
        {
            for (int i = 0; i < perWriter; i++)
                await repository.AddAsync(RecordAt(10_000_000));
        }));
        await Task.WhenAll(writes);

        Assert.Equal(writers * perWriter, await repository.CountAsync());
    }

    /// <summary>
    /// Будує закритий запис із одного сигналу на заданій частоті.
    /// </summary>
    private static SignalRecord RecordAt(ulong frequencyHz)
    {
        var record = new SignalRecord(new Signal
        {
            TimestampUnixMs = 0,
            FrequencyHz     = frequencyHz,
            BandwidthHz     = 10_000,
            SnrDb           = 12.0,
        });
        record.Close();
        return record;
    }
}
