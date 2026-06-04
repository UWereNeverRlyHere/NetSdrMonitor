using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NetSdrMonitor.Desktop.Behaviors;

/// <summary>
/// Приєднана поведінка для TextBox: при отриманні фокуса весь текст виділяється,
/// тож перший введений символ замінює попереднє значення (а не дописується).
/// </summary>
public static class SelectAllOnFocus
{
   public static readonly DependencyProperty EnabledProperty = DependencyProperty
           .RegisterAttached("Enabled", typeof(bool), typeof(SelectAllOnFocus), new PropertyMetadata(false, OnEnabledChanged));
   public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

   public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

   private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
   {
      if (d is not TextBox box)
         return;

      box.GotKeyboardFocus           -= OnGotFocus;
      box.PreviewMouseLeftButtonDown -= OnPreviewMouseDown;

      if (e.NewValue is true)
      {
         box.GotKeyboardFocus           += OnGotFocus;
         box.PreviewMouseLeftButtonDown += OnPreviewMouseDown;
      }
   }

   // фокус із клавіатури (Tab) або після кліку — виділяємо весь текст
   private static void OnGotFocus(object sender, RoutedEventArgs e) => ((TextBox)sender).SelectAll();

   // перший клік по незфокусованому полю інакше зняв би виділення — перехоплюємо:
   // ставимо фокус самі (далі спрацює GotKeyboardFocus -> SelectAll), а клік «гасимо»
   private static void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
   {
      if (sender is TextBox { IsKeyboardFocusWithin: false } box)
      {
         box.Focus();
         e.Handled = true;
      }
   }
}
