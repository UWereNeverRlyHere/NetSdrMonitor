using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace NetSdrMonitor.Desktop.Controls;

/// <summary>
/// Легкий спектр-віджет у стилі аналізатора спектра: по осі X — частота детекцій,
/// висота стовпця — їх SNR; вертикальна позначка показує медіану частоти запису.
/// Малює себе сам (без зовнішніх бібліотек графіків), кольори бере з теми.
/// </summary>
public sealed class SpectrumChart : FrameworkElement
{
   // відступи площі графіка: ліворуч лишаємо місце під підпис SNR, знизу — під частоти
   private const double MarginLeft   = 34;
   private const double MarginRight  = 10;
   private const double MarginTop    = 10;
   private const double MarginBottom = 16;
   private const double LabelFontSize = 10.5;

   public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
      nameof(ItemsSource), typeof(IReadOnlyList<SpectrumPoint>), typeof(SpectrumChart),
      new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

   public static readonly DependencyProperty MedianFrequencyMhzProperty = DependencyProperty.Register(
      nameof(MedianFrequencyMhz), typeof(double), typeof(SpectrumChart),
      new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

   public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
      nameof(BarBrush), typeof(Brush), typeof(SpectrumChart),
      new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

   public static readonly DependencyProperty AxisBrushProperty = DependencyProperty.Register(
      nameof(AxisBrush), typeof(Brush), typeof(SpectrumChart),
      new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

   public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(
      nameof(LabelBrush), typeof(Brush), typeof(SpectrumChart),
      new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

   public static readonly DependencyProperty MedianBrushProperty = DependencyProperty.Register(
      nameof(MedianBrush), typeof(Brush), typeof(SpectrumChart),
      new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

   /// <summary>
   /// Точки спектра (частота + SNR) — джерело стовпців.
   /// </summary>
   public IReadOnlyList<SpectrumPoint>? ItemsSource
   {
      get => (IReadOnlyList<SpectrumPoint>?)GetValue(ItemsSourceProperty);
      set => SetValue(ItemsSourceProperty, value);
   }

   /// <summary>
   /// Медіана частоти запису в МГц для вертикальної позначки; NaN — не малювати.
   /// </summary>
   public double MedianFrequencyMhz
   {
      get => (double)GetValue(MedianFrequencyMhzProperty);
      set => SetValue(MedianFrequencyMhzProperty, value);
   }

   /// <summary>
   /// Кисть стовпців.
   /// </summary>
   public Brush BarBrush
   {
      get => (Brush)GetValue(BarBrushProperty);
      set => SetValue(BarBrushProperty, value);
   }

   /// <summary>
   /// Кисть осей і сітки.
   /// </summary>
   public Brush AxisBrush
   {
      get => (Brush)GetValue(AxisBrushProperty);
      set => SetValue(AxisBrushProperty, value);
   }

   /// <summary>
   /// Кисть текстових підписів.
   /// </summary>
   public Brush LabelBrush
   {
      get => (Brush)GetValue(LabelBrushProperty);
      set => SetValue(LabelBrushProperty, value);
   }

   /// <summary>
   /// Кисть позначки медіани.
   /// </summary>
   public Brush MedianBrush
   {
      get => (Brush)GetValue(MedianBrushProperty);
      set => SetValue(MedianBrushProperty, value);
   }

   /// <summary>
   /// Перемальовуємо при зміні розміру вікна, бо вся геометрія масштабується під площу.
   /// </summary>
   protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
   {
      base.OnRenderSizeChanged(sizeInfo);
      InvalidateVisual();
   }

   /// <summary>
   /// Малює осі, стовпці частота -> SNR, позначку медіани та підписи діапазонів.
   /// </summary>
   protected override void OnRender(DrawingContext dc)
   {
      double width  = ActualWidth;
      double height = ActualHeight;
      if (width <= 0 || height <= 0)
         return;

      // контрол прозорий і малює лише осі/стовпці/підписи — суцільне тло дає контейнер-обгортка
      double plotWidth  = width  - MarginLeft - MarginRight;
      double plotHeight = height - MarginTop  - MarginBottom;
      if (plotWidth <= 4 || plotHeight <= 4)
         return;

      double baselineY = MarginTop + plotHeight; // нульова лінія SNR (низ графіка)
      double leftX     = MarginLeft;
      double rightX    = MarginLeft + plotWidth;

      var axisPen = new Pen(AxisBrush, 1);
      axisPen.Freeze();
      dc.DrawLine(axisPen, new Point(leftX, MarginTop), new Point(leftX, baselineY)); // вісь Y
      dc.DrawLine(axisPen, new Point(leftX, baselineY), new Point(rightX, baselineY)); // базова лінія X

      IReadOnlyList<SpectrumPoint>? points = ItemsSource;
      if (points is null || points.Count == 0)
      {
         DrawText(dc, "немає даних", leftX + plotWidth / 2, MarginTop + plotHeight / 2, centerX: true);
         return;
      }

      // діапазон частот: трохи розширюємо, щоб крайні стовпці не липли до осей
      double freqMin = double.MaxValue;
      double freqMax = double.MinValue;
      double snrMax  = 0;
      foreach (SpectrumPoint p in points)
      {
         if (p.FrequencyMhz < freqMin) freqMin = p.FrequencyMhz;
         if (p.FrequencyMhz > freqMax) freqMax = p.FrequencyMhz;
         if (p.SnrDb > snrMax) snrMax = p.SnrDb;
      }

      double freqSpan = freqMax - freqMin;
      if (freqSpan < 1e-9)
      {
         // усі детекції на одній частоті - даємо штучний діапазон, щоб стовпець став по центру
         freqMin -= 0.001;
         freqMax += 0.001;
      }
      else
      {
         double pad = freqSpan * 0.08;
         freqMin -= pad;
         freqMax += pad;
         if (freqMin < 0)
            freqMin = 0; // частоти невід'ємні - не виводимо від'ємну вісь через падінг
      }
      freqSpan = freqMax - freqMin;

      // верх шкали SNR округлюємо вгору до 5 дБ
      double snrTop = Math.Max(5, Math.Ceiling(snrMax / 5.0) * 5.0);

      double MapX(double freq) => leftX + (freq - freqMin) / freqSpan * plotWidth;
      double MapY(double snr)  => baselineY - snr / snrTop * plotHeight;

      // напівтонова горизонтальна сітка на половині шкали орієнтир по висоті
      var gridPen = new Pen(AxisBrush, 0.5) { DashStyle = DashStyles.Dash };
      gridPen.Freeze();
      double midY = MapY(snrTop / 2.0);
      dc.DrawLine(gridPen, new Point(leftX, midY), new Point(rightX, midY));

      // ширина стовпця підлаштовується під кількість точок, але лишається в розумних межах
      double barWidth = Math.Clamp(plotWidth / (points.Count * 1.8), 2.0, 16.0);
      foreach (SpectrumPoint p in points)
      {
         double x = MapX(p.FrequencyMhz);
         double y = MapY(p.SnrDb);
         var bar = new Rect(x - barWidth / 2, y, barWidth, baselineY - y);
         dc.DrawRectangle(BarBrush, null, bar);
      }

      // вертикальна позначка медіани (якщо задана й потрапляє в видимий діапазон)
      double median = MedianFrequencyMhz;
      if (!double.IsNaN(median) && median >= freqMin && median <= freqMax)
      {
         double mx = MapX(median);
         var medianPen = new Pen(MedianBrush, 1.4) { DashStyle = DashStyles.Dash };
         medianPen.Freeze();
         dc.DrawLine(medianPen, new Point(mx, MarginTop), new Point(mx, baselineY));
      }

      // підписи: верх шкали SNR, межі частотного діапазону
      DrawText(dc, snrTop.ToString("F0", CultureInfo.CurrentCulture), leftX - 4, MarginTop, rightAlign: true);
      DrawText(dc, "0", leftX - 4, baselineY - LabelFontSize, rightAlign: true);
      DrawText(dc, freqMin.ToString("F3", CultureInfo.CurrentCulture), leftX, baselineY + 2);
      DrawText(dc, freqMax.ToString("F3", CultureInfo.CurrentCulture), rightX, baselineY + 2, rightAlign: true);
   }

   /// <summary>
   /// Малює короткий підпис; вирівнювання - за лівим краєм, правим краєм або по центру точки.
   /// </summary>
   private void DrawText(DrawingContext dc, string text, double x, double y, bool rightAlign = false, bool centerX = false)
   {
      double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
      var formatted = new FormattedText(
         text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
         new Typeface("Segoe UI"), LabelFontSize, LabelBrush, pixelsPerDip);

      double drawX = x;
      if (rightAlign) drawX = x - formatted.Width;
      else if (centerX) drawX = x - formatted.Width / 2;

      dc.DrawText(formatted, new Point(drawX, y));
   }
}
