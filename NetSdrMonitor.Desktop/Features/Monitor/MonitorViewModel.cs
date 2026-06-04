using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetSdrMonitor.Core.Features.Monitoring;
using NetSdrMonitor.Desktop.Controls;
using NetSdrMonitor.Domain.Aggregation;
// ReSharper disable MemberCanBePrivate.Global

namespace NetSdrMonitor.Desktop.Features.Monitor;

/// <summary>
/// Модель таблиці моніторингу: показує агреговані записи. Живі зміни приходять подіями від
/// <see cref="MonitoringService"/>, історію вантажить <see cref="RecordFeed"/>. Сама модель — лише
/// презентація: батчить події в UI-потоці, тримає фільтри/сортування, ліміт видимих рядків і діапазон дат.
/// Приймання, агрегація й персист живуть у Core-сервісі, не тут.
/// </summary>
/// <remarks>
/// Потоковість: події записів приходять з фонового потоку сервісу, кладуться в чергу й розбираються
/// батчами на UI-потоці за таймером — так грід не захлинається за високого темпу й не блокує вікно.
/// </remarks>
public sealed partial class MonitorViewModel : ObservableObject
{
   // обмеження розбору за один тік — захист UI від сплеску за дуже високого темпу
   private const int MaxDrainPerTick = 5_000;

   // допустимі формати вводу нижньої межі за часом доби
   private static readonly string[] TimeFormats = { "H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss" };

   private readonly MonitoringService _monitoring;
   private readonly RecordFeed _feed;
   private readonly SynchronizationContext _ui;

   private readonly ConcurrentQueue<RecordChange> _pending = new();
   private readonly DispatcherTimer _pump;

   private SignalRecordRow? _openRow;         // рядок поточного відкритого запису
   private CancellationTokenSource? _loadCts; // скасування попереднього перезавантаження набору
   private TimeSpan? _minTimeOfDay;
   private bool _spectrumDirty;               // спектр треба перебудувати на наступному тіку помпи

   [ObservableProperty]
   private bool _useMedian = true; // за замовчуванням показуємо медіану за частотою

   // максимум рядків на екрані: для пам'яті — жорстка межа, для БД — розмір стартового «хвоста»
   [ObservableProperty]
   private int _maxRecords = 500;

   // триває перезавантаження набору зі сховища — UI показує індикатор зайнятості
   [ObservableProperty]
   private bool _isLoading;

   [ObservableProperty]
   private double _minFrequencyMhz;

   [ObservableProperty]
   private double _minBandwidthKhz;

   // нижня межа за часом доби у форматі «год:хв»; порожньо/некоректно — фільтр за часом вимкнено
   [ObservableProperty]
   private string _minTimeText = string.Empty;

   [ObservableProperty]
   private double _minSnrDb;

   [ObservableProperty]
   private int _minSignalCount;

   // межі фільтра за датою (за днем, включно); null — межа не задана
   [ObservableProperty]
   private DateTime? _fromDate;

   [ObservableProperty]
   private DateTime? _toDate;

   // проекція видимих записів у точки спектра діапазону (медіана частоти → SNR); оновлюється батчем
   [ObservableProperty]
   private IReadOnlyList<SpectrumPoint> _spectrumPoints = [];

   public MonitorViewModel(MonitoringService monitoring, RecordFeed feed)
   {
      _monitoring = monitoring;
      _feed       = feed;
      _ui         = SynchronizationContext.Current ?? new SynchronizationContext();

      Rows     = [];
      RowsView = CollectionViewSource.GetDefaultView(Rows);
      RowsView.Filter = FilterRow;

      // за замовчуванням — найновіші зверху: дата й час сортуються як єдине ціле (день, потім час доби)
      RowsView.SortDescriptions.Add(new SortDescription(nameof(SignalRecordRow.Date), ListSortDirection.Descending));
      RowsView.SortDescriptions.Add(new SortDescription(nameof(SignalRecordRow.Time), ListSortDirection.Descending));

      ((INotifyCollectionChanged)RowsView).CollectionChanged += OnRowsViewChanged;

      _monitoring.RecordChanged += OnRecordChanged; // фоновий потік сервісу -> черга
      _monitoring.SourceChanged += OnSourceChanged; // повне перезавантаження набору (старт/очистка)

      _pump = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
      _pump.Tick += (_, _) => Drain();
      _pump.Start();
   }

   /// <summary>
   /// Агреговані рядки таблиці (по одному на запис).
   /// </summary>
   public ObservableCollection<SignalRecordRow> Rows { get; }

   /// <summary>
   /// Подання колекції для гріда: тримає активні сортування (клік по заголовку) і фільтр.
   /// </summary>
   public ICollectionView RowsView { get; }

   /// <summary>
   /// Заголовок колонки частоти: «Медіана» в режимі медіани, інакше «Частота».
   /// </summary>
   public string FrequencyColumnTitle => UseMedian ? "Медіана, МГц" : "Частота, МГц";

   /// <summary>
   /// Підпис фільтра за частотою — узгоджений із заголовком колонки.
   /// </summary>
   public string FrequencyFilterTitle => UseMedian ? "Медіана ≥, МГц" : "Частота ≥, МГц";

   /// <summary>
   /// Кількість рядків, показаних у таблиці зараз (тобто з урахуванням активного фільтра).
   /// </summary>
   public int VisibleCount => RowsView is CollectionView view ? view.Count : Rows.Count;

   private FrequencyMode CurrentMode => UseMedian ? FrequencyMode.Median : FrequencyMode.First;

   // режим діапазону: лише у персистентному сховищі діапазон читається запитом і вимикає ліміт;
   // у леткій пам'яті діапазон — клієнтський фільтр над обмеженим набором
   private bool RangeMode => _monitoring.IsPersistentStore && RangeActive;

   private bool RangeActive => FromDate is not null || ToDate is not null;

   // --- живий потік записів: фон -> черга -> помпа (UI) ---

   private void OnRecordChanged(RecordChange change) => _pending.Enqueue(change);

   private void Drain()
   {
      int processed = 0;
      while (processed < MaxDrainPerTick && _pending.TryDequeue(out RecordChange change))
      {
         Apply(change);
         processed++;
      }

      // спектр перебудовуємо раз на тік — сплеск нових/відфільтрованих рядків коалесить в одне оновлення
      if (_spectrumDirty)
      {
         _spectrumDirty = false;
         RebuildSpectrum();
      }
   }

   // видимий (відфільтрований) набір -> точки спектра діапазону: позиція по медіані частоти, висота по SNR.
   // зсув медіани в межах однієї станції на масштабі всієї смуги під-піксельний, тож реагуємо лише на
   // появу/зникнення рядків і зміну фільтра (CollectionChanged), а не на кожну детекцію
   private void RebuildSpectrum()
   {
      var points = new List<SpectrumPoint>();
      foreach (object item in RowsView)
         if (item is SignalRecordRow row)
            points.Add(new SpectrumPoint { FrequencyMhz = row.MedianFrequencyMhz, SnrDb = row.SnrDb });

      SpectrumPoints = points;
   }

   private void OnRowsViewChanged(object? sender, NotifyCollectionChangedEventArgs e)
   {
      OnPropertyChanged(nameof(VisibleCount));
      _spectrumDirty = true;
   }

   private void Apply(RecordChange change)
   {
      switch (change.Kind)
      {
         case RecordChangeKind.Opened:
            OpenRow(change.Record);
            break;

         case RecordChangeKind.Updated:
         case RecordChangeKind.Closed:
            _openRow?.Refresh(); // оновились медіана/лічильник/стан відкритого рядка
            break;
      }
   }

   private void OpenRow(SignalRecord record)
   {
      // у режимі БД з активним діапазоном показуємо лише записи проміжку й НЕ обрізаємо за лімітом
      if (RangeMode && !InActiveRange(record))
      {
         _openRow = null; // поза діапазоном: у таблицю не додаємо й «живим» не відстежуємо
         return;
      }

      var row = new SignalRecordRow(record, CurrentMode) { NeedFadeIn = true }; // новий рядок з'явиться з анімацією
      _openRow = row;
      Rows.Add(row);

      if (!RangeMode)
         TrimToCap(); // звичайний «хвіст»: тримаємо межу, відкидаючи найстаріші
   }

   // --- перезавантаження набору (історія) ---

   // сервіс просить перечитати весь набір (старт сесії / очистка) — маршалимо на UI-потік.
   // спершу відкидаємо хвіст подій попереднього набору, щоб старі рядки не просочились у нову сесію
   private void OnSourceChanged() => _ui.Post(_ => ReloadFromSource(), null);

   // нова сесія/очистка: відкидаємо хвіст подій попереднього набору (щоб старі рядки не просочились),
   // скидаємо «живий» рядок і перечитуємо набір заново
   private void ReloadFromSource()
   {
      while (_pending.TryDequeue(out _))
      {
      }

      _openRow = null;
      _ = ReloadAsync();
   }

   // діапазон дат: БД перечитує набір (усі записи проміжку, без ліміту); пам'ять — клієнтський фільтр
   private void OnRangeChanged()
   {
      if (_monitoring.IsPersistentStore)
         _ = ReloadAsync();
      else
         RowsView.Refresh();
   }

   // перечитує набір зі сховища під поточний фільтр; попереднє завантаження скасовуємо, щоб не наклались
   private async Task ReloadAsync()
   {
      _loadCts?.Cancel();
      var cts = new CancellationTokenSource();
      _loadCts = cts;
      CancellationToken token = cts.Token;

      IsLoading = true;
      try
      {
         var query = RecordQuery.ForDays(MaxRecords, FromDate, ToDate);
         IReadOnlyList<SignalRecord> data = await _feed.LoadAsync(query, token);

         if (!token.IsCancellationRequested)
            ReplaceRows(data);
      }
      catch (OperationCanceledException)
      {
         // перекрито новішим завантаженням — ігноруємо
      }
      finally
      {
         if (ReferenceEquals(_loadCts, cts))
            IsLoading = false;
      }
   }

   // замінює весь вміст таблиці завантаженим набором (історія — без анімації появи)
   private void ReplaceRows(IReadOnlyList<SignalRecord> records)
   {
      Rows.Clear();
      _openRow = null; // після переліку «живий» рядок не відстежуємо, доки не відкриється новий
      FrequencyMode mode = CurrentMode;
      foreach (SignalRecord record in records)
         Rows.Add(new SignalRecordRow(record, mode));
   }

   // тримає таблицю в межах MaxRecords, відкидаючи найстаріші.
   // Rows зберігається у хронологічному порядку додавання, тож найстаріший — перший
   private void TrimToCap()
   {
      while (Rows.Count > MaxRecords && Rows.Count > 0)
      {
         if (ReferenceEquals(Rows[0], _openRow))
            break; // відкритий «живий» рядок завжди найновіший — сюди не дійде, лишаємо як запобіжник

         Rows.RemoveAt(0);
      }
   }

   // чи потрапляє запис у поточний діапазон дат (за днем першої детекції, включно з обома кінцями)
   private bool InActiveRange(SignalRecord record)
   {
      DateTime day = record.First.Timestamp.LocalDateTime.Date;
      if (FromDate is { } from && day < from.Date)
         return false;
      if (ToDate is { } to && day > to.Date)
         return false;
      return true;
   }

   // --- фільтр подання (клієнтський, поверх завантаженого набору) ---

   private bool FilterRow(object item)
   {
      if (item is not SignalRecordRow row)
         return false;

      if (row.Count < MinSignalCount)
         return false;

      if (row.SnrDb < MinSnrDb)
         return false;

      // числові нижні межі (0 — фільтр вимкнено, бо значення завжди ≥ 0)
      if (row.FrequencyMhz < MinFrequencyMhz)
         return false;

      if (row.BandwidthKhz < MinBandwidthKhz)
         return false;

      // фільтр за датою — порівнюємо день запису з межами (включно з обома кінцями)
      DateTime day = row.Time.Date;
      if (FromDate is { } from && day < from.Date)
         return false;

      if (ToDate is { } to && day > to.Date)
         return false;

      // нижня межа за часом доби (год:хв) — незалежно від дати
      if (_minTimeOfDay is { } minTime && row.Time.TimeOfDay < minTime)
         return false;

      return true;
   }

   // --- реакції на зміни властивостей ---

   partial void OnUseMedianChanged(bool value)
   {
      OnPropertyChanged(nameof(FrequencyColumnTitle));
      OnPropertyChanged(nameof(FrequencyFilterTitle));

      FrequencyMode mode = CurrentMode;
      foreach (SignalRecordRow row in Rows)
         row.SetMode(mode);
   }

   partial void OnMinFrequencyMhzChanged(double value) => RowsView.Refresh();

   partial void OnMinBandwidthKhzChanged(double value) => RowsView.Refresh();

   // розбираємо «год:хв» (або «год:хв:сек»); порожньо/некоректно — межу за часом знімаємо
   partial void OnMinTimeTextChanged(string value)
   {
      _minTimeOfDay = DateTime.TryParseExact(value?.Trim(), TimeFormats,
                                             CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime parsed)
         ? parsed.TimeOfDay
         : null;
      RowsView.Refresh();
   }

   partial void OnMinSnrDbChanged(double value) => RowsView.Refresh();

   partial void OnMinSignalCountChanged(int value) => RowsView.Refresh();

   partial void OnFromDateChanged(DateTime? value) => OnRangeChanged();

   partial void OnToDateChanged(DateTime? value) => OnRangeChanged();

   // зміна ліміту: для БД у режимі «хвоста» перечитуємо набір під нову межу; для пам'яті — підрізаємо зайве.
   // у діапазонному режимі БД ліміт не діє, тож нічого не робимо
   partial void OnMaxRecordsChanged(int value)
   {
      if (_monitoring.IsPersistentStore)
      {
         if (!RangeActive)
            _ = ReloadAsync();
      }
      else
      {
         TrimToCap();
      }
   }

   /// <summary>
   /// Швидкий вибір: показати лише записи за сьогодні.
   /// </summary>
   [RelayCommand]
   private void Today()
   {
      FromDate = DateTime.Today;
      ToDate   = DateTime.Today;
   }
}
