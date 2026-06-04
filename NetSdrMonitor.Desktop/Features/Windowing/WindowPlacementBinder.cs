using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using NetSdrMonitor.Desktop.Settings;

namespace NetSdrMonitor.Desktop.Features.Windowing;

/// <summary>
/// Прив'язує розмір і позицію вікна до сховища: відновлює збережене розташування при показі
/// та зберігає його «наживо» при кожній зміні, тож стан не губиться навіть при різкому
/// завершенні процесу (вихід через трей, зупинка зневадження).
/// </summary>
internal sealed class WindowPlacementBinder
{
   // дебаунс: серію подій ресайзу/переміщення згортаємо в один запис, коли користувач завершив тягнути
   private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);

   private readonly Window _window;
   private readonly Func<WindowPlacement?> _load;
   private readonly Action<WindowPlacement> _save;
   private readonly DispatcherTimer _saveTimer;

   private bool _suppressSave;

   public WindowPlacementBinder(Window window, Func<WindowPlacement?> load, Action<WindowPlacement> save)
   {
      _window = window;
      _load   = load;
      _save   = save;

      _saveTimer = new DispatcherTimer
      {
               Interval = SaveDelay
      };
      _saveTimer.Tick += OnSaveTick;

      // підписка вже після InitializeComponent гарантує, що відновлення спрацює після стартової
      // частки від WindowFraction і перекриє її збереженим розташуванням
      _window.SourceInitialized += OnSourceInitialized;
      _window.LocationChanged   += (_, _) => ScheduleSave();
      _window.SizeChanged       += (_, _) => ScheduleSave();
      _window.StateChanged      += (_, _) => ScheduleSave();
      _window.Closing           += OnClosing;
   }

   // відновлюємо збережене розташування; SetWindowPlacement сам притягне вікно на видимий монітор
   private void OnSourceInitialized(object? sender, EventArgs e)
   {
      if (_load() is not {} placement)
         return;

      // зміни від самого відновлення не повинні породжувати зайвий запис
      _suppressSave                 = true;
      _window.WindowStartupLocation = WindowStartupLocation.Manual; // позицію задає placement, не центрування
      WindowPlacementInterop.Restore(_window, placement);
      _suppressSave = false;
   }

   private void ScheduleSave()
   {
      if (_suppressSave)
         return;

      _saveTimer.Stop();
      _saveTimer.Start();
   }

   private void OnSaveTick(object? sender, EventArgs e)
   {
      _saveTimer.Stop();
      Flush();
   }

   // на закритті фіксуємо одразу, без очікування таймера
   private void OnClosing(object? sender, CancelEventArgs e)
   {
      _saveTimer.Stop();
      Flush();
   }

   // знімаємо поточне розташування й віддаємо у сховище; до появи дескриптора знімок порожній
   private void Flush()
   {
      if (WindowPlacementInterop.Capture(_window) is {} placement)
         _save(placement);
   }
}
