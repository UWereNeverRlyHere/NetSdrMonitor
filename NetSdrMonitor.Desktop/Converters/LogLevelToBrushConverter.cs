using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Appearance;

namespace NetSdrMonitor.Desktop.Converters;

/// <summary>
/// Перетворює рівень логу на колір тексту для консолі: попередження/помилки — теплі відтінки,
/// інформація — звичайний текст, діагностика — приглушений сірий. Відтінки підбираються під поточну
/// тему (світла/темна), щоб текст лишався читабельним на обох фонах.
/// </summary>
public sealed class LogLevelToBrushConverter : IValueConverter
{
   // приглушений сірий читабельний на обох фонах — спільний для світлої й темної
   private static readonly Brush Muted = Frozen(0x8A, 0x90, 0x9C);

   private static readonly Brush InfoLight    = Frozen(0x1F, 0x24, 0x30);
   private static readonly Brush WarningLight  = Frozen(0xB0, 0x6A, 0x00);
   private static readonly Brush ErrorLight    = Frozen(0xC0, 0x2B, 0x2B);

   private static readonly Brush InfoDark     = Frozen(0xE6, 0xE6, 0xE6);
   private static readonly Brush WarningDark   = Frozen(0xE8, 0xA3, 0x3D);
   private static readonly Brush ErrorDark     = Frozen(0xF0, 0x80, 0x7F);

   public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
   {
      bool dark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
      return value switch
      {
         LogLevel.Trace or LogLevel.Debug    => Muted,
         LogLevel.Warning                    => dark ? WarningDark : WarningLight,
         LogLevel.Error or LogLevel.Critical => dark ? ErrorDark : ErrorLight,
         _                                   => dark ? InfoDark : InfoLight,
      };
   }

   public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
      throw new NotSupportedException();

   // заморожена кисть — потокобезпечна й дешева для багаторазового використання у списку
   private static Brush Frozen(byte r, byte g, byte b)
   {
      var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
      brush.Freeze();
      return brush;
   }
}
