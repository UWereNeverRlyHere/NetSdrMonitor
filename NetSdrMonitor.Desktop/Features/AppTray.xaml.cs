using System.Windows;
using System.Windows.Controls;
using NetSdrMonitor.Desktop.Features.Settings;
using NetSdrMonitor.Desktop.Settings;
using NetSdrMonitor.Desktop.Shell;

namespace NetSdrMonitor.Desktop.Features;

/// <summary>
/// Іконка та контекстне меню в треї: перемикає імітацію, відкриває вікна й налаштування, виходить.
/// </summary>
public partial class AppTray : UserControl
{
   private SimulationController? _simulation;
   private JsonSettingsStore? _store;

   public AppTray()
   {
      InitializeComponent();
   }

   /// <summary>
   /// Прив'язує трей до контролера імітації й сховища налаштувань (виклик із композиційного кореня).
   /// </summary>
   public void Attach(SimulationController simulation, JsonSettingsStore store)
   {
      _simulation = simulation;
      _store      = store;
   }

   private void OnMenuOpened(object sender, RoutedEventArgs e)
   {
      if (_simulation is not null)
         ToggleItem.Header = _simulation.ToggleLabel;
   }

   private void OnToggle(object sender, RoutedEventArgs e) => _ = _simulation?.ToggleAsync();

   private void OnShowMain(object sender, RoutedEventArgs e)
   {
      if (Application.Current.MainWindow is not { } main)
         return;

      main.Show();
      if (main.WindowState == WindowState.Minimized)
         main.WindowState = WindowState.Normal;
      main.Activate();
   }

   private void OnSettings(object sender, RoutedEventArgs e)
   {
      if (_store is null || _simulation is null)
         return;

      SettingsWindow.OpenOrActivate(Application.Current.MainWindow, _store.Load(), _store,
         saved => _ = _simulation.UpdateSettingsAsync(saved));
   }

   private void OnExit(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
