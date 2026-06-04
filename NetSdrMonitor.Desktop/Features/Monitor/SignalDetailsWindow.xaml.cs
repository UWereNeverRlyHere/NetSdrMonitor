using System.Globalization;
using NetSdrMonitor.Desktop.Controls;
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

      var rows = row.Signals.Select(signal => new SignalDetailRow(signal)).ToList();
      DetailsGrid.ItemsSource = rows;
      SummaryText.Text = string.Create(CultureInfo.CurrentCulture,
         $"Сигналів у записі: {row.Count} • медіана частоти: {row.MedianFrequencyMhz:F3} МГц");

      Spectrum.ItemsSource = rows
         .Select(r => new SpectrumPoint { FrequencyMhz = r.FrequencyMhz, SnrDb = r.SnrDb })
         .ToList();
      Spectrum.MedianFrequencyMhz = row.MedianFrequencyMhz;

      // запам'ятовуємо розмір/позицію вікна деталізації між відкриттями
      _ = new WindowPlacementBinder(this,
         () => store.Load().SignalDetailsWindowPlacement,
         placement => store.Save(store.Load() with { SignalDetailsWindowPlacement = placement }));
   }
}
