namespace NetSdrMonitor.Core.Abstractions.Persistence;

/// <summary>
/// Фабрика сховищ записів: дозволяє композиційному кореню / UI обрати реалізацію порту
/// під час виконання (галочка «в пам'яті» проти SQLite), не знаючи про конкретику.
/// </summary>
public interface ISignalRecordRepositoryFactory
{
    /// <summary>
    /// Повертає готове до роботи сховище: при inMemory=true — летке в пам'яті,
    /// інакше — файлове SQLite з уже створеними файлом БД і схемою.
    /// </summary>
    Task<ISignalRecordRepository> CreateAsync(bool inMemory, CancellationToken cancellationToken = default);
}
