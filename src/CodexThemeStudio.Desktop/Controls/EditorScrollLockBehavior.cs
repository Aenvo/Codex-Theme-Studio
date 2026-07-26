using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace CodexThemeStudio.Desktop.Controls;

public static class EditorScrollLockBehavior
{
    public static readonly DependencyProperty IsLockedProperty = DependencyProperty.RegisterAttached(
        "IsLocked",
        typeof(bool),
        typeof(EditorScrollLockBehavior),
        new PropertyMetadata(false, OnIsLockedChanged));

    public static bool GetIsLocked(DependencyObject element) => (bool)element.GetValue(IsLockedProperty);

    public static void SetIsLocked(DependencyObject element, bool value) => element.SetValue(IsLockedProperty, value);

    private static void OnIsLockedChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not ScrollViewer scrollViewer)
        {
            return;
        }

        if ((bool)args.OldValue)
        {
            scrollViewer.PreviewMouseWheel -= OnPreviewMouseWheel;
            scrollViewer.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        }

        if ((bool)args.NewValue)
        {
            scrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;
            scrollViewer.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs args) => args.Handled = true;

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (FindVisualParent<ScrollBar>(args.OriginalSource as DependencyObject) is not null)
        {
            args.Handled = true;
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is Visual)
        {
            if (child is T typed)
            {
                return typed;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }
}
