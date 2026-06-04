using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NetSdrMonitor.Core.Abstractions.Communication;
using NetSdrMonitor.Core.Features.Monitoring;
using NetSdrMonitor.Desktop.Features.Console;
using NetSdrMonitor.Desktop.Features.Monitor;
using NetSdrMonitor.Desktop.Settings;

namespace NetSdrMonitor.Desktop.Shell;

/// <summary>
/// Shell-модель головного вікна: керує сесією приймання (старт/стоп/очистка), віддає в UI стан лінії та
/// лічильник сигналів і агрегує під-моделі — таблицю записів і консоль логів. Уся прикладна логіка
/// (приймання, агрегація, персист, читання історії) живе в Core-сервісах; тут лише делегування й маршалінг.
/// </summary>
public sealed partial class SimulationController : ObservableObject, IAsyncDisposable
{
    private readonly MonitoringService _monitoring;
    private readonly SynchronizationContext _ui;
    private readonly DispatcherTimer _uiTimer;

    private AppSettings _settings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleLabel))]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = "Відключено";

    [ObservableProperty]
    private long _signalCount;

    [ObservableProperty]
    private bool _showConsole;

    public SimulationController(MonitoringService monitoring, MonitorViewModel table, ConsoleViewModel console, AppSettings settings)
    {
        _monitoring = monitoring;
        _settings   = settings;
        Table       = table;
        Console     = console;

        _showConsole     = settings.ShowConsole;
        Table.UseMedian  = settings.UseMedianFrequency;
        Table.MaxRecords = settings.MaxUiRecords;

        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _monitoring.StatusChanged += OnStatusChanged;

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _uiTimer.Tick += (_, _) => SignalCount = _monitoring.ReceivedCount;
    }

    /// <summary>
    /// Таблиця агрегованих записів (джерело даних гріда головного вікна).
    /// </summary>
    public MonitorViewModel Table { get; }

    /// <summary>
    /// Консоль логів монітора й мок-сервера.
    /// </summary>
    public ConsoleViewModel Console { get; }

    public string ToggleLabel => IsRunning ? "Зупинити імітацію" : "Розпочати імітацію";

    /// <summary>
    /// Запам'ятовує нові настройки; режим частоти й ліміт застосовуємо одразу, а якщо сесія триває —
    /// перезапускаємо її, щоб підхопити опції монітора, генератора й обране сховище.
    /// </summary>
    public async Task UpdateSettingsAsync(AppSettings settings)
    {
        _settings        = settings;
        Table.UseMedian  = settings.UseMedianFrequency;
        Table.MaxRecords = settings.MaxUiRecords;
        ShowConsole      = settings.ShowConsole;

        if (!IsRunning)
            return;

        await StopAsync();
        await StartAsync();
    }

    public async Task StartAsync()
    {
        if (IsRunning)
            return;

        Table.UseMedian = _settings.UseMedianFrequency; // режим колонки — до завантаження стартового набору
        await _monitoring.StartAsync();                 // підіймає сесію й сигналить таблиці перечитати набір

        IsRunning = true;
        _uiTimer.Start();
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
            return;

        _uiTimer.Stop();
        await _monitoring.StopAsync();

        SignalCount = _monitoring.ReceivedCount;
        IsRunning   = false;
        StatusText  = "Зупинено";
    }

    public Task ToggleAsync() => IsRunning ? StopAsync() : StartAsync();

    /// <summary>
    /// Очищає журнал «по-чесному»: зупиняє сесію, прибирає сховище й перезапускає (якщо була активна),
    /// щоб лічильник і таблиця стартували з нуля синхронно.
    /// </summary>
    public async Task ClearJournalAsync()
    {
        bool wasRunning = IsRunning;
        if (wasRunning)
            await StopAsync();

        await _monitoring.ClearAsync(); // чистить сховище сесії + сигналить таблиці перечитати порожньо

        if (wasRunning)
            await StartAsync();
        else
            SignalCount = 0;
    }

    public async ValueTask DisposeAsync()
    {
        _monitoring.StatusChanged -= OnStatusChanged;
        await _monitoring.DisposeAsync();
    }

    private void OnStatusChanged(ConnectionStatus status) => _ui.Post(_ => StatusText = Describe(status), null);

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
