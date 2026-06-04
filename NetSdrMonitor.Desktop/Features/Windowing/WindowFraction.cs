using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace NetSdrMonitor.Desktop.Features.Windowing;

/// <summary>
/// Приєднані властивості розміру вікна: стартова частка робочої області монітора,
/// на якому відкривається вікно, та обмеження максимуму цією ж областю.
/// Працює коректно на будь-якому моніторі з будь-яким системним масштабуванням.
/// </summary>
public static class WindowFraction
{
   private const int MonitorDefaultToNearest = 0x00000002;

   public static readonly DependencyProperty WidthFractionProperty =
            DependencyProperty.RegisterAttached("WidthFraction", typeof(double), typeof(WindowFraction),
                                                new PropertyMetadata(double.NaN, OnChanged));

   public static readonly DependencyProperty HeightFractionProperty =
            DependencyProperty.RegisterAttached("HeightFraction", typeof(double), typeof(WindowFraction),
                                                new PropertyMetadata(double.NaN, OnChanged));

   public static readonly DependencyProperty MaxToWorkAreaProperty =
            DependencyProperty.RegisterAttached("MaxToWorkArea", typeof(bool), typeof(WindowFraction),
                                                new PropertyMetadata(false, OnChanged));

   public static void SetWidthFraction(DependencyObject element, double value) => element.SetValue(WidthFractionProperty, value);

   public static double GetWidthFraction(DependencyObject element) => (double)element.GetValue(WidthFractionProperty);

   public static void SetHeightFraction(DependencyObject element, double value) => element.SetValue(HeightFractionProperty, value);

   public static double GetHeightFraction(DependencyObject element) => (double)element.GetValue(HeightFractionProperty);

   public static void SetMaxToWorkArea(DependencyObject element, bool value) => element.SetValue(MaxToWorkAreaProperty, value);

   public static bool GetMaxToWorkArea(DependencyObject element) => (bool)element.GetValue(MaxToWorkAreaProperty);

   private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
   {
      if (d is not Window window)
         return;

      // підписка ідемпотентна навіть коли задано кілька властивостей одразу
      window.SourceInitialized -= ApplyOnSourceInitialized;
      window.SourceInitialized += ApplyOnSourceInitialized;
   }

   private static void ApplyOnSourceInitialized(object? sender, EventArgs e)
   {
      var window = (Window)sender!;
      Rect work = GetWorkAreaDip(window);

      // стелю беремо як мінімум зі вказаної в XAML і доступної області — щоб вікно не вилазило за екран
      if (GetMaxToWorkArea(window))
      {
         window.MaxWidth  = Math.Min(window.MaxWidth, work.Width);
         window.MaxHeight = Math.Min(window.MaxHeight, work.Height);
      }

      double widthFraction = GetWidthFraction(window);
      double heightFraction = GetHeightFraction(window);

      // зажими Min/Max лишаються головними — частка лише задає бажане значення
      if (!double.IsNaN(widthFraction))
         window.Width = Clamp(work.Width * widthFraction, window.MinWidth, window.MaxWidth);

      if (!double.IsNaN(heightFraction))
         window.Height = Clamp(work.Height * heightFraction, window.MinHeight, window.MaxHeight);
   }

   /// <summary>
   /// Повертає робочу область монітора під вікном (для діалогу — під його власником) у DIP.
   /// </summary>
   private static Rect GetWorkAreaDip(Window window)
   {
      // для діалогу з CenterOwner орієнтуємось на монітор власника, інакше — на монітор самого вікна
      Window anchor = window.Owner ?? window;
      IntPtr hwnd = new WindowInteropHelper(anchor).EnsureHandle();

      IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
      var info = new NativeMonitorInfo
      {
               Size = Marshal.SizeOf<NativeMonitorInfo>()
      };
      GetMonitorInfo(monitor, ref info);

      // rcWork приходить в апаратних пікселях; ділимо на масштаб монітора-якоря (для діалогу — власника,
      // який уже на своєму моніторі), бо WPF міряє в DIP
      DpiScale dpi = VisualTreeHelper.GetDpi(anchor);
      NativeRect w = info.WorkArea;
      return new Rect(w.Left              / dpi.DpiScaleX,
                      w.Top               / dpi.DpiScaleY,
                      (w.Right  - w.Left) / dpi.DpiScaleX,
                      (w.Bottom - w.Top)  / dpi.DpiScaleY);
   }

   private static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;

   [DllImport("user32.dll")]
   private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

   [DllImport("user32.dll", CharSet = CharSet.Unicode)]
   [return: MarshalAs(UnmanagedType.Bool)]
   private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo info);

   [StructLayout(LayoutKind.Sequential)]
   private struct NativeRect
   {
      public int Left;
      public int Top;
      public int Right;
      public int Bottom;
   }

   [StructLayout(LayoutKind.Sequential)]
   private struct NativeMonitorInfo
   {
      public int Size;
      public NativeRect Monitor;
      public NativeRect WorkArea;
      public uint Flags;
   }
}
