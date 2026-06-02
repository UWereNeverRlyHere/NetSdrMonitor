using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NetSdrMonitor.Desktop.Behaviors;
using NetSdrMonitor.Desktop.Features.Monitor;
using NetSdrMonitor.Desktop.Features.Settings;
using NetSdrMonitor.Desktop.Settings;
using NetSdrMonitor.Desktop.Shell;

namespace NetSdrMonitor.Desktop;

/// <summary>
/// Головне вікно. «Закриття» ховає його у трей; вихід — лише через меню трея.
/// Розкладку колонок (порядок і ширина) відновлюємо при показі та зберігаємо при змінах.
/// </summary>
public partial class MainWindow : Window
{
    private const double DefaultConsoleHeight = 180; // ≈28% від типової висоти вікна

    private readonly SimulationController _simulation;
    private readonly JsonSettingsStore _store;

    private bool _layoutApplied;
    private double _consoleHeightPx = DefaultConsoleHeight;

    public MainWindow(SimulationController simulation, JsonSettingsStore store)
    {
        InitializeComponent();
        _simulation = simulation;
        _store      = store;
        DataContext = simulation;

        AppSettings settings = _store.Load();
        if (settings.ConsoleHeight > 0)
            _consoleHeightPx = settings.ConsoleHeight;

        _simulation.PropertyChanged += OnSimulationPropertyChanged;
        ApplyConsole(_simulation.ShowConsole);
    }

    private void OnToggle(object sender, RoutedEventArgs e) => _ = _simulation.ToggleAsync();

    private void OnSettings(object sender, RoutedEventArgs e) => SettingsWindow.OpenOrActivate(this, _store.Load(), _store, saved => _ = _simulation.UpdateSettingsAsync(saved));

    private void OnClearLog(object sender, RoutedEventArgs e) => _ = _simulation.ClearJournalAsync();

    // ПКМ по рядку — виділяємо його, щоб контекстне меню гріда діяло саме на нього
    private void OnRowRightClick(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? source = e.OriginalSource as DependencyObject;
        while (source is not null and not DataGridRow)
            source = VisualTreeHelper.GetParent(source);

        if (source is DataGridRow row)
            row.IsSelected = true;
    }

    private void OnShowSignals(object sender, RoutedEventArgs e)
    {
        if (SignalsGrid.SelectedItem is SignalRecordRow row)
            new SignalDetailsWindow(row)
            {
                        Owner = this
            }.Show();
    }

    private void OnSimulationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SimulationController.ShowConsole))
            ApplyConsole(_simulation.ShowConsole);
    }

    // показ/приховування нижньої консолі; ховаючи, запам'ятовуємо її висоту, щоб повернути ту саму
    private void ApplyConsole(bool show)
    {
        if (show)
        {
            ConsoleRow.Height          = new GridLength(_consoleHeightPx);
            ConsoleSplitter.Visibility = Visibility.Visible;
            ConsoleHost.Visibility     = Visibility.Visible;
        }
        else
        {
            if (ConsoleRow.Height is { IsAbsolute: true, Value: > 0 })
                _consoleHeightPx = ConsoleRow.Height.Value;

            ConsoleRow.Height          = new GridLength(0);
            ConsoleSplitter.Visibility = Visibility.Collapsed;
            ConsoleHost.Visibility     = Visibility.Collapsed;
        }
    }

    private void OnResetFilter(object sender, RoutedEventArgs e)
    {
        _simulation.Table.SearchText     = string.Empty;
        _simulation.Table.MinSnrDb       = 0;
        _simulation.Table.MinSignalCount = 0;
    }

    private void OnGridLoaded(object sender, RoutedEventArgs e)
    {
        if (_layoutApplied)
            return;

        ColumnLayout.Apply(SignalsGrid, _store.Load().Columns);
        _layoutApplied = true;
    }

    // перетягування колонки — зберігаємо одразу; ширини доберуться при ховуванні вікна
    private void OnColumnsChanged(object? sender, DataGridColumnEventArgs e) => SaveLayout();

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        SaveLayout();    // фіксуємо порядок/ширину колонок і висоту консолі перед тим, як сховати у трей
        e.Cancel = true; // не закриваємось — ховаємось у трей
        Hide();
    }

    // зберігаємо висоту консолі завжди; розкладку колонок — лише коли грід уже реалізувався
    private void SaveLayout()
    {
        if (ConsoleRow.Height is { IsAbsolute: true, Value: > 0 })
            _consoleHeightPx = ConsoleRow.Height.Value;

        AppSettings settings = _store.Load() with
        {
                    ConsoleHeight = _consoleHeightPx
        };
        if (_layoutApplied)
            settings = settings with
            {
                        Columns = ColumnLayout.Capture(SignalsGrid)
            };

        _store.Save(settings);
    }
}
