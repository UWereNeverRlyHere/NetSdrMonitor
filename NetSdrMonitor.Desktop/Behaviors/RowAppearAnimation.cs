using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NetSdrMonitor.Desktop.Behaviors;

/// <summary>
/// Приєднана поведінка для будь-якого <see cref="DataGrid"/>: нові рядки, чий елемент реалізує
/// <see cref="IAnimatedRow"/> і має <c>NeedFadeIn = true</c>, з'являються з плавною анімацією
/// (прозорість + легкий зсув зверху). Вмикається через <c>RowAppearAnimation.Enabled="True"</c>.
/// </summary>
public static class RowAppearAnimation
{
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(500));

    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(RowAppearAnimation), new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid)
            return;

        // підписка ідемпотентна: спершу прибираємо, тоді (за потреби) додаємо
        grid.LoadingRow -= OnLoadingRow;
        if (e.NewValue is true)
            grid.LoadingRow += OnLoadingRow;
    }

    // рядок матеріалізується (зокрема при віртуалізації/переробці контейнерів) — анімуємо лише
    // позначені новими; прапорець гасимо одразу, тож ефект разовий і не спрацьовує при прокручуванні
    private static void OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.Item is IAnimatedRow { NeedFadeIn: true } row)
        {
            row.NeedFadeIn = false;
            PlayFadeIn(e.Row);
        }
    }

    // плавна поява: прозорість 0→1 + легкий зсув зверху
    private static void PlayFadeIn(UIElement row)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var slide = new TranslateTransform();
        row.RenderTransform = slide;

        row.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, FadeDuration) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-120, 0, FadeDuration) { EasingFunction = ease });
    }
}
