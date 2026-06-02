using System.Windows.Controls;

namespace NetSdrMonitor.Desktop.Features.Console;

/// <summary>
/// Консоль логів. Автопрокрутка тримає видимим «хвіст», але не заважає: щойно користувач
/// прокрутив угору — автоскрол вимикається, поки він не повернеться донизу.
/// </summary>
public partial class ConsoleView : UserControl
{
   private bool _autoScroll = true;

   public ConsoleView()
   {
      InitializeComponent();
   }

   private void OnLogScroll(object sender, ScrollChangedEventArgs e)
   {
      if (e.OriginalSource is not ScrollViewer scrollViewer)
         return;

      // зміна висоти вмісту (нові рядки) vs ручна прокрутка — розрізняємо за ExtentHeightChange
      if (e.ExtentHeightChange == 0)
         _autoScroll = scrollViewer.VerticalOffset + scrollViewer.ViewportHeight >= scrollViewer.ExtentHeight - 1.0;
      else if (_autoScroll)
         scrollViewer.ScrollToEnd();
   }
}
