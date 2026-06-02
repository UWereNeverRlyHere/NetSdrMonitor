using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NetSdrMonitor.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Контекст EF Core для сховища записів детекцій: дві таблиці (записи та їх сигнали)
/// зі зв'язком «один-до-багатьох» і каскадним видаленням дочірніх рядків.
/// </summary>
public sealed class SignalRecordDbContext(DbContextOptions<SignalRecordDbContext> options) : DbContext(options)
{
   /// <summary>
    /// Записи детекцій.
    /// </summary>
    public DbSet<SignalRecordEntity> Records => Set<SignalRecordEntity>();

    /// <summary>
    /// Сигнали записів (доступ для масових операцій; зазвичай вантажаться через навігацію).
    /// </summary>
    public DbSet<SignalEntity> Signals => Set<SignalEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<SignalRecordEntity> record = modelBuilder.Entity<SignalRecordEntity>();
        record.ToTable("SignalRecords");
        record.HasKey(r => r.Id);
        record.Property(r => r.Id).ValueGeneratedOnAdd();
        record
            .HasMany(r => r.Signals)
            .WithOne()
            .HasForeignKey(s => s.RecordId)
            .OnDelete(DeleteBehavior.Cascade);

        EntityTypeBuilder<SignalEntity> signal = modelBuilder.Entity<SignalEntity>();
        signal.ToTable("Signals");
        signal.HasKey(s => s.Id);
        signal.Property(s => s.Id).ValueGeneratedOnAdd();
        // індекс по (RecordId, Ordinal): пришвидшує впорядковане читання сигналів запису
        signal.HasIndex(s => new { s.RecordId, s.Ordinal });
    }
}
