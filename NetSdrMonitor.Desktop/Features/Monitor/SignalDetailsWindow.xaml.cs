using System.Globalization;
using System.Linq;
using System.Windows;

namespace NetSdrMonitor.Desktop.Features.Monitor;

/// <summary>
/// Вікно деталізації запису: показує всі сигнали, що злились у нього (джерело медіани),
/// та підсумок із кількістю детекцій і медіаною частоти.
/// </summary>
public partial class SignalDetailsWindow : Window
{
   public SignalDetailsWindow(SignalRecordRow row)
   {
      InitializeComponent();

      DetailsGrid.ItemsSource = row.Signals.Select(signal => new SignalDetailRow(signal)).ToList();
      SummaryText.Text = string.Create(CultureInfo.CurrentCulture,
         $"Сигналів у записі: {row.Count} • медіана частоти: {row.MedianFrequencyMhz:F3} МГц");
   }
}
