namespace NetSdrMonitor.Desktop.Controls;

/// <summary>
/// Одна точка спектра для <see cref="SpectrumChart"/>: частота детекції та її SNR.
/// </summary>
public readonly record struct SpectrumPoint
{
   /// <summary>
   /// Частота детекції в МГц (позиція стовпця по осі X).
   /// </summary>
   public required double FrequencyMhz { get; init; }

   /// <summary>
   /// Відношення сигнал/шум у дБ (висота стовпця по осі Y).
   /// </summary>
   public required double SnrDb { get; init; }
}
