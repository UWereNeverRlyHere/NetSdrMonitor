using System.Diagnostics.CodeAnalysis;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetSdrMonitor.Core.Abstractions.Persistence;
using NetSdrMonitor.Desktop.Features.Console;
using NetSdrMonitor.Desktop.Logging;
using NetSdrMonitor.Desktop.Settings;
using NetSdrMonitor.Desktop.Shell;
using NetSdrMonitor.Desktop.Theming;
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
           _simulation = new SimulationController(settings, repositoryFactory, _loggerFactory, logSink);

           var main = new MainWindow(_simulation, store);
           MainWindow = main;
           ThemeApplier.Initialize(main, settings.Theme);
           main.Tray.Attach(_simulation, store);

           main.Show();
           if (settings.HideMainWindowOnStartup)
               main.Hide();

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
