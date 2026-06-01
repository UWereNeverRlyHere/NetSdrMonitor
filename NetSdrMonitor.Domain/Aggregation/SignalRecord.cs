using NetSdrMonitor.Domain.Signals;

namespace NetSdrMonitor.Domain.Aggregation;

/// <summary>
/// Агрегована «запис» детекцій довкола однієї частоти — це rich-модель рядка таблиці.
/// Сама вирішує, чи поглинути новий сигнал (правило діапазону), тримає медіану
/// та віддає значення частоти для колонки. Жодного «менеджера» зверху не треба.
/// </summary>
/// <remarks>
/// Базові поля рядка (timestamp, смуга, SNR) формуються з ПЕРШОГО сигналу.
/// Діапазон поглинання: [f − bw/2 ; f + bw/2) — низ включно, верх виключно.
/// Після <see cref="Close"/> запис більше не поглинає сигнали (закритий назавжди).
/// </remarks>
public sealed class SignalRecord
{
   private readonly List<Signal> _signals;

   // Калькулятор медіани — лише «риштування» для обчислення, потрібне доки запис відкритий.
   // Після Close() його звільняємо, а готову медіану тримаємо в _medianHz.
   private RunningMedian? _median = new();
   private ulong          _medianHz;

   /// <summary>
   /// Створює новий відкритий запис із першого сигналу.
   /// </summary>
   public SignalRecord(Signal first)
   {
      First    = first;
      _signals = [first];
      _median!.Add(first.FrequencyHz); // перший елемент: медіана дорівнює йому
      _medianHz = _median.Value;
   }

   /// <summary>
   /// Перший сигнал — джерело базових значень рядка.
   /// </summary>
   public Signal First { get; }

   /// <summary>
   /// Усі детекції запису (для drill-down).
   /// </summary>
   public IReadOnlyList<Signal> Signals => _signals;

   /// <summary>
   /// Скільки детекцій злилось у цей запис.
   /// </summary>
   public int Count => _signals.Count;

   /// <summary>
   /// Центральна частота запису (від першого сигналу).
   /// </summary>
   public ulong FrequencyHz => First.FrequencyHz;

   /// <summary>
   /// Ширина смуги запису (від першого сигналу).
   /// </summary>
   public uint BandwidthHz => First.BandwidthHz;

   /// <summary>
   /// Медіана частот усіх сигналів запису. Поки запис відкритий — підтримується інкрементально
   /// (<see cref="RunningMedian"/>, оновлення O(log n)); читання завжди O(1) із кешованого поля.
   /// Після <see cref="Close"/> значення заморожене. Стійка до викидів (на відміну від середнього).
   /// </summary>
   public ulong MedianFrequencyHz => _medianHz;

   /// <summary>
   /// Чи закритий запис (більше не поглинає сигнали).
   /// </summary>
   public bool IsClosed { get; private set; }

   /// <summary>
   /// Частота для колонки таблиці залежно від обраного режиму (готове значення, без обчислень).
   /// </summary>
   public ulong DisplayFrequencyHz(FrequencyMode mode) => mode == FrequencyMode.Median ? MedianFrequencyHz : First.FrequencyHz;

   /// <summary>
   /// Чи входить частота сигналу в діапазон [f − bw/2 ; f + bw/2).
   /// </summary>
   public bool Accepts(Signal candidate)
   {
      long half = BandwidthHz / 2;          // половина смуги в Гц
      long low  = (long)FrequencyHz - half; // включно
      long high = (long)FrequencyHz + half; // виключно
      long freq = (long)candidate.FrequencyHz;
      return freq >= low && freq < high;
   }

   /// <summary>
   /// Намагається додати сигнал до запису.
   /// true — сигнал поглинуто (той самий рядок); false — поза діапазоном або запис закритий
   /// (потрібно закрити поточний і відкрити новий).
   /// </summary>
   public bool TryAppend(Signal candidate)
   {
      if (IsClosed || !Accepts(candidate))
         return false;

      _signals.Add(candidate);
      _median!.Add(candidate.FrequencyHz); // інкрементально, O(log n)
      _medianHz = _median.Value;
      return true;
   }

   /// <summary>
   /// Закриває запис — далі він незмінний. Звільняє калькулятор медіани (купи більше не потрібні):
   /// готове значення лишається в кеші, а пам'ять «риштувань» забирає GC. Список сигналів
   /// зберігаємо — він потрібен для drill-down («показати сигнали запису»).
   /// </summary>
   public void Close()
   {
      IsClosed = true;
      _median  = null; // купи більше не потрібні — звільняємо ~4× пам'яті від обсягу частот
   }
}
