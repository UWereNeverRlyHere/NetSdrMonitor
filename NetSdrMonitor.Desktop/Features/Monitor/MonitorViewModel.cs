using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetSdrMonitor.Core.Abstractions.Persistence;
using NetSdrMonitor.Domain.Aggregation;
using NetSdrMonitor.Domain.Signals;
// ReSharper disable MemberCanBePrivate.Global

namespace NetSdrMonitor.Desktop.Features.Monitor;

/// <summary>
/// Модель таблиці моніторингу: приймає потік сигналів, зводить їх у записи через доменний
/// агрегатор і віддає рядки в грід. Сортування й фільтрація працюють поверх подання колекції,
/// частота показується за медіаною або першим сигналом. Закриті записи осідають у сховищі.
/// </summary>
/// <remarks>
/// Потоковість: сигнали кладуться у чергу з фонової задачі (<see cref="Submit"/>), а розбираються
/// батчами на UI-потоці за таймером — так грід не захлинається за високого темпу і не блокує вікно.
/// </remarks>
public sealed partial class MonitorViewModel : ObservableObject
{
   // обмеження розбору за один тік — захист UI від сплеску за дуже високого темпу прийому
   private const int MaxDrainPerTick = 5_000;

   // допустимі формати вводу нижньої межі за часом доби
   private static readonly string[] TimeFormats = { "H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss" };

   private readonly ConcurrentQueue<Signal> _inbox = new();
   private readonly DispatcherTimer _pump;

   private ISignalRecordRepository? _repository;
   private SignalAggregator? _aggregator;
   private SignalRecordRow? _openRow; // рядок поточного відкритого запису (оновлюємо на кожній детекції)
   private Task _persistChain = Task.CompletedTask; // ланцюг записів у сховище — щоб дочекатися їх перед очисткою

   private bool _persistent; // true — сховище файлове (БД): діапазон дат читаємо запитом, ліміт у ньому вимкнено
   private CancellationTokenSource? _loadCts; // скасування попереднього перезавантаження набору з БД

   [ObservableProperty]
   private bool _useMedian = true; // за замовчуванням показуємо медіану за частотою

   // максимум рядків на екрані: для пам'яті — жорстка межа, для БД — розмір стартового «хвоста»
   [ObservableProperty]
   private int _maxRecords = 500;

   // триває перезавантаження набору з БД — UI показує індикатор зайнятості
   [ObservableProperty]
   private bool _isLoading;

   [ObservableProperty]
   private double _minFrequencyMhz;

   [ObservableProperty]
   private double _minBandwidthKhz;

   // нижня межа за часом доби у форматі «год:хв»; порожньо/некоректно — фільтр за часом вимкнено
   [ObservableProperty]
   private string _minTimeText = string.Empty;

   private TimeSpan? _minTimeOfDay;

   [ObservableProperty]
   private double _minSnrDb;

   [ObservableProperty]
   private int _minSignalCount;

   // межі фільтра за датою (за днем, включно); null — межа не задана
   [ObservableProperty]
   private DateTime? _fromDate;

   [ObservableProperty]
   private DateTime? _toDate;

   public MonitorViewModel()
   {
      Rows     = [];
      RowsView = CollectionViewSource.GetDefaultView(Rows);
      RowsView.Filter = FilterRow;

      // за замовчуванням — найновіші зверху: дата й час сортуються як єдине ціле (спершу день,
      // потім час доби), тож «найсвіжіше» — це найновіший момент, а не лише час доби.
      // клік по заголовку Дати/Часу перемикає напрям одразу для пари (див. MainWindow.OnGridSorting)
      RowsView.SortDescriptions.Add(new SortDescription(nameof(SignalRecordRow.Date), ListSortDirection.Descending));
      RowsView.SortDescriptions.Add(new SortDescription(nameof(SignalRecordRow.Time), ListSortDirection.Descending));

      // склад подання змінюється при додаванні рядків і при оновленні фільтра — оновлюємо лічильник
      ((INotifyCollectionChanged)RowsView).CollectionChanged += (_, _) => OnPropertyChanged(nameof(VisibleCount));

      _pump = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
      _pump.Tick += (_, _) => Drain();
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

   /// <summary>
   /// Готує модель до нової сесії: підхоплює сховище, завантажує стартовий набір і відкриває агрегатор.
   /// </summary>
   public async Task BeginAsync(ISignalRecordRepository repository, bool persistent, int maxRecords, CancellationToken cancellationToken = default)
   {
      _repository = repository;
      _persistent = persistent;
      _maxRecords = maxRecords > 0 ? maxRecords : 1; // поле напряму — без перезавантаження у сетері
      OnPropertyChanged(nameof(MaxRecords));

      _inbox.Clear(); // прибираємо можливий хвіст сигналів попередньої сесії
      _openRow      = null;
      _persistChain = Task.CompletedTask;

      _aggregator = SignalAggregator.Create()
         .OnRecordOpened(OnRecordOpened)
         .OnSignalAppended(OnSignalAppended)
         .OnRecordClosed(OnRecordClosed)
         .Build();

      // стартовий набір: для БД — свіжий «хвіст» (або діапазон, якщо фільтр дат уже стоїть); для пам'яті — порожньо
      await ReloadAsync();

      _pump.Start();
   }

   /// <summary>
   /// Кладе прийнятий сигнал у чергу на розбір (викликається з фонової задачі прийому).
   /// </summary>
   public void Submit(Signal signal) => _inbox.Enqueue(signal);

   /// <summary>
   /// Завершує сесію: дорозбирає чергу, закриває останній відкритий запис і чекає, доки всі
   /// закриті записи осядуть у сховищі (щоб подальша очистка не розійшлася із фоновим записом).
   /// </summary>
   public async Task EndAsync()
   {
      _pump.Stop();
      Drain();
      _aggregator?.Dispose(); // закриває останній запис -> OnRecordClosed -> ланцюг запису
      _aggregator = null;
      await _persistChain;
   }

   /// <summary>
   /// Очищає таблицю й сховище — починаємо журнал з чистого аркуша.
   /// </summary>
   public async Task ClearAsync(CancellationToken cancellationToken = default)
   {
      if (_repository is not null)
         await _repository.ClearAsync(cancellationToken);

      Rows.Clear();
      _openRow = null;
   }

   private FrequencyMode CurrentMode => UseMedian ? FrequencyMode.Median : FrequencyMode.First;

   private void Drain()
   {
      if (_aggregator is null)
         return;

      int processed = 0;
      while (processed < MaxDrainPerTick && _inbox.TryDequeue(out Signal signal))
      {
         _aggregator.Process(signal);
         processed++;
      }
   }

   private void OnRecordOpened(SignalRecord record)
   {
      // у режимі БД з активним діапазоном дат показуємо лише ті записи, що в нього потрапляють,
      // і НЕ обрізаємо за лімітом (діапазонний перегляд необмежений)
      if (_persistent && RangeActive && !InActiveRange(record))
      {
         _openRow = null; // запис поза діапазоном: у таблицю не додаємо й «живим» не відстежуємо
         return;
      }

      var row = new SignalRecordRow(record, CurrentMode) { NeedFadeIn = true }; // новий рядок з'явиться з анімацією
      _openRow = row;
      Rows.Add(row);

      if (!(_persistent && RangeActive))
         TrimToCap(); // звичайний «хвіст»: тримаємо межу, відкидаючи найстаріші
   }

   private void OnSignalAppended(SignalRecord record, Signal signal) => _openRow?.Refresh();

   private void OnRecordClosed(SignalRecord record, RecordCloseReason reason)
   {
      _openRow?.Refresh();
      // запис уже закритий і незмінний; зберігаємо по черзі, щоб мати один awaitable «усі записи завершені»
      _persistChain = ChainPersistAsync(_persistChain, record);
   }

   private async Task ChainPersistAsync(Task previous, SignalRecord record)
   {
      await previous; // БД і так серіалізує запис — черга лише дає нам точку очікування
      await PersistAsync(record);
   }

   private async Task PersistAsync(SignalRecord record)
   {
      try
      {
         if (_repository is not null)
            await _repository.AddAsync(record);
      }
      catch
      {
         // збій сховища не має валити прийом сигналів; журнал помилок з'явиться разом із Serilog
      }
   }

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
      if (_persistent)
      {
         if (!RangeActive)
            _ = ReloadAsync();
      }
      else
      {
         TrimToCap();
      }
   }

   // діапазон дат: БД перечитує набір (усі записи проміжку, без ліміту); пам'ять фільтрує набір клієнтськи
   private void OnRangeChanged()
   {
      if (_persistent)
         _ = ReloadAsync();
      else
         RowsView.Refresh();
   }

   private bool RangeActive => FromDate is not null || ToDate is not null;

   private DateTimeOffset RangeFrom => FromDate is { } from
      ? new DateTimeOffset(DateTime.SpecifyKind(from.Date, DateTimeKind.Local))
      : DateTimeOffset.MinValue;

   // верхня межа — наступна північ після останнього дня (проміжок напіввідкритий: [from; to))
   private DateTimeOffset RangeTo => ToDate is { } to
      ? new DateTimeOffset(DateTime.SpecifyKind(to.Date.AddDays(1), DateTimeKind.Local))
      : DateTimeOffset.MaxValue;

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

   // перечитує набір зі сховища: діапазон дат (БД) або свіжий «хвіст» на MaxRecords.
   // попереднє завантаження скасовуємо, щоб швидкі зміни фільтра не наклались
   private async Task ReloadAsync()
   {
      if (_repository is null)
         return;

      _loadCts?.Cancel();
      var cts = new CancellationTokenSource();
      _loadCts = cts;
      CancellationToken token = cts.Token;

      IsLoading = true;
      try
      {
         IReadOnlyList<SignalRecord> data = _persistent && RangeActive
            ? await _repository.GetInRangeAsync(RangeFrom, RangeTo, token)
            : await _repository.GetRecentAsync(MaxRecords, token);

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
