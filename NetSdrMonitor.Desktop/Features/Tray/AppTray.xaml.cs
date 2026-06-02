using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using NetSdrMonitor.Core.Abstractions.Communication;
using Application = System.Windows.Application;

namespace NetSdrMonitor.Desktop.Features.Tray;

/// <summary>
/// Іконка та меню в системному треї. Динамічні рядки статусу ліній додаються/оновлюються через SetStatus,
/// фіксовані пункти — дії оболонки (показати вікно, налаштування, вихід).
/// </summary>
public sealed partial class AppTray : UserControl
{
   public AppTray()
   {
      InitializeComponent();
      DataContext = this; // меню біндиться на цей екземпляр (через проксі)
   }

   public ObservableCollection<TrayStatusLine> StatusLines { get; } = [];

   /// <summary>
   /// Додає рядок статусу лінії або оновлює наявний (за номером лінії/порту).
   /// </summary>
   public void SetStatus(int lineId, ConnectionStatus status)
   {
      TrayStatusLine? line = StatusLines.FirstOrDefault(l => l.LineId == lineId);
      if (line is null)
         StatusLines.Add(new TrayStatusLine(lineId, status));
      else
         line.Status = status; // INPC оновить текст у меню сам
   }

   [RelayCommand]
   private void ShowMain()
   {
      if (Application.Current.MainWindow is not { } main)
         return;

      main.Show();
      if (main.WindowState == WindowState.Minimized)
         main.WindowState = WindowState.Normal;
      main.Activate();
   }

   [RelayCommand]
   private void OpenSettings()
   {
      // TODO(decide): вікно налаштувань — поки заглушка
   }

   [RelayCommand]
   private void Exit() => Application.Current.Shutdown();
}
