using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using CodexThemeStudio.Desktop.Controls;

namespace CodexThemeStudio.Desktop.Tests;

public sealed class AppDialogWindowTests
{
    [Fact]
    public void Confirmation_UsesProjectChromeAndAccessibleActions()
    {
        WpfTestHost.Invoke(() =>
        {
            var dialog = AppDialogWindow.CreateConfirmation(
                "移入应用回收站",
                "“示例主题”将从资料库隐藏，但主题文件会保留以便恢复。",
                owner: null);

            Assert.Equal(WindowStyle.None, dialog.WindowStyle);
            Assert.False(dialog.AllowsTransparency);
            Assert.False(dialog.ShowInTaskbar);
            Assert.True(dialog.UseLayoutRounding);
            Assert.True(dialog.SnapsToDevicePixels);
            Assert.Equal(
                Application.Current.Resources["PopoverBrush"],
                dialog.Background);
            Assert.Null(dialog.DialogChrome.Effect);
            var chrome = WindowChrome.GetWindowChrome(dialog);
            Assert.NotNull(chrome);
            Assert.Equal(new CornerRadius(12), chrome.CornerRadius);
            Assert.Equal("移入应用回收站", dialog.TitleText.Text);
            Assert.Equal(Visibility.Visible, dialog.CancelButton.Visibility);
            Assert.True(dialog.CancelButton.IsCancel);
            Assert.True(dialog.ConfirmButton.IsDefault);
            Assert.Same(
                Application.Current.Resources["PrimaryButtonStyle"],
                dialog.ConfirmButton.Style);
            Assert.Equal(
                Application.Current.Resources["PopoverBrush"],
                dialog.DialogChrome.Background);

            RenderDialog(
                dialog,
                96,
                Environment.GetEnvironmentVariable("CTS_DIALOG_SCREENSHOT_PATH"));
            RenderDialog(dialog, 192, screenshotPath: null);
        });
    }

    [Fact]
    public void InformationErrorAndTextInput_UseSharedDialogStates()
    {
        WpfTestHost.Invoke(() =>
        {
            var information = AppDialogWindow.CreateInformation(
                "导入完成",
                "主题已导入。",
                owner: null);
            Assert.Equal(Visibility.Collapsed, information.CancelButton.Visibility);
            Assert.Equal("知道了", information.ConfirmButton.Content);

            var error = AppDialogWindow.CreateInformation(
                "启动失败",
                "无法完成启动。",
                owner: null,
                isError: true);
            Assert.Equal("关闭", error.ConfirmButton.Content);

            var input = AppDialogWindow.CreateTextInput(
                "重命名主题",
                "输入新的主题名称。",
                "示例主题",
                owner: null);
            Assert.Equal(Visibility.Visible, input.InputTextBox.Visibility);
            Assert.Equal("示例主题", input.InputText);
            Assert.Equal(120, input.InputTextBox.MaxLength);
            Assert.True(input.DialogChrome.Focusable);
            Assert.Null(input.DialogChrome.FocusVisualStyle);

            FocusManager.SetFocusedElement(input, input.InputTextBox);
            Assert.Same(
                input.InputTextBox,
                FocusManager.GetFocusedElement(input));

            var inputClick = new MouseButtonEventArgs(
                Mouse.PrimaryDevice,
                Environment.TickCount,
                MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
            };
            input.InputTextBox.RaiseEvent(inputClick);
            Assert.False(inputClick.Handled);
            Assert.Same(
                input.InputTextBox,
                FocusManager.GetFocusedElement(input));

            var surfaceClick = new MouseButtonEventArgs(
                Mouse.PrimaryDevice,
                Environment.TickCount,
                MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
            };
            input.DialogChrome.RaiseEvent(surfaceClick);
            Assert.False(surfaceClick.Handled);
            Assert.Same(
                input.DialogChrome,
                FocusManager.GetFocusedElement(input));
            RenderDialog(
                input,
                96,
                Environment.GetEnvironmentVariable("CTS_DIALOG_INPUT_SCREENSHOT_PATH"));
        });
    }

    [Fact]
    public void TextBox_UsesSharedRoundedInteractionTemplate()
    {
        WpfTestHost.Invoke(() =>
        {
            var textBox = new TextBox
            {
                Style = Assert.IsType<Style>(
                    Application.Current.Resources[typeof(TextBox)]),
            };
            textBox.ApplyTemplate();

            var chrome = Assert.IsType<Border>(
                textBox.Template.FindName("InputChrome", textBox));
            var contentHost = Assert.IsType<ScrollViewer>(
                textBox.Template.FindName("PART_ContentHost", textBox));
            Assert.Equal(new CornerRadius(8), chrome.CornerRadius);
            Assert.Equal(new Thickness(0), chrome.Padding);
            Assert.Equal(new Thickness(8, 0, 8, 0), textBox.Padding);
            Assert.Equal(VerticalAlignment.Center, textBox.VerticalContentAlignment);
            Assert.Null(textBox.FocusVisualStyle);
            Assert.False(contentHost.Focusable);
            Assert.Contains(
                textBox.Template.Triggers.OfType<Trigger>(),
                trigger => trigger.Property == UIElement.IsMouseOverProperty);
            Assert.Contains(
                textBox.Template.Triggers.OfType<Trigger>(),
                trigger => trigger.Property == UIElement.IsKeyboardFocusedProperty);
            Assert.Contains(
                textBox.Template.Triggers.OfType<Trigger>(),
                trigger => trigger.Property == UIElement.IsEnabledProperty);
            AssertTriggerResource(
                textBox.Template,
                UIElement.IsMouseOverProperty,
                Border.BackgroundProperty,
                "CardBrush");
            AssertTriggerResource(
                textBox.Template,
                UIElement.IsKeyboardFocusedProperty,
                Border.BorderBrushProperty,
                "RingBrush");
            AssertTriggerValue(
                textBox.Template,
                UIElement.IsKeyboardFocusedProperty,
                Border.BorderThicknessProperty,
                new Thickness(2));
            AssertTriggerValue(
                textBox.Template,
                UIElement.IsEnabledProperty,
                UIElement.OpacityProperty,
                0.42);
        });
    }

    [Fact]
    public void DialogBackdrop_BlursDimsAndRestoresMainWindow()
    {
        WpfTestHost.Invoke(() =>
        {
            using var fixture = new ViewModelFixture(themeCount: 2);
            fixture.ViewModel.InitializeAsync().GetAwaiter().GetResult();
            var window = new MainWindow(fixture.ViewModel);

            Assert.Equal(Visibility.Collapsed, window.DialogLightbox.Visibility);
            Assert.Null(window.ApplicationSurface.Effect);

            using (window.EnterDialogBackdrop())
            {
                Assert.Equal(Visibility.Visible, window.DialogLightbox.Visibility);
                var blur = Assert.IsType<BlurEffect>(window.ApplicationSurface.Effect);
                Assert.Equal(4, blur.Radius);
                var lightboxBrush = Assert.IsType<SolidColorBrush>(
                    window.DialogLightbox.Background);
                Assert.Equal(Color.FromArgb(0x99, 0, 0, 0), lightboxBrush.Color);
                RenderElement(
                    Assert.IsAssignableFrom<FrameworkElement>(window.Content),
                    960,
                    620,
                    96,
                    Environment.GetEnvironmentVariable(
                        "CTS_DIALOG_BACKDROP_SCREENSHOT_PATH"));
            }

            Assert.Equal(Visibility.Collapsed, window.DialogLightbox.Visibility);
            Assert.Null(window.ApplicationSurface.Effect);
        });
    }

    private static void RenderDialog(
        AppDialogWindow dialog,
        double dpi,
        string? screenshotPath)
    {
        var root = Assert.IsAssignableFrom<FrameworkElement>(dialog.Content);
        const int width = 500;
        root.Measure(new Size(width, double.PositiveInfinity));
        var height = (int)Math.Ceiling(root.DesiredSize.Height);
        RenderElement(root, width, height, dpi, screenshotPath);
    }

    private static void RenderElement(
        FrameworkElement root,
        int width,
        int height,
        double dpi,
        string? screenshotPath)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            (int)Math.Round(width * dpi / 96),
            (int)Math.Round(height * dpi / 96),
            dpi,
            dpi,
            PixelFormats.Pbgra32);
        bitmap.Render(root);
        Assert.True(bitmap.PixelWidth >= width);
        Assert.True(bitmap.PixelHeight >= height);

        if (string.IsNullOrWhiteSpace(screenshotPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath)!);
        using var stream = File.Create(screenshotPath);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static void AssertTriggerResource(
        ControlTemplate template,
        DependencyProperty triggerProperty,
        DependencyProperty setterProperty,
        string expectedResourceKey)
    {
        var setter = template.Triggers
            .OfType<Trigger>()
            .Single(trigger => trigger.Property == triggerProperty)
            .Setters
            .OfType<Setter>()
            .Single(item => item.Property == setterProperty);
        var resource = Assert.IsType<DynamicResourceExtension>(setter.Value);
        Assert.Equal(expectedResourceKey, resource.ResourceKey);
    }

    private static void AssertTriggerValue(
        ControlTemplate template,
        DependencyProperty triggerProperty,
        DependencyProperty setterProperty,
        object expectedValue)
    {
        var setter = template.Triggers
            .OfType<Trigger>()
            .Single(trigger => trigger.Property == triggerProperty)
            .Setters
            .OfType<Setter>()
            .Single(item => item.Property == setterProperty);
        Assert.Equal(expectedValue, setter.Value);
    }
}
