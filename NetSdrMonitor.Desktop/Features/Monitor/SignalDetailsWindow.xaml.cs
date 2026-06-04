using System.Globalization;
using NetSdrMonitor.Desktop.Features.Windowing;
using NetSdrMonitor.Desktop.Settings;
using Wpf.Ui.Controls;

namespace NetSdrMonitor.Desktop.Features.Monitor;

/// <summary>
/// Вікно деталізації запису: показує всі сигнали, що злились у нього (джерело медіани),
/// та підсумок із кількістю детекцій і медіаною частоти.
/// </summary>
public partial class SignalDetailsWindow : FluentWindow
{
   public SignalDetailsWindow(SignalRecordRow row, JsonSettingsStore store)
   {
      InitializeComponent();

      DetailsGrid.ItemsSource = row.Signals.Select(signal => new SignalDetailRow(signal)).ToList();
      SummaryText.Text = string.Create(CultureInfo.CurrentCulture,
         $"Сигналів у записі: {row.Count} • медіана частоти: {row.MedianFrequencyMhz:F3} МГц");

      // запам'ятовуємо розмір/позицію вікна деталізації між відкриттями
      _ = new WindowPlacementBinder(this,
         () => store.Load().SignalDetailsWindowPlacement,
         placement => store.Save(store.Load() with { SignalDetailsWindowPlacement = placement }));
   }
}
