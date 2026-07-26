using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CodexThemeStudio.Desktop.Controls;

public static class ComboBoxMouseWheelBehavior
{
    public static readonly DependencyProperty ForwardToScrollViewerProperty = DependencyProperty.RegisterAttached(
        "ForwardToScrollViewer",
        typeof(bool),
        typeof(ComboBoxMouseWheelBehavior),
        new PropertyMetadata(false, OnForwardToScrollViewerChanged));

    public static bool GetForwardToScrollViewer(DependencyObject element) =>
        (bool)element.GetValue(ForwardToScrollViewerProperty);

    public static void SetForwardToScrollViewer(DependencyObject element, bool value) =>
        element.SetValue(ForwardToScrollViewerProperty, value);

    private static void OnForwardToScrollViewerChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not ComboBox comboBox)
        {
            return;
        }

        if ((bool)args.OldValue)
        {
            comboBox.PreviewMouseWheel -= OnPreviewMouseWheel;
        }

        if ((bool)args.NewValue)
        {
            comboBox.PreviewMouseWheel += OnPreviewMouseWheel;
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs args)
    {
        if (sender is not ComboBox { IsDropDownOpen: false } comboBox ||
            FindAncestor<ScrollViewer>(comboBox) is not { } scrollViewer)
        {
            return;
        }

        args.Handled = true;
        scrollViewer.RaiseEvent(new MouseWheelEventArgs(args.MouseDevice, args.Timestamp, args.Delta)
        {
            RoutedEvent = Mouse.MouseWheelEvent,
            Source = scrollViewer,
        });
    }

    private static T? FindAncestor<T>(DependencyObject child)
        where T : DependencyObject
    {
        for (DependencyObject? current = child; current is Visual; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T ancestor)
            {
                return ancestor;
            }
        }

        return null;
    }
}
