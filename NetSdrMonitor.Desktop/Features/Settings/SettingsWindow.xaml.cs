using System.Windows;
using System.Windows.Interop;
using NetSdrMonitor.Desktop.Behaviors;
using NetSdrMonitor.Desktop.Settings;
using NetSdrMonitor.Desktop.Theming;

namespace NetSdrMonitor.Desktop.Features.Settings;

public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow
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

      // запам'ятовуємо розмір/позицію вікна налаштувань між відкриттями
      _ = new WindowPlacementBinder(this,
         () => _store.Load().SettingsWindowPlacement,
         placement => _store.Save(_store.Load() with { SettingsWindowPlacement = placement }));
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

      // Корінь проблеми «з трея»: WPF після закриття модального вікна повертає активність ПОПЕРЕДНЬО
      // активному вікну. Коли налаштування відкрито з трея, попередньо активним є службове вікно меню
      // трея, а не головне вікно — тож воно не відновлюється й візуально «провалюється»/ховається.
      // Лік: робимо власника активним ДО показу діалога, щоб саме йому повернулась активність на закритті.
      if (owner is { IsVisible: true })
      {
         if (owner.WindowState == WindowState.Minimized)
            owner.WindowState = WindowState.Normal;

         owner.Activate();
      }

      try
      {
         if (dialog.ShowDialog() == true && dialog.Saved is { } saved)
            onSaved(saved);
      }
      finally
      {
         _open = null;

         // підстраховка: якщо власника все одно згорнуло, повертаємо його (схований у трей не чіпаємо)
         if (owner is { IsVisible: true, WindowState: WindowState.Minimized })
         {
            owner.WindowState = WindowState.Normal;
            owner.Activate();
         }
      }
   }

   private void OnSave(object sender, RoutedEventArgs e)
   {
      // знімок налаштувань на момент відкриття вже застарів: розкладку вікон/колонок ведуть
      // інші вікна «наживо», тож накладаємо редаговані поля на свіжий стан, а не на старий знімок
      AppSettings latest = _store.Load();
      AppSettings updated = _viewModel.ToSettings() with
      {
         MainWindowPlacement          = latest.MainWindowPlacement,
         SettingsWindowPlacement      = latest.SettingsWindowPlacement,
         SignalDetailsWindowPlacement = latest.SignalDetailsWindowPlacement,
         ConsoleHeight                = latest.ConsoleHeight,
         Columns                      = latest.Columns,
      };

      _store.Save(updated);
      ThemeApplier.Apply(updated.Theme); // застосовуємо тему одразу, без перезапуску
      Saved        = updated;
      DialogResult = true;
   }
}
