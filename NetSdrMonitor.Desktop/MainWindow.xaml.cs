using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NetSdrMonitor.Desktop.Behaviors;
using NetSdrMonitor.Desktop.Features.Monitor;
using NetSdrMonitor.Desktop.Features.Settings;
using NetSdrMonitor.Desktop.Settings;
using NetSdrMonitor.Desktop.Shell;
using Wpf.Ui.Controls;

namespace NetSdrMonitor.Desktop;

/// <summary>
/// Головне вікно. «Закриття» ховає його у трей; вихід — лише через меню трея.
/// Розкладку колонок (порядок і ширина) відновлюємо при показі та зберігаємо при змінах.
/// </summary>
public partial class MainWindow : FluentWindow
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

        // розмір/позицію вікна веде окремий байндер: відновлює при показі й зберігає наживо при змінах.
        // він живе разом із вікном через підписки на його події, тож окреме поле не потрібне
        _ = new WindowPlacementBinder(this,
            () => _store.Load().MainWindowPlacement,
            placement => _store.Save(_store.Load() with { MainWindowPlacement = placement }));

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
            new SignalDetailsWindow(row, _store)
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
        _simulation.Table.MinFrequencyMhz = 0;
        _simulation.Table.MinBandwidthKhz = 0;
        _simulation.Table.MinTimeText     = string.Empty;
        _simulation.Table.MinSnrDb        = 0;
        _simulation.Table.MinSignalCount  = 0;
        _simulation.Table.FromDate        = null;
        _simulation.Table.ToDate          = null;
    }

    // ключі пов'язаної пари колонок, що сортуються як одне ціле (день, потім час доби)
    private static readonly string[] DateTimeKeys = { "date", "time" };

    // Дата й Час — одна логічна вісь (момент детекції), тож сортуємо їх разом: інакше клік по «Час»
    // упорядкував би лише за часом доби й до найсвіжіших могли б домішатись учорашні записи
    private void OnGridSorting(object sender, DataGridSortingEventArgs e)
    {
        if (ColumnLayout.GetKey(e.Column) is not { } key || !DateTimeKeys.Contains(key))
            return; // решта колонок — штатне сортування DataGrid

        e.Handled = true;

        // напрям беремо за поточною стрілкою пари; перший клік по «свіжій» парі дає спадання (новіші зверху)
        DataGridColumn? dateColumn = ColumnByKey("date");
        ListSortDirection direction = dateColumn?.SortDirection == ListSortDirection.Descending
            ? ListSortDirection.Ascending
            : ListSortDirection.Descending;

        ApplyDateTimeSort(direction);
    }

    // переписує сортування подання на пару [Дата, Час] в одному напрямі й ставить стрілку на обидві колонки
    private void ApplyDateTimeSort(ListSortDirection direction)
    {
        ICollectionView view = _simulation.Table.RowsView;
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(nameof(SignalRecordRow.Date), direction));
            view.SortDescriptions.Add(new SortDescription(nameof(SignalRecordRow.Time), direction));
        }

        // стрілку лишаємо лише на парі «Дата/Час», з інших колонок знімаємо
        foreach (DataGridColumn column in SignalsGrid.Columns)
            column.SortDirection = ColumnLayout.GetKey(column) is { } key && DateTimeKeys.Contains(key)
                ? direction
                : null;
    }

    private DataGridColumn? ColumnByKey(string key) =>
        SignalsGrid.Columns.FirstOrDefault(c => ColumnLayout.GetKey(c) == key);

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
