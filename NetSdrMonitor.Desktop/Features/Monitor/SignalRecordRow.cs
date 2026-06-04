using CommunityToolkit.Mvvm.ComponentModel;
using NetSdrMonitor.Desktop.Behaviors;
using NetSdrMonitor.Domain.Aggregation;
using NetSdrMonitor.Domain.Signals;

namespace NetSdrMonitor.Desktop.Features.Monitor;

/// <summary>
/// Презентаційна обгортка одного агрегованого запису для рядка таблиці: переводить герци
/// у МГц/кГц і віддає частоту згідно з обраним режимом (медіана чи перший сигнал). Домен
/// лишається чистим — тут лише форматування й сповіщення UI про зміни живого рядка.
/// </summary>
public sealed partial class SignalRecordRow : ObservableObject, IAnimatedRow
{
   private readonly SignalRecord _record;
   private FrequencyMode _mode;

   /// <summary>
   /// Створює рядок поверх запису в заданому режимі частоти.
   /// </summary>
   public SignalRecordRow(SignalRecord record, FrequencyMode mode)
   {
      _record = record;
      _mode   = mode;
   }

   /// <summary>
   /// Час першої детекції запису (локальний час для зручності читання).
   /// </summary>
   public DateTime Time => _record.First.Timestamp.LocalDateTime;

   /// <summary>
   /// Частота для колонки в МГц: медіана або перша частота — залежно від режиму.
   /// </summary>
   public double FrequencyMhz => _record.DisplayFrequencyHz(_mode) / 1_000_000.0;

   /// <summary>
   /// Ширина смуги запису в кГц.
   /// </summary>
   public double BandwidthKhz => _record.BandwidthHz / 1_000.0;

   /// <summary>
   /// Відношення сигнал/шум у дБ (від першого сигналу запису).
   /// </summary>
   public double SnrDb => _record.First.SnrDb;

   /// <summary>
   /// Скільки детекцій злилось у запис.
   /// </summary>
   public int Count => _record.Count;

   /// <summary>
   /// Чи запис уже закритий (більше не поглинає сигнали).
   /// </summary>
   public bool IsClosed => _record.IsClosed;

   /// <summary>
   /// Медіана частоти запису в МГц (для деталізації — незалежно від режиму колонки).
   /// </summary>
   public double MedianFrequencyMhz => _record.MedianFrequencyHz / 1_000_000.0;

   /// <summary>
   /// Усі детекції, що злились у запис (джерело для перегляду «показати сигнали»).
   /// </summary>
   public IReadOnlyList<Signal> Signals => _record.Signals;

   /// <summary>
   /// Прапорець разової анімації появи рядка: ставиться для нових (живих) записів і
   /// гаситься після програвання. Історія, завантажена на старті, не анімується.
   /// </summary>
   public bool NeedFadeIn { get; set; }

   /// <summary>
   /// Сповіщає UI про зміни після нової детекції (оновились медіана, лічильник, стан).
   /// </summary>
   public void Refresh()
   {
      OnPropertyChanged(nameof(FrequencyMhz));
      OnPropertyChanged(nameof(Count));
      OnPropertyChanged(nameof(IsClosed));
   }

   /// <summary>
   /// Перемикає режим частоти й оновлює лише залежну колонку.
   /// </summary>
   public void SetMode(FrequencyMode mode)
   {
      if (_mode == mode)
         return;

      _mode = mode;
      OnPropertyChanged(nameof(FrequencyMhz));
   }
}
