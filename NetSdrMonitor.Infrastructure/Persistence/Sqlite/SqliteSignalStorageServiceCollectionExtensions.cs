using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetSdrMonitor.Core.Abstractions.Persistence;

namespace NetSdrMonitor.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Реєстрація сховища записів у DI: фабрика контексту EF, бутстрапер БД і фабрика репозиторіїв
/// (вона ж обирає реалізацію за прапорцем). Уся конкретика лишається тут — корінь робить один виклик.
/// </summary>
public static class SqliteSignalStorageServiceCollectionExtensions
{
    /// <summary>
    /// Назва файлу БД за замовчуванням — кладеться поряд із виконуваним файлом застосунку.
    /// </summary>
    public const string DefaultDatabaseFileName = "netsdrmonitor.db";

    /// <summary>
    /// Підключає сховище записів і його фабрику. Шлях файлу SQLite без аргументу лягає в корінь
    /// застосунку (поряд із .exe); сам файл і схему створює <see cref="SqliteBootstrapper"/> на вимогу.
    /// Готову реалізацію віддає <see cref="ISignalRecordRepositoryFactory"/> за прапорцем (галочка UI).
    /// </summary>
    public static IServiceCollection AddSqliteSignalStorage(this IServiceCollection services, string? databasePath = null)
    {
        string path = databasePath ?? Path.Combine(AppContext.BaseDirectory, DefaultDatabaseFileName);

        services.AddDbContextFactory<SignalRecordDbContext>(options => options.UseSqlite($"Data Source={path}"));
        services.AddSingleton<SqliteBootstrapper>();
        services.AddSingleton<ISignalRecordRepositoryFactory, SignalRecordRepositoryFactory>();

        return services;
    }
}
