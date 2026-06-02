using Microsoft.EntityFrameworkCore;

namespace NetSdrMonitor.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Готує файлове сховище SQLite до роботи: створює файл БД і схему, якщо їх ще немає.
/// Викликати один раз на старті застосунку перед першим зверненням до сховища.
/// </summary>
public sealed class SqliteBootstrapper(IDbContextFactory<SignalRecordDbContext> contextFactory)
{
   /// <summary>
    /// Створює БД і таблиці, якщо їх немає (ідемпотентно). Повертає true, якщо БД щойно створено.
    /// </summary>
    public async Task<bool> EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        await using SignalRecordDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Database.EnsureCreatedAsync(cancellationToken);
    }
}
