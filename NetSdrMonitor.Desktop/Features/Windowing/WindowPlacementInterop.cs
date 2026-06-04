using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using NetSdrMonitor.Desktop.Settings;

namespace NetSdrMonitor.Desktop.Features.Windowing;

/// <summary>
/// Зчитує й відновлює розташування вікна через Win32 WINDOWPLACEMENT.
/// Відновлення саме притягує вікно на видимий монітор, якщо збережений уже недоступний.
/// </summary>
internal static class WindowPlacementInterop
{
   private const int ShowNormal = 1;
   private const int ShowMaximized = 3;

   /// <summary>
   /// Знімає поточне розташування вікна; повертає null, якщо вікно ще не має дескриптора.
   /// </summary>
   public static WindowPlacement? Capture(Window window)
   {
      IntPtr hwnd = new WindowInteropHelper(window).Handle;
      if (hwnd == IntPtr.Zero)
         return null;

      var raw = new NativePlacement
      {
               Length = Marshal.SizeOf<NativePlacement>()
      };
      if (!GetWindowPlacement(hwnd, ref raw))
         return null;

      NativeRect r = raw.NormalPosition;
      return new WindowPlacement
      {
               Left   = r.Left,
               Top    = r.Top,
               Right  = r.Right,
               Bottom = r.Bottom,
               // згорнутий стан не зберігаємо як такий — вікно має відновитися видимим
               IsMaximized = raw.ShowCmd == ShowMaximized,
      };
   }

   /// <summary>
   /// Застосовує збережене розташування до вікна (з дескриптором).
   /// </summary>
   public static void Restore(Window window, WindowPlacement placement)
   {
      IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();

      var raw = new NativePlacement
      {
               Length  = Marshal.SizeOf<NativePlacement>(),
               ShowCmd = placement.IsMaximized ? ShowMaximized : ShowNormal,
               NormalPosition = new NativeRect
               {
                        Left   = placement.Left,
                        Top    = placement.Top,
                        Right  = placement.Right,
                        Bottom = placement.Bottom,
               },
      };

      SetWindowPlacement(hwnd, ref raw);
   }

   [DllImport("user32.dll")]
   [return: MarshalAs(UnmanagedType.Bool)]
   private static extern bool GetWindowPlacement(IntPtr hwnd, ref NativePlacement placement);

   [DllImport("user32.dll")]
   [return: MarshalAs(UnmanagedType.Bool)]
   private static extern bool SetWindowPlacement(IntPtr hwnd, [In] ref NativePlacement placement);

   [StructLayout(LayoutKind.Sequential)]
   private struct NativePoint
   {
      public int X;
      public int Y;
   }

   [StructLayout(LayoutKind.Sequential)]
   private struct NativeRect
   {
      public int Left;
      public int Top;
      public int Right;
      public int Bottom;
   }

   [StructLayout(LayoutKind.Sequential)]
   private struct NativePlacement
   {
      public int Length;
      public int Flags;
      public int ShowCmd;
      public NativePoint MinPosition;
      public NativePoint MaxPosition;
      public NativeRect NormalPosition;
   }
}
