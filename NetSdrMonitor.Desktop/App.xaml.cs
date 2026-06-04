using System.Diagnostics.CodeAnalysis;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetSdrMonitor.Core.Abstractions.Persistence;
using NetSdrMonitor.Core.Features.Monitoring;
using NetSdrMonitor.Desktop.Features.Console;
using NetSdrMonitor.Desktop.Features.Monitor;
using NetSdrMonitor.Desktop.Features.Settings;
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

   [SuppressMessage("ReSharper", "AsyncVoidMethod")]
   protected async override void OnStartup(StartupEventArgs e)
   {
      base.OnStartup(e);

      try
      {
         var collection = new ServiceCollection();
         collection.AddSqliteSignalStorage();
         _services = collection.BuildServiceProvider();
         var repositoryFactory = _services.GetRequiredService<ISignalRecordRepositoryFactory>();

         var logSink = new UiLogSink();
         _loggerFactory = LoggerFactory.Create(builder =>
         {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(new UiLoggerProvider(logSink, LogLevel.Debug));
         });

         var store = new JsonSettingsStore();
         AppSettings settings = store.Load();

         // прикладні сервіси (Core) + їх композиція: фабрики читають актуальні настройки на кожен старт
         var session        = new RecordSession();
         var monitorFactory = new SdrMonitorFactory(store, _loggerFactory);
         var storeFactory   = new SessionStoreFactory(store, repositoryFactory);
         var monitoring     = new MonitoringService(monitorFactory, storeFactory, session);
         var feed           = new RecordFeed(session);

         // тонкі під-моделі + shell, що їх агрегує
         var table   = new MonitorViewModel(monitoring, feed);
         var console = new ConsoleViewModel(logSink);
         _simulation = new SimulationController(monitoring, table, console, settings);

         var main = new MainWindow(_simulation, store);
         MainWindow = main;
         main.Tray.Attach(_simulation, store);

         if (settings.HideMainWindowOnStartup)
            main.Hide();
         else
            main.Show();

         // тему застосовуємо вже ПІСЛЯ показу вікна: ApplicationThemeManager і SystemThemeWatcher
         // коректно чіпляються лише коли у вікна є дескриптор. Якщо застосувати до показу — тема
         // «недозастосовується» (видно лише після ручної зміни), а вікно лишається в крихкому стані,
         // через що модальні діалоги (напр. налаштування) при закритті можуть ховати головне вікно
         ThemeApplier.Initialize(main, settings.Theme);

         await _simulation.StartAsync();
      }
      catch (Exception ex)
      {
         // старт не вдався (сховище, порт тощо) — показуємо причину й коректно завершуємось, а не падаємо мовчки
         MessageBox.Show(ex.Message, "NetSdrMonitor — помилка запуску",
                         MessageBoxButton.OK, MessageBoxImage.Error);
         Shutdown(-1);
      }
   }

   [SuppressMessage("ReSharper", "AsyncVoidMethod")]
   protected async override void OnExit(ExitEventArgs e)
   {
      try
      {
         if (_simulation is not null)
            await _simulation.DisposeAsync();

         if (_services is not null)
            await _services.DisposeAsync();

         _loggerFactory?.Dispose();
      }
      catch (Exception ex)
      {
         // помилки звільнення на виході не критичні — застосунок усе одно закривається
         System.Diagnostics.Debug.WriteLine($"Shutdown cleanup failed: {ex}");
      }
      finally
      {
         base.OnExit(e);
      }
   }
}
