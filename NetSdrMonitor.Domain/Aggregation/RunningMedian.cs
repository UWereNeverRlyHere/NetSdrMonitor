namespace NetSdrMonitor.Domain.Aggregation;

/// <summary>
/// Інкрементальний калькулятор медіани потоку частот (алгоритм «двох куп»).
/// Додавання значення — O(log n), читання медіани — O(1). Завдяки цьому медіану можна
/// перераховувати на КОЖЕН сигнал навіть за високого темпу, без фонового потоку чи таймера.
/// </summary>
/// <remarks>
/// Ідея: тримаємо потік розділеним на дві половини довкола медіани.
/// <list type="bullet">
///   <item><c>_low</c> — max-купа: містить меншу половину значень, нагорі — найбільше з них.</item>
///   <item><c>_high</c> — min-купа: містить більшу половину значень, нагорі — найменше з них.</item>
/// </list>
/// Інваріанти після кожного додавання:
/// <list type="number">
///   <item>кожен елемент <c>_low</c> ≤ кожен елемент <c>_high</c>;</item>
///   <item>розміри куп відрізняються не більше ніж на 1, причому <c>_low</c> ніколи не менша за <c>_high</c>.</item>
/// </list>
/// Тоді медіана — це або вершина <c>_low</c> (непарна кількість), або середнє двох вершин (парна).
/// Не потокобезпечний: додавати значення слід з одного потоку (як і весь агрегатор).
/// </remarks>
public sealed class RunningMedian
{
   // max-купа: компаратор «навпаки», щоб нагорі опинявся найбільший елемент меншої половини
   private readonly PriorityQueue<ulong, ulong> _low = new(Comparer<ulong>.Create(static (a, b) => b.CompareTo(a)));

   // min-купа: стандартний порядок, нагорі — найменший елемент більшої половини
   private readonly PriorityQueue<ulong, ulong> _high = new();

   /// <summary>Скільки значень уже додано.</summary>
   public int Count { get; private set; }

   /// <summary>Поточна медіана (готове значення, без обчислень при читанні).</summary>
   public ulong Value { get; private set; }

   /// <summary>Додає значення в потік і оновлює медіану за O(log n).</summary>
   public void Add(ulong value)
   {
      // 1) кладемо у відповідну купу: ≤ вершини меншої половини → у _low, інакше → у _high
      if (_low.Count == 0 || value <= _low.Peek())
         _low.Enqueue(value, value);
      else
         _high.Enqueue(value, value);

      // 2) ребаланс: вирівнюємо розміри так, щоб різниця була ≤ 1, а _low ≥ _high
      if (_low.Count > _high.Count + 1)
      {
         ulong moved = _low.Dequeue();
         _high.Enqueue(moved, moved);
      }
      else if (_high.Count > _low.Count)
      {
         ulong moved = _high.Dequeue();
         _low.Enqueue(moved, moved);
      }

      Count++;
      Value = Compute();
   }

   private ulong Compute()
   {
      // непарна кількість — зайвий елемент завжди в _low, тож медіана це його вершина
      if (_low.Count > _high.Count)
         return _low.Peek();

      // парна кількість — середнє двох центральних; формула стійка до переповнення ulong
      ulong a = _low.Peek();
      ulong b = _high.Peek();
      return (a & b) + ((a ^ b) >> 1);
   }
}
