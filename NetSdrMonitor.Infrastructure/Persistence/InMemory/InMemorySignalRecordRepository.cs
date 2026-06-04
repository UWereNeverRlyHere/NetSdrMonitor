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
   private readonly int _capacity;

   /// <summary>
   /// Створює сховище з обмеженням на кількість записів: за переповнення найстаріші відкидаються,
   /// тож пам'ять процесу лишається обмеженою (за замовчуванням — без обмеження).
   /// </summary>
   public InMemorySignalRecordRepository(int capacity = int.MaxValue) =>
      _capacity = capacity > 0 ? capacity : int.MaxValue;

   /// <summary>
   /// Додає запис у кінець списку, відкидаючи найстаріші понад місткість.
   /// </summary>
   public Task AddAsync(SignalRecord record, CancellationToken cancellationToken = default)
   {
      ArgumentNullException.ThrowIfNull(record);
      cancellationToken.ThrowIfCancellationRequested();

      lock (_gate)
      {
         _records.Add(record);
         // тримаємо лише останні _capacity: голова списку — найстаріші записи
         int excess = _records.Count - _capacity;
         if (excess > 0)
            _records.RemoveRange(0, excess);
      }

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
   /// Найновіші <paramref name="limit"/> записів (хвіст списку) — копією, безпечною поза локом.
   /// </summary>
   public Task<IReadOnlyList<SignalRecord>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
   {
      cancellationToken.ThrowIfCancellationRequested();
      if (limit <= 0)
         return Task.FromResult<IReadOnlyList<SignalRecord>>([]);

      lock (_gate)
      {
         int skip = Math.Max(0, _records.Count - limit);
         return Task.FromResult<IReadOnlyList<SignalRecord>>(_records.Skip(skip).ToArray());
      }
   }

   /// <summary>
   /// Записи, чий час першої детекції потрапляє в [from; to).
   /// </summary>
   public Task<IReadOnlyList<SignalRecord>> GetInRangeAsync(
      DateTimeOffset fromInclusive,
      DateTimeOffset toExclusive,
      CancellationToken cancellationToken = default)
   {
      cancellationToken.ThrowIfCancellationRequested();

      lock (_gate)
      {
         SignalRecord[] hit = _records
            .Where(r => r.First.Timestamp >= fromInclusive && r.First.Timestamp < toExclusive)
            .ToArray();
         return Task.FromResult<IReadOnlyList<SignalRecord>>(hit);
      }
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
