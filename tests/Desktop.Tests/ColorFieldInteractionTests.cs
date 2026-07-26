using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodexThemeStudio.Desktop.Controls;
using CodexThemeStudio.Desktop.Services;

namespace CodexThemeStudio.Desktop.Tests;

public sealed class ColorFieldInteractionTests
{
    [Fact]
    public async Task Popup_UpdatesHexRgbCssAndKeepsRealTimeChangesAfterClose()
    {
        await WpfTestHost.InvokeAsync(async () =>
        {
            var history = new ColorHistoryService(CreateTemporaryDirectory());
            var field = new ColorField
            {
                Value = "#B98AE933",
                ColorHistory = history.Colors,
                ColorHistoryService = history,
            };
            var window = CreateWindow(field);
            await Dispatcher.Yield(DispatcherPriority.Background);

            var open = Assert.IsType<Button>(field.FindName("OpenButton"));
            var popup = Assert.IsType<Popup>(field.FindName("PickerPopup"));
            var formats = Assert.IsType<ComboBox>(field.FindName("FormatSelector"));
            var hex = Assert.IsType<TextBox>(field.FindName("HexInput"));
            var hexOpacity = Assert.IsType<TextBox>(field.FindName("HexOpacityInput"));
            var hexInputs = Assert.IsType<Grid>(field.FindName("HexInputs"));
            var red = Assert.IsType<TextBox>(field.FindName("RedInput"));
            var green = Assert.IsType<TextBox>(field.FindName("GreenInput"));
            var blue = Assert.IsType<TextBox>(field.FindName("BlueInput"));
            var opacity = Assert.IsType<TextBox>(field.FindName("AlphaPercentInput"));
            var css = Assert.IsType<TextBox>(field.FindName("CssInput"));
            var hue = Assert.IsType<Slider>(field.FindName("HueSlider"));
            var saturation = Assert.IsType<Slider>(field.FindName("SaturationSlider"));
            var historyPalette = Assert.IsType<ItemsControl>(field.FindName("HistoryPalette"));

            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(popup.IsOpen);
            Assert.True(field.IsPickerOpen);
            var selectionSurfaceBrush = Assert.IsType<SolidColorBrush>(
                Application.Current.Resources["SelectionSurfaceBrush"]);
            Assert.Equal(
                selectionSurfaceBrush.Color,
                Assert.IsType<SolidColorBrush>(open.Background).Color);
            Assert.True(SliderJumpBehavior.GetIsEnabled(saturation));
            var hueTrack = Assert.Single(FindVisualChildren<Track>(hue));
            Assert.Equal(hue.Minimum, hueTrack.Minimum);
            Assert.Equal(hue.Maximum, hueTrack.Maximum);
            Assert.Equal(hue.Value, hueTrack.Value);
            Assert.Same(history.Colors, historyPalette.ItemsSource);
            Assert.Null(field.FindName("ThemePalette"));
            Assert.Empty(historyPalette.Items);
            Assert.Equal("#B98AE9", hex.Text);
            Assert.Equal("20", hexOpacity.Text);
            hex.Text = "#11223344";
            Assert.Equal("#11223344", field.Value);
            Assert.Equal("#112233", hex.Text);
            Assert.Equal("27", hexOpacity.Text);
            await Dispatcher.Yield(DispatcherPriority.Background);
            Assert.Empty(history.Colors);
            Assert.Empty(historyPalette.Items);
            history.Record("#11223344");
            await Dispatcher.Yield(DispatcherPriority.Background);
            Assert.Contains("#11223344", history.Colors);
            Assert.Single(historyPalette.Items);
            var historySwatch = Assert.Single(FindVisualChildren<Button>(historyPalette));
            Assert.InRange(historySwatch.ActualWidth, 22, 23);
            Assert.Contains(
                FindVisualChildren<Border>(historySwatch),
                border => border.Background is SolidColorBrush brush &&
                          brush.Color == Color.FromArgb(0x44, 0x11, 0x22, 0x33));

            formats.SelectedIndex = 1;
            Assert.Equal(Visibility.Visible, red.Visibility);
            red.Text = "255";
            green.Text = "89";
            blue.Text = "89";
            opacity.Text = "50";
            Assert.Equal("#FF595980", field.Value);

            formats.SelectedIndex = 2;
            css.Text = "rgba(12, 34, 56, 0.5)";
            Assert.Equal("#0C223880", field.Value);
            hue.Value = 180;
            await Dispatcher.Yield(DispatcherPriority.Background);
            Assert.Equal(hue.Value, hueTrack.Value);
            saturation.Value = 0;
            Assert.NotEqual("#0C223880", field.Value);

            popup.IsOpen = false;
            await Dispatcher.Yield(DispatcherPriority.Background);
            Assert.NotEqual("#B98AE933", field.Value);
            Assert.False(field.IsPickerOpen);
            Assert.Equal(Visibility.Collapsed, hexInputs.Visibility);
            window.Close();
        });
    }

    [Fact]
    public async Task Popup_InputClickSelectsAllAndInvalidTextRestoresLatestValue()
    {
        await WpfTestHost.InvokeAsync(async () =>
        {
            var field = new ColorField { Value = "#3B82F680" };
            var window = CreateWindow(field);
            await Dispatcher.Yield(DispatcherPriority.Background);
            var open = Assert.IsType<Button>(field.FindName("OpenButton"));
            var hex = Assert.IsType<TextBox>(field.FindName("HexInput"));
            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.True(hex.Focus());
            Assert.Equal(hex.Text.Length, hex.SelectionLength);
            hex.Text = "not-a-color";
            Assert.True(open.Focus());
            Assert.Equal("#3B82F6", hex.Text);
            window.Close();
        });
    }

    [Fact]
    public async Task Popup_HueChangeOnAchromaticColorRetainsTheSelectedHueAndSaturation()
    {
        await WpfTestHost.InvokeAsync(async () =>
        {
            var field = new ColorField { Value = "#1C1C1CCC" };
            var window = CreateWindow(field);
            await Dispatcher.Yield(DispatcherPriority.Background);
            var open = Assert.IsType<Button>(field.FindName("OpenButton"));
            var hue = Assert.IsType<Slider>(field.FindName("HueSlider"));
            var saturation = Assert.IsType<Slider>(field.FindName("SaturationSlider"));

            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            hue.Value = 180;
            await Dispatcher.Yield(DispatcherPriority.Background);
            Assert.Equal(180, hue.Value);
            Assert.Equal(0, saturation.Value);
            Assert.Equal("#1C1C1CCC", field.Value);

            saturation.Value = 1;
            await Dispatcher.Yield(DispatcherPriority.Background);
            Assert.Equal(180, hue.Value);
            Assert.Equal("#001C1CCC", field.Value);
            window.Close();
        });
    }

    private static Window CreateWindow(ColorField field)
    {
        var window = new Window
        {
            Content = field,
            Left = -10000,
            Top = -10000,
            Width = 360,
            Height = 500,
            ShowInTaskbar = false,
        };
        window.Show();
        window.Activate();
        return window;
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"CodexThemeStudio.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
