using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CodexThemeStudio.Desktop.Tests;

public sealed class DesignTokenTests
{
    [Fact]
    public void ApplicationPalette_UsesNeutralSurfacesAndAccessibleBlueAccents()
    {
        WpfTestHost.Invoke(() =>
        {
            var resources = Application.Current.Resources;

            Assert.Equal(Parse("#111111"), Color(resources, "BackgroundColor"));
            Assert.Equal(Parse("#2563EB"), Color(resources, "PrimaryColor"));
            Assert.Equal(Parse("#1D4ED8"), Color(resources, "PrimaryHoverColor"));
            Assert.Equal(Parse("#1E40AF"), Color(resources, "PrimaryPressedColor"));
            Assert.Equal(Parse("#FFFFFF"), Color(resources, "PrimaryForegroundColor"));
            Assert.Equal(Parse("#60A5FA"), Color(resources, "RingColor"));

            foreach (var key in new[]
                     {
                         "BackgroundColor",
                         "CardColor",
                         "PopoverColor",
                         "SecondaryColor",
                         "MutedColor",
                         "BorderColor",
                         "InputColor",
                         "SidebarColor",
                         "SidebarAccentColor",
                     })
            {
                var color = Color(resources, key);
                Assert.True(
                    color.R == color.G && color.G == color.B,
                    $"{key} must remain neutral, but was {color}.");
            }

            AssertContrastAtLeast(
                Color(resources, "ForegroundColor"),
                Color(resources, "BackgroundColor"),
                4.5);
            AssertContrastAtLeast(
                Color(resources, "MutedForegroundColor"),
                Color(resources, "BackgroundColor"),
                4.5);
            AssertContrastAtLeast(
                Color(resources, "PrimaryForegroundColor"),
                Color(resources, "PrimaryColor"),
                4.5);
            AssertContrastAtLeast(
                Color(resources, "PrimaryForegroundColor"),
                Color(resources, "PrimaryHoverColor"),
                4.5);
            AssertContrastAtLeast(
                Color(resources, "PrimaryForegroundColor"),
                Color(resources, "PrimaryPressedColor"),
                4.5);
            AssertContrastAtLeast(
                Color(resources, "RingColor"),
                Color(resources, "BackgroundColor"),
                3.0);
        });
    }

    [Fact]
    public void PrimaryButtonStyle_UsesDedicatedInteractionBrushes()
    {
        WpfTestHost.Invoke(() =>
        {
            var resources = Application.Current.Resources;
            var style = Assert.IsType<Style>(resources["PrimaryButtonStyle"]);
            var template = Assert.IsType<ControlTemplate>(
                style.Setters
                    .OfType<Setter>()
                    .Single(setter => setter.Property == Control.TemplateProperty)
                    .Value);

            AssertTriggerBrush(
                template,
                UIElement.IsMouseOverProperty,
                "PrimaryHoverBrush");
            AssertTriggerBrush(
                template,
                Button.IsPressedProperty,
                "PrimaryPressedBrush");
        });
    }

    [Fact]
    public void ComboBoxStyle_RestoresClosedChromeAndUsesBorderlessSelectedItems()
    {
        WpfTestHost.Invoke(() =>
        {
            var resources = Application.Current.Resources;
            var comboBoxStyle = Assert.IsType<Style>(resources[typeof(ComboBox)]);
            var comboBoxTemplate = Assert.IsType<ControlTemplate>(
                comboBoxStyle.Setters
                    .OfType<Setter>()
                    .Single(setter => setter.Property == Control.TemplateProperty)
                    .Value);

            Assert.DoesNotContain(
                comboBoxTemplate.Triggers.OfType<Trigger>(),
                trigger => trigger.Property == UIElement.IsKeyboardFocusWithinProperty);

            var openTrigger = Assert.Single(
                comboBoxTemplate.Triggers.OfType<Trigger>(),
                trigger =>
                    trigger.Property == ComboBox.IsDropDownOpenProperty &&
                    Equals(trigger.Value, true));
            Assert.Single(openTrigger.EnterActions);
            Assert.Single(openTrigger.ExitActions);
            AssertRotation(openTrigger.EnterActions, 180);
            AssertRotation(openTrigger.ExitActions, 0);
            var openBorder = openTrigger.Setters
                .OfType<Setter>()
                .Single(setter => setter.Property == Border.BorderBrushProperty);
            Assert.Equal(
                "PrimaryBrush",
                Assert.IsType<DynamicResourceExtension>(openBorder.Value).ResourceKey);

            var comboBox = new ComboBox
            {
                Style = comboBoxStyle,
                ItemsSource = new[] { "One", "Two" },
                SelectedIndex = 0,
            };
            comboBox.Measure(new Size(180, 42));
            comboBox.Arrange(new Rect(0, 0, 180, 42));
            comboBox.ApplyTemplate();
            var chrome = Assert.IsType<Border>(
                comboBox.Template.FindName("Chrome", comboBox));
            var popup = Assert.IsType<Popup>(
                comboBox.Template.FindName("PART_Popup", comboBox));
            Assert.Equal(5, popup.VerticalOffset);

            Assert.Equal(
                Color(resources, "InputColor"),
                Assert.IsType<SolidColorBrush>(chrome.BorderBrush).Color);
            Assert.Equal(new Thickness(1), chrome.BorderThickness);

            var itemStyle = Assert.IsType<Style>(resources[typeof(ComboBoxItem)]);
            var itemTemplate = Assert.IsType<ControlTemplate>(
                itemStyle.Setters
                    .OfType<Setter>()
                    .Single(setter => setter.Property == Control.TemplateProperty)
                    .Value);
            var selectedTrigger = Assert.Single(
                itemTemplate.Triggers.OfType<Trigger>(),
                trigger =>
                    trigger.Property == ListBoxItem.IsSelectedProperty &&
                    Equals(trigger.Value, true));
            var selectedBorder = selectedTrigger.Setters
                .OfType<Setter>()
                .Single(setter => setter.Property == Border.BorderBrushProperty);
            Assert.Equal(
                Colors.Transparent,
                Assert.IsType<SolidColorBrush>(selectedBorder.Value).Color);
        });
    }

    private static Color Color(ResourceDictionary resources, string key) =>
        Assert.IsType<Color>(resources[key]);

    private static Color Parse(string value) =>
        (Color)ColorConverter.ConvertFromString(value);

    private static void AssertTriggerBrush(
        ControlTemplate template,
        DependencyProperty stateProperty,
        string expectedResourceKey)
    {
        var trigger = template.Triggers
            .OfType<Trigger>()
            .Single(item => item.Property == stateProperty && Equals(item.Value, true));
        var setter = trigger.Setters
            .OfType<Setter>()
            .Single(item => item.Property == Border.BackgroundProperty);
        var resource = Assert.IsType<DynamicResourceExtension>(setter.Value);
        Assert.Equal(expectedResourceKey, resource.ResourceKey);
    }

    private static void AssertRotation(TriggerActionCollection actions, double targetAngle)
    {
        var begin = Assert.IsType<BeginStoryboard>(Assert.Single(actions));
        var animation = Assert.IsType<DoubleAnimation>(Assert.Single(begin.Storyboard.Children));
        Assert.Equal("ChevronRotation", Storyboard.GetTargetName(animation));
        Assert.Equal(targetAngle, animation.To);
        Assert.Equal(TimeSpan.FromMilliseconds(160), animation.Duration.TimeSpan);
    }

    private static void AssertContrastAtLeast(
        Color foreground,
        Color background,
        double minimum)
    {
        var lighter = Math.Max(Luminance(foreground), Luminance(background));
        var darker = Math.Min(Luminance(foreground), Luminance(background));
        var ratio = (lighter + 0.05) / (darker + 0.05);
        Assert.True(
            ratio >= minimum,
            $"Contrast {ratio:F2}:1 is below the required {minimum:F1}:1.");
    }

    private static double Luminance(Color color) =>
        0.2126 * Linear(color.R) +
        0.7152 * Linear(color.G) +
        0.0722 * Linear(color.B);

    private static double Linear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
