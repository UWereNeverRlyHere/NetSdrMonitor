using NetSdrMonitor.Domain.Signals;

namespace NetSdrMonitor.Desktop.Features.Monitor;

/// <summary>
/// Презентаційний рядок одного сигналу для вікна деталізації запису: переводить герци
/// в МГц/кГц і показує час та SNR кожної детекції, що увійшла в медіану.
/// </summary>
public sealed class SignalDetailRow
{
   /// <summary>
   /// Створює рядок поверх однієї детекції.
   /// </summary>
   public SignalDetailRow(Signal signal)
   {
      Time         = signal.Timestamp.LocalDateTime;
      FrequencyMhz = signal.FrequencyHz / 1_000_000.0;
      BandwidthKhz = signal.BandwidthHz / 1_000.0;
      SnrDb        = signal.SnrDb;
   }

   /// <summary>
   /// Час детекції (локальний).
   /// </summary>
   public DateTime Time { get; }

   /// <summary>
   /// Частота детекції в МГц.
   /// </summary>
   public double FrequencyMhz { get; }

   /// <summary>
   /// Ширина смуги в кГц.
   /// </summary>
   public double BandwidthKhz { get; }

   /// <summary>
   /// Відношення сигнал/шум у дБ.
   /// </summary>
   public double SnrDb { get; }
}
