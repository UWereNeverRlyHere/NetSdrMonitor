using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Extensions.Logging;

namespace NetSdrMonitor.Desktop.Converters;

/// <summary>
/// Перетворює рівень логу на колір тексту для консолі: попередження/помилки — червоні відтінки,
/// інформація — звичайний текст, діагностика — приглушений сірий.
/// </summary>
public sealed class LogLevelToBrushConverter : IValueConverter
{
   private static readonly Brush Debug   = new SolidColorBrush(Color.FromRgb(0x8A, 0x90, 0x9C));
   private static readonly Brush Info    = new SolidColorBrush(Color.FromRgb(0x1F, 0x24, 0x30));
   private static readonly Brush Warning = new SolidColorBrush(Color.FromRgb(0xB0, 0x6A, 0x00));
   private static readonly Brush Error   = new SolidColorBrush(Color.FromRgb(0xC0, 0x2B, 0x2B));

   public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
   {
      LogLevel.Trace or LogLevel.Debug          => Debug,
      LogLevel.Warning                          => Warning,
      LogLevel.Error or LogLevel.Critical       => Error,
      _                                         => Info,
   };

   public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
      throw new NotSupportedException();
}
