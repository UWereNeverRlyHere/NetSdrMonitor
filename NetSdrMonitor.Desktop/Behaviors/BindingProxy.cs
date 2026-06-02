using System.Windows;

namespace NetSdrMonitor.Desktop.Behaviors;

/// <summary>
/// Переносить DataContext у місця, відрізані від візуального дерева (меню трея, CompositeCollection):
/// як Freezable успадковує контекст і віддає його через властивість Data.
/// </summary>
public sealed class BindingProxy : Freezable
{
   public static readonly DependencyProperty DataProperty = DependencyProperty
        .Register(nameof(Data), typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));

   public object? Data
   {
      get => GetValue(DataProperty);
      set => SetValue(DataProperty, value);
   }

   protected override Freezable CreateInstanceCore() => new BindingProxy();
}
