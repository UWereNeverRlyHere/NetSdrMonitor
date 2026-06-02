using System.Net;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using NetSdrMonitor.Communication.Monitor;
using NetSdrMonitor.Communication.Server;
using NetSdrMonitor.Core.Abstractions.Communication;
using NetSdrMonitor.Core.Abstractions.Persistence;
using NetSdrMonitor.Desktop.Features.Console;
using NetSdrMonitor.Desktop.Features.Monitor;
using NetSdrMonitor.Desktop.Settings;
using NetSdrMonitor.Domain.Signals;

namespace NetSdrMonitor.Desktop.Shell;

/// <summary>
/// Глобальний контекст застосунку: володіє монітором і моком, керує імітацією (старт/стоп),
/// віддає в UI стан з'єднання, лічильник сигналів і таблицю агрегованих записів. Один екземпляр на застосунок.
/// </summary>
public sealed partial class SimulationController : ObservableObject, IAsyncDisposable
{
   private readonly ISignalRecordRepositoryFactory _repositoryFactory;
   private readonly ILoggerFactory _loggerFactory;
   private readonly SynchronizationContext _ui;
   private readonly DispatcherTimer _uiTimer;

   private AppSettings _settings;
   private SdrMonitor? _monitor;
   private MockLoopbackTransportFactory? _factory;
   private CancellationTokenSource? _drainCts;
   private long _received; // пишеться з фонової задачі, читається таймером UI

   [ObservableProperty]
   [NotifyPropertyChangedFor(nameof(ToggleLabel))]
   private bool _isRunning;

   [ObservableProperty]
   private string _statusText = "Відключено";

   [ObservableProperty]
   private long _signalCount;

   [ObservableProperty]
   private bool _showConsole;

   public SimulationController(
         AppSettings                    settings,
         ISignalRecordRepositoryFactory repositoryFactory,
         ILoggerFactory                 loggerFactory,
         UiLogSink                      logSink)
   {
      _settings          = settings;
      _repositoryFactory = repositoryFactory;
      _loggerFactory     = loggerFactory;
      _showConsole       = settings.ShowConsole;
      Console            = new ConsoleViewModel(logSink);
      _ui                = SynchronizationContext.Current ?? new SynchronizationContext();
      _uiTimer = new DispatcherTimer
      {
            Interval = TimeSpan.FromMilliseconds(250)
      };
      _uiTimer.Tick += (_, _) => SignalCount = Interlocked.Read(ref _received);
   }

   /// <summary>
   /// Таблиця агрегованих записів (джерело даних гріда головного вікна).
   /// </summary>
   public MonitorViewModel Table { get; } = new();

   /// <summary>
   /// Консоль логів монітора й мок-сервера.
   /// </summary>
   public ConsoleViewModel Console { get; }

   public string ToggleLabel => IsRunning ? "Зупинити імітацію" : "Розпочати імітацію";

   /// <summary>
   /// Запам'ятовує нові налаштування; якщо імітація триває — перезапускає її, щоб одразу
   /// застосувати опції монітора, генератора та обране сховище.
   /// </summary>
   public async Task UpdateSettingsAsync(AppSettings settings)
   {
      _settings = settings;
      Table.UseMedian = settings.UseMedianFrequency; // заголовок/режим колонки оновлюємо одразу
      ShowConsole     = settings.ShowConsole;        // показ консолі застосовуємо без перезапуску

      if (!IsRunning)
         return;

      await StopAsync();
      await StartAsync();
   }

   public async Task StartAsync()
   {
      if (IsRunning)
         return;

      // режим частоти з налаштувань ставимо ДО завантаження історії — щоб рядки одразу були в потрібному режимі
      Table.UseMedian = _settings.UseMedianFrequency;

      // сховище під поточну галочку: летке в пам'яті або файлове SQLite (із гарантованою схемою)
      ISignalRecordRepository repository = await _repositoryFactory.CreateAsync(_settings.UseInMemoryStorage);
      await Table.BeginAsync(repository);

      // свіжі мок + монітор під поточні налаштування
      var generator = new RandomSignalGenerator();
      var mock = new MockSignalServer(new IPEndPoint(IPAddress.Loopback, 0), generator, _loggerFactory.CreateLogger<MockSignalServer>(), _settings.Mock);
      _factory               =  new MockLoopbackTransportFactory(mock);
      _monitor               =  new SdrMonitor(_loggerFactory.CreateLogger<SdrMonitor>(), _factory, _settings.Monitor);
      _monitor.StatusChanged += OnStatusChanged;

      Interlocked.Exchange(ref _received, 0);
      SignalCount = 0;
      _drainCts   = new CancellationTokenSource();
      _           = DrainAsync(_monitor, _drainCts.Token); // тримаємо канал порожнім: рахуємо й годуємо таблицю

      _monitor.Start();
      IsRunning = true;
      _uiTimer.Start();
   }

   public async Task StopAsync()
   {
      if (!IsRunning)
         return;

      _uiTimer.Stop();
      if (_drainCts is not null)
         await _drainCts.CancelAsync();

      if (_monitor is not null)
      {
         _monitor.StatusChanged -= OnStatusChanged;
         await _monitor.DisposeAsync(); // гасить монітор і мок-сервер під ним
         _monitor = null;
      }

      await Table.EndAsync(); // дорозбирає чергу, закриває останній запис і чекає запис у сховище

      _factory   = null;
      IsRunning  = false;
      StatusText = "Зупинено";
   }

   public Task ToggleAsync() => IsRunning ? StopAsync() : StartAsync();

   /// <summary>
   /// Очищає журнал «по-чесному»: зупиняє сесію, прибирає сховище й перезапускає мок-сервер.
   /// Так лічильник сигналів і таблиця стартують з нуля синхронно (без розбіжності «лічильник vs рядки»).
   /// </summary>
   public async Task ClearJournalAsync()
   {
      bool wasRunning = IsRunning;
      if (wasRunning)
         await StopAsync(); // закриває останній запис у сховище; далі його ж і чистимо

      // для файлового SQLite треба явно спорожнити той самий файл (історію читаємо при старті);
      // летке сховище нова сесія і так створює порожнім, тож зайвий екземпляр не чіпаємо
      if (!_settings.UseInMemoryStorage)
      {
         ISignalRecordRepository persistent = await _repositoryFactory.CreateAsync(inMemory: false);
         await persistent.ClearAsync();
      }

      if (wasRunning)
      {
         await StartAsync(); // свіжий лічильник + перезапуск мока + порожня історія
      }
      else
      {
         await Table.ClearAsync();
         Interlocked.Exchange(ref _received, 0);
         SignalCount = 0; // лічильник і таблиця лишаються синхронними навіть у зупиненому стані
      }
   }

   public async ValueTask DisposeAsync() => await StopAsync();

   private async Task DrainAsync(ISdrMonitor monitor, CancellationToken ct)
   {
      try
      {
         await foreach (Signal signal in monitor.Signals(ct))
         {
            Interlocked.Increment(ref _received);
            Table.Submit(signal); // черга потокобезпечна; розбір — на UI-таймері таблиці
         }
      }
      catch (OperationCanceledException)
      {
         // штатна зупинка
      }
   }

   private void OnStatusChanged(object? sender, ConnectionStatus status) => _ui.Post(_ => StatusText = Describe(status), null);

   private static string Describe(ConnectionStatus status) => status switch
   {
         ConnectionStatus.Disconnected => "Відключено",
         ConnectionStatus.Connecting   => "Підключення...",
         ConnectionStatus.Connected    => "Підключено",
         ConnectionStatus.Reconnecting => "Відновлення...",
         ConnectionStatus.Stopped      => "Зупинено",
         _                             => status.ToString(),
   };
}
