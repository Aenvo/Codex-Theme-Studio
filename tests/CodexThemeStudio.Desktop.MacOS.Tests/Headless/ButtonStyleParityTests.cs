using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CodexThemeStudio.Desktop.MacOS.Tests.Headless;

public sealed class ButtonStyleParityTests
{
    [Fact]
    public Task WindowsButtonComponentStatesAreMappedExactly() =>
        AvaloniaTestHost.DispatchAsync(() =>
        {
            var primary = CreateButton("临时应用", "primary");
            var secondary = CreateButton("设为持久主题");
            var ghost = CreateButton("还原外观", "ghost");
            var hoverPrimary = CreateButton("临时应用", "primary");
            var hoverSecondary = CreateButton("设为持久主题");
            var hoverGhost = CreateButton("还原外观", "ghost");
            var pressedPrimary = CreateButton("临时应用", "primary");
            var pressedSecondary = CreateButton("设为持久主题");
            var pressedGhost = CreateButton("还原外观", "ghost");
            var focusedPrimary = CreateButton("临时应用", "primary");
            var focusedSecondary = CreateButton("设为持久主题");
            var focusedGhost = CreateButton("还原外观", "ghost");
            var disabledPrimary = CreateButton("临时应用", "primary");
            var disabledSecondary = CreateButton("设为持久主题");
            var disabledGhost = CreateButton("还原外观", "ghost");
            var window = new Window
            {
                Width = 760,
                Height = 340,
                Content = new StackPanel
                {
                    Margin = new(20),
                    Spacing = 12,
                    Children =
                    {
                        CreateStateRow("默认", primary, secondary, ghost),
                        CreateStateRow(
                            "Hover",
                            hoverPrimary,
                            hoverSecondary,
                            hoverGhost),
                        CreateStateRow(
                            "Pressed",
                            pressedPrimary,
                            pressedSecondary,
                            pressedGhost),
                        CreateStateRow(
                            "Focus",
                            focusedPrimary,
                            focusedSecondary,
                            focusedGhost),
                        CreateStateRow(
                            "Disabled",
                            disabledPrimary,
                            disabledSecondary,
                            disabledGhost),
                    },
                },
            };
            window.Show();

            AssertButton(
                "primary/default",
                primary,
                "#2563EB",
                "#2563EB",
                "#FFFFFF",
                1);
            AssertButton(
                "secondary/default",
                secondary,
                "#242424",
                "#303030",
                "#F5F5F5",
                1);
            AssertButton(
                "ghost/default",
                ghost,
                "#00000000",
                "#00000000",
                "#F5F5F5",
                1);

            hoverPrimary.SetState(":pointerover", true);
            hoverSecondary.SetState(":pointerover", true);
            hoverGhost.SetState(":pointerover", true);
            AssertButton(
                "primary/hover",
                hoverPrimary,
                "#1D4ED8",
                "#1D4ED8",
                "#FFFFFF",
                1);
            AssertButton(
                "secondary/hover",
                hoverSecondary,
                "#1F1F1F",
                "#404040",
                "#F5F5F5",
                1);
            AssertButton(
                "ghost/hover",
                hoverGhost,
                "#1F1F1F",
                "#404040",
                "#F5F5F5",
                1);

            pressedPrimary.SetState(":pointerover", true);
            pressedSecondary.SetState(":pointerover", true);
            pressedGhost.SetState(":pointerover", true);
            pressedPrimary.SetState(":pressed", true);
            pressedSecondary.SetState(":pressed", true);
            pressedGhost.SetState(":pressed", true);
            AssertButton(
                "primary/pressed",
                pressedPrimary,
                "#1E40AF",
                "#1E40AF",
                "#FFFFFF",
                1);
            AssertButton(
                "secondary/pressed",
                pressedSecondary,
                "#1F1F1F",
                "#404040",
                "#F5F5F5",
                0.82);
            AssertButton(
                "ghost/pressed",
                pressedGhost,
                "#1F1F1F",
                "#404040",
                "#F5F5F5",
                0.82);

            focusedPrimary.SetState(":focus-visible", true);
            focusedSecondary.SetState(":focus-visible", true);
            focusedGhost.SetState(":focus-visible", true);
            AssertButton(
                "primary/focus",
                focusedPrimary,
                "#2563EB",
                "#60A5FA",
                "#FFFFFF",
                1,
                borderThickness: 2);
            AssertButton(
                "secondary/focus",
                focusedSecondary,
                "#242424",
                "#60A5FA",
                "#F5F5F5",
                1,
                borderThickness: 2);
            AssertButton(
                "ghost/focus",
                focusedGhost,
                "#00000000",
                "#60A5FA",
                "#F5F5F5",
                1,
                borderThickness: 2);

            disabledPrimary.IsEnabled = false;
            disabledSecondary.IsEnabled = false;
            disabledGhost.IsEnabled = false;
            AssertButton(
                "primary/disabled",
                disabledPrimary,
                "#2563EB",
                "#2563EB",
                "#FFFFFF",
                0.42);
            AssertButton(
                "secondary/disabled",
                disabledSecondary,
                "#242424",
                "#303030",
                "#F5F5F5",
                0.42);
            AssertButton(
                "ghost/disabled",
                disabledGhost,
                "#00000000",
                "#00000000",
                "#F5F5F5",
                0.42);

            Capture(window, "21-button-component-states.png");
            window.Close();
        });

    private static StackPanel CreateStateRow(
        string label,
        params Button[] buttons) =>
        new()
        {
            Spacing = 8,
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Children =
            {
                new TextBlock
                {
                    Width = 70,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Text = label,
                },
                buttons[0],
                buttons[1],
                buttons[2],
            },
        };

    private static ProbeButton CreateButton(
        string content,
        string? styleClass = null)
    {
        var button = new ProbeButton
        {
            Content = content,
            MinWidth = 120,
        };
        if (styleClass is not null)
        {
            button.Classes.Add(styleClass);
        }

        return button;
    }

    private static void AssertButton(
        string state,
        Button button,
        string background,
        string border,
        string foreground,
        double opacity,
        double borderThickness = 1)
    {
        AssertBrush(state, "background", Color.Parse(background), button.Background);
        AssertBrush(state, "border", Color.Parse(border), button.BorderBrush);
        AssertBrush(state, "foreground", Color.Parse(foreground), button.Foreground);
        Assert.True(
            Math.Abs(opacity - button.Opacity) < 0.005,
            $"{state} opacity: expected {opacity}, actual {button.Opacity}");
        Assert.True(
            button.Padding == new Avalonia.Thickness(14, 7),
            $"{state} padding: expected 14,7, actual {button.Padding}");
        Assert.True(
            button.CornerRadius == new Avalonia.CornerRadius(8),
            $"{state} corner radius: expected 8, actual {button.CornerRadius}");
        Assert.True(
            Math.Abs(36 - button.MinHeight) < 0.005,
            $"{state} min height: expected 36, actual {button.MinHeight}");
        Assert.True(
            button.BorderThickness == new Avalonia.Thickness(borderThickness),
            $"{state} border thickness: expected {borderThickness}, actual {button.BorderThickness}");
    }

    private static void AssertBrush(
        string state,
        string property,
        Color expected,
        IBrush? brush)
    {
        if (brush is null)
        {
            Assert.True(
                expected.A == 0,
                $"{state} {property}: expected {expected}, actual null");
            return;
        }

        var actual = Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
        if (expected.A == 0)
        {
            Assert.True(
                actual.A == 0,
                $"{state} {property}: expected transparent, actual {actual}");
            return;
        }

        Assert.True(
            expected == actual,
            $"{state} {property}: expected {expected}, actual {actual}");
    }

    private static void Capture(Window window, string fileName)
    {
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var directory = Environment.GetEnvironmentVariable(
            "CTS_UI_AUDIT_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        frame.Save(
            Path.Combine(directory, fileName),
            PngBitmapEncoderOptions.Default);
    }

    private sealed class ProbeButton : Button
    {
        protected override Type StyleKeyOverride => typeof(Button);

        public void SetState(string pseudoClass, bool value) =>
            PseudoClasses.Set(pseudoClass, value);
    }
}
