using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NetSdrMonitor.Desktop.Controls;

/// <summary>
/// Поле вибору часу доби «год:хв» із випадним спінером годин і хвилин.
/// Назовні віддає рядок <see cref="Text"/> у форматі HH:mm; порожній рядок — час не задано.
/// </summary>
public partial class TimePicker : UserControl
{
   // допустимі формати ручного вводу часу доби
   private static readonly string[] TimeFormats = { "H:mm", "HH:mm" };

   public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
      nameof(Text), typeof(string), typeof(TimePicker),
      new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

   public static readonly DependencyProperty HourProperty = DependencyProperty.Register(
      nameof(Hour), typeof(int), typeof(TimePicker), new PropertyMetadata(0, OnPartChanged));

   public static readonly DependencyProperty MinuteProperty = DependencyProperty.Register(
      nameof(Minute), typeof(int), typeof(TimePicker), new PropertyMetadata(0, OnPartChanged));

   // захист від рекурсії: синхронізація Text <-> Hour/Minute не має заводити себе ж по колу
   private bool _syncing;

   public TimePicker()
   {
      InitializeComponent();
   }

   /// <summary>
   /// Час доби у форматі HH:mm; порожньо — час не задано (фільтр вимкнено).
   /// </summary>
   public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

   /// <summary>
   /// Години (0..23) для спінера.
   /// </summary>
   public int Hour { get => (int)GetValue(HourProperty); set => SetValue(HourProperty, value); }

   /// <summary>
   /// Хвилини (0..59) для спінера.
   /// </summary>
   public int Minute { get => (int)GetValue(MinuteProperty); set => SetValue(MinuteProperty, value); }

   // ручний ввід тексту -> розкладаємо на години/хвилини (некоректне/порожнє лишаємо без змін спінерів)
   private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
   {
      var picker = (TimePicker)d;
      if (picker._syncing)
         return;

      if (!DateTime.TryParseExact(((string)e.NewValue)?.Trim(), TimeFormats,
                                  CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime parsed))
         return;

      picker._syncing = true;
      picker.Hour   = parsed.Hour;
      picker.Minute = parsed.Minute;
      picker._syncing = false;
   }

   // зміна спінера -> складаємо рядок HH:mm
   private static void OnPartChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
   {
      var picker = (TimePicker)d;
      if (picker._syncing)
         return;

      picker._syncing = true;
      picker.Text = $"{picker.Hour:00}:{picker.Minute:00}";
      picker._syncing = false;
   }

   private void OnClear(object sender, RoutedEventArgs e)
   {
      _syncing = true;
      Hour   = 0;
      Minute = 0;
      _syncing = false;

      Text = string.Empty;        // порожньо — фільтр за часом вимкнено
      DropToggle.IsChecked = false; // ховаємо випадну панель
   }

   // у полі дозволені лише цифри та двокрапка
   private static bool IsAllowed(string text) => text.All(c => char.IsAsciiDigit(c) || c == ':');

   private void OnDisplayPreviewTextInput(object sender, TextCompositionEventArgs e) =>
      e.Handled = !IsAllowed(e.Text);

   private void OnDisplayPaste(object sender, DataObjectPastingEventArgs e)
   {
      if (e.DataObject.GetDataPresent(DataFormats.UnicodeText)
          && e.DataObject.GetData(DataFormats.UnicodeText) is string pasted
          && IsAllowed(pasted))
         return;

      e.CancelCommand(); // вставка з чимось окрім цифр/двокрапки — відхиляємо
   }
}
