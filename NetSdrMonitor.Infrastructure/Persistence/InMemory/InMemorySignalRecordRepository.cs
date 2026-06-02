using NetSdrMonitor.Core.Abstractions.Persistence;
using NetSdrMonitor.Domain.Aggregation;

namespace NetSdrMonitor.Infrastructure.Persistence.InMemory;

/// <summary>
/// Сховище записів у пам'яті процесу: потокобезпечний список без персистентності.
/// Дефолтна реалізація для запуску без БД і основа для тестів; SQLite-варіант реалізує той самий порт.
/// </summary>
public sealed class InMemorySignalRecordRepository : ISignalRecordRepository
{
   private readonly Lock _gate = new();
   private readonly List<SignalRecord> _records = [];

   /// <summary>
   /// Додає запис у кінець списку.
   /// </summary>
   public Task AddAsync(SignalRecord record, CancellationToken cancellationToken = default)
   {
      ArgumentNullException.ThrowIfNull(record);
      cancellationToken.ThrowIfCancellationRequested();

      lock (_gate)
         _records.Add(record);

      return Task.CompletedTask;
   }

   /// <summary>
   /// Повертає знімок-копію всіх записів — її безпечно ітерувати поза локом.
   /// </summary>
   public Task<IReadOnlyList<SignalRecord>> GetAllAsync(CancellationToken cancellationToken = default)
   {
      cancellationToken.ThrowIfCancellationRequested();
      
      lock (_gate)
         return Task.FromResult<IReadOnlyList<SignalRecord>>(_records.ToArray());
   }

   /// <summary>
   /// Поточна кількість записів.
   /// </summary>
   public Task<int> CountAsync(CancellationToken cancellationToken = default)
   {
      cancellationToken.ThrowIfCancellationRequested();

      lock (_gate)
         return Task.FromResult(_records.Count);
   }

   /// <summary>
   /// Прибирає всі записи.
   /// </summary>
   public Task ClearAsync(CancellationToken cancellationToken = default)
   {
      cancellationToken.ThrowIfCancellationRequested();

      lock (_gate)
         _records.Clear();

      return Task.CompletedTask;
   }
}
