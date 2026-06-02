using Microsoft.Extensions.DependencyInjection;
using NetSdrMonitor.Core.Abstractions.Persistence;
using NetSdrMonitor.Domain.Aggregation;
using NetSdrMonitor.Domain.Signals;
using NetSdrMonitor.Infrastructure.Persistence.Sqlite;

namespace NetSdrMonitor.IntegrationTests.Persistence;

/// <summary>
/// Наскрізні тести SQLite-сховища на справжньому файлі БД: створення файлу, кругообіг
/// (запис → читання з відновленням агрегату), збереження між підключеннями, очищення.
/// Кожен тест має власний тимчасовий файл, який прибирається після прогону.
/// </summary>
public sealed class SqliteSignalRecordRepositoryTests : IDisposable
{
    // станція для збірки тестових записів: смуга 10 кГц => джитер у межах смуги
    private const ulong CenterHz    = 100_000_000;
    private const uint  BandwidthHz = 10_000;

    private readonly string _databaseDirectory;
    private readonly string _databasePath;

    public SqliteSignalRecordRepositoryTests()
    {
        _databaseDirectory = Path.Combine(Path.GetTempPath(), "netsdr-it", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_databaseDirectory);
        _databasePath = Path.Combine(_databaseDirectory, "netsdrmonitor.db");
    }

    [Fact]
    public async Task EnsureCreatedAsync_CreatesDatabaseFileWhenMissing()
    {
        Assert.False(File.Exists(_databasePath));

        await using ServiceProvider provider = BuildProvider();
        bool created = await provider.GetRequiredService<SqliteBootstrapper>().EnsureCreatedAsync();

        Assert.True(created);
        Assert.True(File.Exists(_databasePath));
    }

    [Fact]
    public async Task EnsureCreatedAsync_SecondCall_DoesNotRecreate()
    {
        await using ServiceProvider provider = BuildProvider();
        SqliteBootstrapper bootstrapper = provider.GetRequiredService<SqliteBootstrapper>();

        Assert.True(await bootstrapper.EnsureCreatedAsync());  // перший раз — створено
        Assert.False(await bootstrapper.EnsureCreatedAsync()); // вже існує — без повторного створення
    }

    [Fact]
    public async Task AddAsync_ThenGetAll_RestoresAggregatedRecord()
    {
        await using ServiceProvider provider = BuildProvider();
        ISignalRecordRepository repository =
            await provider.GetRequiredService<ISignalRecordRepositoryFactory>().CreateAsync(inMemory: false);

        SignalRecord original = ClosedRecordOfThree();
        await repository.AddAsync(original);

        IReadOnlyList<SignalRecord> all = await repository.GetAllAsync();

        SignalRecord restored = Assert.Single(all);
        Assert.Equal(original.Count, restored.Count);                         // 3 сигнали
        Assert.Equal(original.FrequencyHz, restored.FrequencyHz);             // частота першого
        Assert.Equal(original.BandwidthHz, restored.BandwidthHz);
        Assert.Equal(original.MedianFrequencyHz, restored.MedianFrequencyHz); // медіана відновлена
        Assert.Equal(original.Signals.Count, restored.Signals.Count);         // drill-down збережено
        Assert.True(restored.IsClosed);
    }

    [Fact]
    public async Task Records_PersistAcrossSeparateProviders()
    {
        // перше підключення пише й закривається — дані мають лишитися у файлі
        await using (ServiceProvider writer = BuildProvider())
        {
            ISignalRecordRepository writeRepository =
                await writer.GetRequiredService<ISignalRecordRepositoryFactory>().CreateAsync(inMemory: false);
            await writeRepository.AddAsync(ClosedRecordOfThree());
        }

        // друге, незалежне підключення до того ж файлу бачить запис
        await using ServiceProvider reader = BuildProvider();
        ISignalRecordRepository readRepository =
            await reader.GetRequiredService<ISignalRecordRepositoryFactory>().CreateAsync(inMemory: false);
        Assert.Equal(1, await readRepository.CountAsync());
    }

    [Fact]
    public async Task ClearAsync_RemovesAllRecordsAndSignals()
    {
        await using ServiceProvider provider = BuildProvider();
        ISignalRecordRepository repository =
            await provider.GetRequiredService<ISignalRecordRepositoryFactory>().CreateAsync(inMemory: false);
        await repository.AddAsync(ClosedRecordOfThree());

        await repository.ClearAsync();

        Assert.Equal(0, await repository.CountAsync());
        Assert.Empty(await repository.GetAllAsync());
    }

    [Fact]
    public async Task CreateAsync_InMemory_ReturnsWorkingStore_WithoutTouchingDatabaseFile()
    {
        await using ServiceProvider provider = BuildProvider();
        ISignalRecordRepository repository =
            await provider.GetRequiredService<ISignalRecordRepositoryFactory>().CreateAsync(inMemory: true);

        await repository.AddAsync(ClosedRecordOfThree());

        Assert.Equal(1, await repository.CountAsync());
        Assert.False(File.Exists(_databasePath)); // летке сховище не створює файл БД
    }

    public void Dispose()
    {
        // прибирання тимчасової теки; файл може ще бути зайнятий пулом SQLite — не критично
        try
        {
            Directory.Delete(_databaseDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private ServiceProvider BuildProvider() =>
        new ServiceCollection()
            .AddSqliteSignalStorage(_databasePath)
            .BuildServiceProvider();

    /// <summary>
    /// Будує закритий запис із трьох сигналів у одній смузі (медіана частот = центр + 2).
    /// </summary>
    private static SignalRecord ClosedRecordOfThree()
    {
        var record = new SignalRecord(SignalAt(CenterHz));
        record.TryAppend(SignalAt(CenterHz + 2));
        record.TryAppend(SignalAt(CenterHz + 4));
        record.Close();
        return record;
    }

    private static Signal SignalAt(ulong frequencyHz) =>
        new()
        {
            TimestampUnixMs = 1_700_000_000_000,
            FrequencyHz     = frequencyHz,
            BandwidthHz     = BandwidthHz,
            SnrDb           = 12.5,
        };
}
