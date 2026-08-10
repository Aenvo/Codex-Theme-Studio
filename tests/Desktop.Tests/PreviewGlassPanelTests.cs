using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexThemeStudio.Desktop.Controls;

namespace CodexThemeStudio.Desktop.Tests;

public sealed class PreviewGlassPanelTests
{
    [Fact]
    public void Panel_UsesAlignedBackdropAndScalesBlurFromReferenceViewport()
    {
        WpfTestHost.Invoke(() =>
        {
            var source = new Grid
            {
                Width = 620,
                Height = 390,
                Background = Brushes.CornflowerBlue,
            };
            var content = new TextBlock
            {
                Text = "preview",
            };
            var panel = new PreviewGlassPanel
            {
                Width = 200,
                Height = 100,
                Margin = new Thickness(70, 45, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                BackdropSource = source,
                Background = new SolidColorBrush(Color.FromArgb(0xE8, 0x24, 0x1B, 0x30)),
                BlurRadius = 32,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                PanelContent = content,
                SurfaceOpacity = 0.9104,
            };
            var root = new Grid
            {
                Width = 620,
                Height = 390,
            };
            root.Children.Add(source);
            root.Children.Add(panel);

            var window = new Window
            {
                Content = root,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
            };
            try
            {
                window.Show();
                root.UpdateLayout();

                Assert.Equal(16, panel.EffectiveBlurRadius, precision: 3);
                Assert.Equal(new Rect(70, 45, 200, 100), panel.CurrentSampleRect);
                Assert.Equal(1, panel.Opacity);
                Assert.Equal(1, content.Opacity);
                Assert.Equal(0.9104, panel.SurfaceOpacity, precision: 4);
                Assert.Equal(
                    0xE8,
                    Assert.IsType<SolidColorBrush>(panel.Background).Color.A);
                Assert.Equal(new Rect(0, 0, 200, 100), panel.CurrentLayerBounds);
                Assert.IsType<ContentPresenter>(VisualTreeHelper.GetChild(panel, 0));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Panel_RecomputesSampleWhenLayoutChanges()
    {
        WpfTestHost.Invoke(() =>
        {
            var source = new Grid
            {
                Width = 1240,
                Height = 780,
                Background = Brushes.Black,
            };
            var panel = new PreviewGlassPanel
            {
                Width = 160,
                Height = 80,
                Margin = new Thickness(20, 30, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                BackdropSource = source,
                BlurRadius = 10,
            };
            var root = new Grid
            {
                Width = 1240,
                Height = 780,
            };
            root.Children.Add(source);
            root.Children.Add(panel);

            var window = new Window
            {
                Content = root,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
            };
            try
            {
                window.Show();
                root.UpdateLayout();
                Assert.Equal(new Rect(20, 30, 160, 80), panel.CurrentSampleRect);
                Assert.Equal(10, panel.EffectiveBlurRadius, precision: 3);

                panel.Margin = new Thickness(90, 120, 0, 0);
                root.UpdateLayout();

                Assert.Equal(new Rect(90, 120, 160, 80), panel.CurrentSampleRect);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
