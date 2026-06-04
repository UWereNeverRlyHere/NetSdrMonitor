namespace NetSdrMonitor.Core.Abstractions.Persistence;

/// <summary>
/// Готує сховище під нову сесію моніторингу за поточними настройками (летке в пам'яті чи файлове
/// SQLite, з лімітом). Реалізація живе в композиційному корені — прикладний сервіс не знає, ні який
/// це тип сховища, ні звідки беруться настройки.
/// </summary>
public interface ISessionStoreFactory
{
    /// <summary>
    /// Створює сховище сесії й повертає його разом з ознакою персистентності.
    /// </summary>
    Task<SessionStore> CreateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Сховище однієї сесії: готовий репозиторій і ознака, чи воно файлове (від цього залежить,
/// як читати історію — діапазон дат запитом до БД чи клієнтський фільтр над леткою пам'яттю).
/// </summary>
public sealed record SessionStore
{
    public required ISignalRecordRepository Repository { get; init; }
    public required bool IsPersistent { get; init; }
}
