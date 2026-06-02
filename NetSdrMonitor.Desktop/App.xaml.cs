using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetSdrMonitor.Core.Abstractions.Persistence;
using NetSdrMonitor.Desktop.Features.Console;
using NetSdrMonitor.Desktop.Logging;
using NetSdrMonitor.Desktop.Settings;
using NetSdrMonitor.Desktop.Shell;
using NetSdrMonitor.Infrastructure.Persistence.Sqlite;

namespace NetSdrMonitor.Desktop;

public partial class App : Application
{
   private ServiceProvider? _services;
   private ILoggerFactory? _loggerFactory;
   private SimulationController? _simulation;

   protected override async void OnStartup(StartupEventArgs e)
   {
      base.OnStartup(e);

      // мінімальний композиційний корінь: лише реєстрація сховища (фабрика контексту + бутстрапер + фабрика порту).
      // Конкретику (EF/SQLite) знає тільки тут — решта застосунку бачить лише порт.
      var collection = new ServiceCollection();
      collection.AddSqliteSignalStorage();
      _services = collection.BuildServiceProvider();
      var repositoryFactory = _services.GetRequiredService<ISignalRecordRepositoryFactory>();

      // логи монітора й мока йдуть у консоль застосунку; Trace «на кадр» відсікаємо порогом Debug
      var logSink = new UiLogSink();
      _loggerFactory = LoggerFactory.Create(builder =>
      {
         builder.SetMinimumLevel(LogLevel.Debug);
         builder.AddProvider(new UiLoggerProvider(logSink, LogLevel.Debug));
      });

      var store = new JsonSettingsStore();
      AppSettings settings = store.Load();
      _simulation = new SimulationController(settings, repositoryFactory, _loggerFactory, logSink);

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

      if (_services is not null)
         await _services.DisposeAsync();

      _loggerFactory?.Dispose();

      base.OnExit(e);
   }
}
