using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NetSdrMonitor.Desktop.Controls;

/// <summary>
/// Просте числове поле зі стрілками вгору/вниз. Крок, межі та значення задаються властивостями.
/// З <see cref="IntegerOnly"/>=true з клавіатури приймаються лише цифри; значення оновлюється
/// одразу під час введення (зручно для живого фільтра).
/// </summary>
public partial class NumericSpinner : UserControl
{
   public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
      nameof(Value), typeof(double), typeof(NumericSpinner),
      new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, null, CoerceValue));

   public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
      nameof(Step), typeof(double), typeof(NumericSpinner), new PropertyMetadata(1.0));

   public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
      nameof(Minimum), typeof(double), typeof(NumericSpinner), new PropertyMetadata(double.MinValue));

   public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
      nameof(Maximum), typeof(double), typeof(NumericSpinner), new PropertyMetadata(double.MaxValue));

   public static readonly DependencyProperty IntegerOnlyProperty = DependencyProperty.Register(
      nameof(IntegerOnly), typeof(bool), typeof(NumericSpinner), new PropertyMetadata(false));

   // дозволені проміжні стани введення: лише цифри або число з одним роздільником/мінусом
   private static readonly Regex IntegerPattern = new(@"^\d*$", RegexOptions.Compiled);
   private static readonly Regex DecimalPattern = new(@"^-?\d*([.,]\d*)?$", RegexOptions.Compiled);

   public NumericSpinner()
   {
      InitializeComponent();
   }

   public double Value   { get => (double)GetValue(ValueProperty);   set => SetValue(ValueProperty, value); }
   public double Step    { get => (double)GetValue(StepProperty);    set => SetValue(StepProperty, value); }
   public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
   public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
   public bool   IntegerOnly { get => (bool)GetValue(IntegerOnlyProperty); set => SetValue(IntegerOnlyProperty, value); }

   private static object CoerceValue(DependencyObject d, object baseValue)
   {
      var spinner = (NumericSpinner)d;
      double clamped = Math.Clamp((double)baseValue, spinner.Minimum, spinner.Maximum);
      return Math.Round(clamped, 4); // прибираємо артефакти double при дробових кроках (напр. 0.05)
   }

   private void OnUp(object sender, RoutedEventArgs e) => Value += Step;

   private void OnDown(object sender, RoutedEventArgs e) => Value -= Step;

   private void OnPreviewTextInput(object sender, TextCompositionEventArgs e) => e.Handled = !WouldStayValid(e.Text);

   private void OnPaste(object sender, DataObjectPastingEventArgs e)
   {
      if (e.DataObject.GetDataPresent(DataFormats.UnicodeText)
          && e.DataObject.GetData(DataFormats.UnicodeText) is string pasted
          && WouldStayValid(pasted))
         return;

      e.CancelCommand(); // нечислова вставка — відхиляємо
   }
   private void OnInputKeyDown(object sender, KeyEventArgs e)
   {
      if (e.Key      == Key.Up)        { Value += Step; e.Handled = true; }
      else if (e.Key == Key.Down) { Value      -= Step; e.Handled = true; }
   }
   // підставляє введений фрагмент у поточний текст і перевіряє, чи лишається це коректним (частковим) числом
   private bool WouldStayValid(string insertion)
   {
      string candidate = Input.Text
         .Remove(Input.SelectionStart, Input.SelectionLength)
         .Insert(Input.SelectionStart, insertion);

      return (IntegerOnly ? IntegerPattern : DecimalPattern).IsMatch(candidate);
   }
}
