using System.Windows;
using NetSdrMonitor.Desktop.Settings;
using Wpf.Ui.Appearance;

namespace NetSdrMonitor.Desktop.Features.Settings;

/// <summary>
/// Застосовує обрану тему оформлення до всього застосунку й, у системному режимі,
/// тримає її синхронізованою з темою Windows у реальному часі.
/// </summary>
public static class ThemeApplier
{
   private static AppTheme _mode = AppTheme.System;
   private static bool _initialized;

   /// <summary>
   /// Налаштовує відстеження системної теми (одноразово) і застосовує початковий режим.
   /// Викликається на старті з головним вікном як «довгоживучим» хостом для спостерігача.
   /// </summary>
   public static void Initialize(Window host, AppTheme theme)
   {
      if (!_initialized)
      {
         _initialized = true;
         SystemThemeWatcher.Watch(host); // реагуємо на перемикання теми Windows на льоту

         // у фіксованому режимі (Світла/Темна) не даємо системному перемикачу збити вибір користувача
         ApplicationThemeManager.Changed += (applicationTheme, _) =>
         {
            if (_mode == AppTheme.System)
               return;

            ApplicationTheme desired = _mode == AppTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
            if (applicationTheme != desired)
               ApplicationThemeManager.Apply(desired);
         };
      }

      Apply(theme);
   }

   /// <summary>
   /// Застосовує тему негайно: фіксовану світлу/темну або поточну тему ОС (системний режим).
   /// </summary>
   public static void Apply(AppTheme theme)
   {
      _mode = theme;
      switch (theme)
      {
         case AppTheme.Light:
            ApplicationThemeManager.Apply(ApplicationTheme.Light);
            break;
         case AppTheme.Dark:
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
            break;
         default: // System — беремо те, що зараз стоїть у Windows
            ApplicationThemeManager.ApplySystemTheme();
            break;
      }
   }
}
