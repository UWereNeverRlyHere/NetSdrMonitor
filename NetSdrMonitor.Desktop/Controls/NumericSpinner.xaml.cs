using System.Windows;
using System.Windows.Controls;

namespace NetSdrMonitor.Desktop.Controls;

/// <summary>
/// Просте числове поле зі стрілками вгору/вниз. Крок, межі та значення задаються властивостями.
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

   public NumericSpinner()
   {
      InitializeComponent();
   }

   public double Value   { get => (double)GetValue(ValueProperty);   set => SetValue(ValueProperty, value); }
   public double Step    { get => (double)GetValue(StepProperty);    set => SetValue(StepProperty, value); }
   public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
   public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

   private static object CoerceValue(DependencyObject d, object baseValue)
   {
      var spinner = (NumericSpinner)d;
      double clamped = Math.Clamp((double)baseValue, spinner.Minimum, spinner.Maximum);
      return Math.Round(clamped, 4); // прибираємо артефакти double при дробових кроках (напр. 0.05)
   }

   private void OnUp(object sender, RoutedEventArgs e) => Value += Step;

   private void OnDown(object sender, RoutedEventArgs e) => Value -= Step;
}
