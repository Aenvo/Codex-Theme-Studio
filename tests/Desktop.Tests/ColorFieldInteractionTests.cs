using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using CodexThemeStudio.Desktop.Controls;

namespace CodexThemeStudio.Desktop.Tests;

public sealed class ColorFieldInteractionTests
{
    [Fact]
    public async Task Popup_HexAlphaApplyAndOutsideCloseStaySynchronized()
    {
        await WpfTestHost.InvokeAsync(async () =>
        {
            string[] colors =
            [
                "#100D13", "#1A151F", "#B98AE9",
                "#EFE9F0", "#A99AB0", "#B98AE933",
            ];
            var panel = new StackPanel();
            var fields = colors.Select(color => new ColorField { Value = color }).ToArray();
            foreach (var colorField in fields)
            {
                panel.Children.Add(colorField);
            }

            var window = new Window
            {
                Content = panel,
                Left = -10000,
                Top = -10000,
                Width = 360,
                Height = 500,
                ShowInTaskbar = false,
            };
            window.Show();
            window.Activate();
            await Dispatcher.Yield(DispatcherPriority.Background);
            foreach (var colorField in fields)
            {
                var colorOpen = Assert.IsType<Button>(colorField.FindName("OpenButton"));
                var colorPopup = Assert.IsType<Popup>(colorField.FindName("PickerPopup"));
                colorOpen.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(colorPopup.IsOpen);
                colorPopup.IsOpen = false;
                await Dispatcher.Yield(DispatcherPriority.Background);
            }

            var field = fields[^1];
            var open = Assert.IsType<Button>(field.FindName("OpenButton"));
            var popup = Assert.IsType<Popup>(field.FindName("PickerPopup"));
            var hex = Assert.IsType<TextBox>(field.FindName("HexInput"));
            var alpha = Assert.IsType<Slider>(field.FindName("AlphaSlider"));

            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(popup.IsOpen);
            Assert.Equal("#B98AE933", hex.Text);
            Assert.Equal(51, alpha.Value);

            hex.Text = "#11223344";
            Assert.Equal("#11223344", field.Value);
            alpha.Value = 255;
            Assert.Equal("#112233", field.Value);

            popup.IsOpen = false;
            await Dispatcher.Yield(DispatcherPriority.Background);
            Assert.Equal("#B98AE933", field.Value);

            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            hex.Text = "#3B82F680";
            var apply = FindButton(popup.Child, "应用");
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(popup.IsOpen);
            Assert.Equal("#3B82F680", field.Value);
            window.Close();
        });
    }

    private static Button FindButton(DependencyObject root, string content)
    {
        if (root is Button { Content: string text } button && text == content)
        {
            return button;
        }

        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            try
            {
                return FindButton(System.Windows.Media.VisualTreeHelper.GetChild(root, index), content);
            }
            catch (InvalidOperationException)
            {
            }
        }

        throw new InvalidOperationException($"Button '{content}' was not found.");
    }

}
