using System.ComponentModel;
using System.Windows;
using NetSdrMonitor.Desktop.Features.Settings;
using NetSdrMonitor.Desktop.Settings;
using NetSdrMonitor.Desktop.Shell;

namespace NetSdrMonitor.Desktop;

/// <summary>
/// Головне вікно. «Закриття» ховає його у трей; вихід — лише через меню трея.
/// </summary>
public partial class MainWindow : Window
{
   private readonly SimulationController _simulation;
   private readonly JsonSettingsStore _store;

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

   protected override void OnClosing(CancelEventArgs e)
   {
      base.OnClosing(e);
      e.Cancel = true; // не закриваємось — ховаємось у трей
      Hide();
   }
}
