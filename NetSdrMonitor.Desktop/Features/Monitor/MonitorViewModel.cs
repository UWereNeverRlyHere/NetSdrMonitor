using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
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

   private readonly ConcurrentQueue<Signal> _inbox = new();
   private readonly DispatcherTimer _pump;

   private ISignalRecordRepository? _repository;
   private SignalAggregator? _aggregator;
   private SignalRecordRow? _openRow; // рядок поточного відкритого запису (оновлюємо на кожній детекції)
   private Task _persistChain = Task.CompletedTask; // ланцюг записів у сховище — щоб дочекатися їх перед очисткою

   [ObservableProperty]
   private bool _useMedian = true; // за замовчуванням показуємо медіану за частотою

   [ObservableProperty]
   private string _searchText = string.Empty;

   [ObservableProperty]
   private double _minSnrDb;

   [ObservableProperty]
   private int _minSignalCount;

   public MonitorViewModel()
   {
      Rows     = [];
      RowsView = CollectionViewSource.GetDefaultView(Rows);
      RowsView.Filter = FilterRow;

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
   /// Готує модель до нової сесії: підхоплює сховище, відновлює історію й відкриває агрегатор.
   /// </summary>
   public async Task BeginAsync(ISignalRecordRepository repository, CancellationToken cancellationToken = default)
   {
      _repository = repository;

      _inbox.Clear(); // прибираємо можливий хвіст сигналів попередньої сесії
      Rows.Clear();
      _openRow      = null;
      _persistChain = Task.CompletedTask;

      // показуємо вже збережені записи (для SQLite — переживають перезапуск; для пам'яті — порожньо)
      IReadOnlyList<SignalRecord> history = await repository.GetAllAsync(cancellationToken);
      FrequencyMode mode = CurrentMode;
      foreach (SignalRecord record in history)
         Rows.Add(new SignalRecordRow(record, mode));

      _aggregator = SignalAggregator.Create()
         .OnRecordOpened(OnRecordOpened)
         .OnSignalAppended(OnSignalAppended)
         .OnRecordClosed(OnRecordClosed)
         .Build();

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
      var row = new SignalRecordRow(record, CurrentMode);
      _openRow = row;
      Rows.Add(row);
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

      if (string.IsNullOrWhiteSpace(SearchText))
         return true;

      // вільний пошук по текстовому представленню УСІХ колонок рядка (час + числові), культурою показу
      string haystack = string.Create(CultureInfo.CurrentCulture,
         $"{row.Time:HH:mm:ss.fff} {row.FrequencyMhz:F3} {row.BandwidthKhz:F1} {row.SnrDb:F1} {row.Count}");
      return haystack.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase);
   }

   partial void OnUseMedianChanged(bool value)
   {
      OnPropertyChanged(nameof(FrequencyColumnTitle));

      FrequencyMode mode = CurrentMode;
      foreach (SignalRecordRow row in Rows)
         row.SetMode(mode);
   }

   partial void OnSearchTextChanged(string value) => RowsView.Refresh();

   partial void OnMinSnrDbChanged(double value) => RowsView.Refresh();

   partial void OnMinSignalCountChanged(int value) => RowsView.Refresh();
}
