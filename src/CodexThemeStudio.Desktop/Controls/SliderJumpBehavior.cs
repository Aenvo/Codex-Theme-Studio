using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace CodexThemeStudio.Desktop.Controls;

public static class SliderJumpBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(SliderJumpBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static bool TrySetValueFromPoint(Slider slider, Point point)
    {
        ArgumentNullException.ThrowIfNull(slider);
        var track = FindVisualChild<Track>(slider);
        if (track is null)
        {
            return false;
        }

        var trackPoint = slider.TranslatePoint(point, track);
        slider.SetCurrentValue(
            Slider.ValueProperty,
            Math.Clamp(track.ValueFromPoint(trackPoint), slider.Minimum, slider.Maximum));
        return true;
    }

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not Slider slider)
        {
            return;
        }

        if ((bool)args.OldValue)
        {
            slider.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        }

        if ((bool)args.NewValue)
        {
            slider.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        }
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (sender is not Slider slider || FindVisualParent<Thumb>(args.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (TrySetValueFromPoint(slider, args.GetPosition(slider)))
        {
            slider.Focus();
            args.Handled = true;
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typed)
            {
                return typed;
            }

            if (FindVisualChild<T>(child) is { } result)
            {
                return result;
            }
        }

        return null;
    }

    private static T? FindVisualParent<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T typed)
            {
                return typed;
            }

            child = child is Visual ? VisualTreeHelper.GetParent(child) : null;
        }

        return null;
    }
}
