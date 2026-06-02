using System.Windows;
using System.Windows.Interop;
using NetSdrMonitor.Desktop.Settings;
using NetSdrMonitor.Desktop.Theming;

namespace NetSdrMonitor.Desktop.Features.Settings;

public partial class SettingsWindow : Window
{
   private static SettingsWindow? _open; // одне вікно налаштувань на застосунок

   private readonly SettingsViewModel _viewModel;
   private readonly JsonSettingsStore _store;

   public SettingsWindow(AppSettings current, JsonSettingsStore store)
   {
      InitializeComponent();
      _store      = store;
      _viewModel  = new SettingsViewModel(current);
      DataContext = _viewModel;
   }

   public AppSettings? Saved { get; private set; }

   /// <summary>
   /// Відкриває вікно налаштувань, а якщо воно вже відкрите — повертає на нього фокус (без дубля).
   /// </summary>
   public static void OpenOrActivate(Window? owner, AppSettings current, JsonSettingsStore store, Action<AppSettings> onSaved)
   {
      if (_open is not null)
      {
         if (_open.WindowState == WindowState.Minimized)
            _open.WindowState = WindowState.Normal;

         _open.Activate();
         return;
      }

      var dialog = new SettingsWindow(current, store);

      // Owner призначаємо лише якщо головне вікно вже показували: непоказане вікно ще без HWND,
      // і WPF кине InvalidOperationException. Без власника — центруємо діалог по екрану.
      if (owner is not null && new WindowInteropHelper(owner).Handle != IntPtr.Zero)
         dialog.Owner = owner;
      else
         dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

      _open = dialog;
      try
      {
         if (dialog.ShowDialog() == true && dialog.Saved is { } saved)
            onSaved(saved);
      }
      finally
      {
         _open = null;
      }
   }

   private void OnSave(object sender, RoutedEventArgs e)
   {
      AppSettings updated = _viewModel.ToSettings();
      _store.Save(updated);
      ThemeApplier.Apply(updated.Theme); // застосовуємо тему одразу, без перезапуску
      Saved        = updated;
      DialogResult = true;
   }
}
