using System.Windows;
using NetSdrMonitor.Desktop.Settings;
using NetSdrMonitor.Desktop.Shell;

namespace NetSdrMonitor.Desktop;

public partial class App : Application
{
   private SimulationController? _simulation;

   protected override async void OnStartup(StartupEventArgs e)
   {
      base.OnStartup(e);

      var store = new JsonSettingsStore();
      AppSettings settings = store.Load();
      _simulation = new SimulationController(settings);

      var main = new MainWindow(_simulation, store);
      MainWindow = main;
      main.Tray.Attach(_simulation, store);

      main.Show();                              // вантажить трей у будь-якому разі
      if (settings.HideMainWindowOnStartup)
         main.Hide();                           // тихий старт: лишаємось у треї

      await _simulation.StartAsync();           // одразу починаємо імітацію
   }

   protected override async void OnExit(ExitEventArgs e)
   {
      if (_simulation is not null)
         await _simulation.DisposeAsync();
      base.OnExit(e);
   }
}
