using System.Net;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging.Abstractions;
using NetSdrMonitor.Communication.Monitor;
using NetSdrMonitor.Communication.Server;
using NetSdrMonitor.Core.Abstractions.Communication;
using NetSdrMonitor.Desktop.Settings;
using NetSdrMonitor.Domain.Signals;

namespace NetSdrMonitor.Desktop.Shell;

/// <summary>
/// Глобальний контекст застосунку: володіє монітором і моком, керує імітацією (старт/стоп),
/// віддає в UI стан з'єднання та лічильник прийнятих сигналів. Один екземпляр на застосунок.
/// </summary>
public sealed partial class SimulationController : ObservableObject, IAsyncDisposable
{
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

   public SimulationController(AppSettings settings)
   {
      _settings = settings;
      _ui       = SynchronizationContext.Current ?? new SynchronizationContext();
      _uiTimer = new DispatcherTimer
      {
            Interval = TimeSpan.FromMilliseconds(250)
      };
      _uiTimer.Tick += (_, _) => SignalCount = Interlocked.Read(ref _received);
   }

   public string ToggleLabel => IsRunning ? "Зупинити імітацію" : "Розпочати імітацію";

   /// <summary>
   /// Запам'ятовує нові налаштування; якщо імітація триває — перезапускає її, щоб одразу
   /// застосувати опції монітора й генератора.
   /// </summary>
   public async Task UpdateSettingsAsync(AppSettings settings)
   {
      _settings = settings;
      if (!IsRunning)
         return;

      await StopAsync();
      await StartAsync();
   }

   public Task StartAsync()
   {
      if (IsRunning)
         return Task.CompletedTask;

      // свіжі мок + монітор під поточні налаштування
      var generator = new RandomSignalGenerator();
      var mock = new MockSignalServer(new IPEndPoint(IPAddress.Loopback, 0), generator, NullLogger<MockSignalServer>.Instance, _settings.Mock);
      _factory               =  new MockLoopbackTransportFactory(mock);
      _monitor               =  new SdrMonitor(NullLogger<SdrMonitor>.Instance, _factory, _settings.Monitor);
      _monitor.StatusChanged += OnStatusChanged;

      Interlocked.Exchange(ref _received, 0);
      SignalCount = 0;
      _drainCts   = new CancellationTokenSource();
      _           = DrainAsync(_monitor, _drainCts.Token); // тримаємо канал порожнім і рахуємо

      _monitor.Start();
      IsRunning = true;
      _uiTimer.Start();
      return Task.CompletedTask;
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

      _factory   = null;
      IsRunning  = false;
      StatusText = "Зупинено";
   }

   public Task ToggleAsync() => IsRunning ? StopAsync() : StartAsync();

   public async ValueTask DisposeAsync() => await StopAsync();

   private async Task DrainAsync(ISdrMonitor monitor, CancellationToken ct)
   {
      try
      {
         await foreach (Signal _ in monitor.Signals(ct))
            Interlocked.Increment(ref _received);
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
