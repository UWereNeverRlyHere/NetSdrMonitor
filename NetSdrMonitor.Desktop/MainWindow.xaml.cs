using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
   private readonly SimulationController _simulation;
   private readonly JsonSettingsStore _store;

   private bool _layoutApplied;

   public MainWindow(SimulationController simulation, JsonSettingsStore store)
   {
      InitializeComponent();
      _simulation = simulation;
      _store      = store;
      DataContext = simulation;
   }

   private void OnToggle(object sender, RoutedEventArgs e) => _ = _simulation.ToggleAsync();

   private void OnSettings(object sender, RoutedEventArgs e) =>
      SettingsWindow.OpenOrActivate(this, _store.Load(), _store, saved => _ = _simulation.UpdateSettingsAsync(saved));

   private void OnClearLog(object sender, RoutedEventArgs e) => _ = _simulation.ClearJournalAsync();

   // ПКМ по рядку — виділяємо його, щоб контекстне меню гріда діяло саме на нього
   private void OnRowRightClick(object sender, MouseButtonEventArgs e)
   {
      if (sender is DataGridRow row)
         row.IsSelected = true;
   }

   private void OnShowSignals(object sender, RoutedEventArgs e)
   {
      if (SignalsGrid.SelectedItem is SignalRecordRow row)
         new SignalDetailsWindow(row) { Owner = this }.Show();
   }

   private void OnResetFilter(object sender, RoutedEventArgs e)
   {
      _simulation.Table.SearchText      = string.Empty;
      _simulation.Table.MinSnrDb        = 0;
      _simulation.Table.MinSignalCount  = 0;
   }

   private void OnGridLoaded(object sender, RoutedEventArgs e)
   {
      if (_layoutApplied)
         return;

      ColumnLayout.Apply(SignalsGrid, _store.Load().Columns);
      _layoutApplied = true;
   }

   // перетягування колонки — зберігаємо одразу; ширини доберуться при ховуванні вікна
   private void OnColumnsChanged(object? sender, DataGridColumnEventArgs e) => SaveColumnLayout();

   protected override void OnClosing(CancelEventArgs e)
   {
      base.OnClosing(e);
      SaveColumnLayout(); // фіксуємо ширини/порядок перед тим, як сховати вікно у трей
      e.Cancel = true;    // не закриваємось — ховаємось у трей
      Hide();
   }

   private void SaveColumnLayout()
   {
      if (!_layoutApplied)
         return;

      AppSettings settings = _store.Load() with { Columns = ColumnLayout.Capture(SignalsGrid) };
      _store.Save(settings);
   }
}
