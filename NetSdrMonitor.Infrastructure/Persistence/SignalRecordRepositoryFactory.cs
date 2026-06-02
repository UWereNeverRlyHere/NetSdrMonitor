using Microsoft.EntityFrameworkCore;
using NetSdrMonitor.Core.Abstractions.Persistence;
using NetSdrMonitor.Infrastructure.Persistence.InMemory;
using NetSdrMonitor.Infrastructure.Persistence.Sqlite;

namespace NetSdrMonitor.Infrastructure.Persistence;

/// <summary>
/// Фабрика сховищ записів: за прапорцем повертає готову реалізацію порту —
/// летке сховище в пам'яті або персистентне SQLite із гарантовано створеною БД.
/// </summary>
public sealed class SignalRecordRepositoryFactory(
            IDbContextFactory<SignalRecordDbContext> contextFactory,
            SqliteBootstrapper                       bootstrapper) : ISignalRecordRepositoryFactory
{
    public async Task<ISignalRecordRepository> CreateAsync(bool inMemory, CancellationToken cancellationToken = default)
    {
        if (inMemory)
            return new InMemorySignalRecordRepository();

        await bootstrapper.EnsureCreatedAsync(cancellationToken);
        return new SqliteSignalRecordRepository(contextFactory);
    }
}
