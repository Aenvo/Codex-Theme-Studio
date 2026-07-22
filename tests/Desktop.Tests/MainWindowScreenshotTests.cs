using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexThemeStudio.Desktop.ViewModels;

namespace CodexThemeStudio.Desktop.Tests;

public sealed class MainWindowScreenshotTests
{
    [Fact]
    public void MainWindow_RendersAtMinimumAndStandardSizes()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent();
                using var fixture = new ViewModelFixture(themeCount: 100);
                fixture.Runtime.Status = new(
                    Contracts.Models.ThemeRuntimeState.Temporary,
                    fixture.Repository.Summaries[0].ThemeId,
                    false,
                    1234,
                    DateTimeOffset.UtcNow,
                    "已通过运行时标记确认主题状态。",
                    fixture.Repository.Summaries[0].ThemeId,
                    Evidence: Contracts.Models.ThemeRuntimeEvidence.RuntimeMarkers);
                fixture.ViewModel.InitializeAsync().GetAwaiter().GetResult();

                RenderWindow(fixture.ViewModel, 960, 620, 120, screenshotPath: null);
                RenderWindow(fixture.ViewModel, 960, 620, 144, screenshotPath: null);
                RenderWindow(fixture.ViewModel, 960, 620, 192, screenshotPath: null);
                RenderWindow(
                    fixture.ViewModel,
                    1240,
                    780,
                    96,
                    Environment.GetEnvironmentVariable("CTS_SCREENSHOT_PATH"));
                fixture.Editor.Begin(
                    fixture.Repository.Themes[fixture.Repository.Summaries[0].ThemeId],
                    newTheme: false);
                fixture.ViewModel.NavigateCommand.Execute("Editor");
                RenderWindow(
                    fixture.ViewModel,
                    1240,
                    780,
                    96,
                    Environment.GetEnvironmentVariable("CTS_EDITOR_SCREENSHOT_PATH"));
                fixture.ViewModel.NavigateCommand.Execute("Settings");
                RenderWindow(
                    fixture.ViewModel,
                    1240,
                    780,
                    96,
                    Environment.GetEnvironmentVariable("CTS_MIGRATION_SCREENSHOT_PATH"));
                app.Shutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "WPF screenshot thread timed out.");
        Assert.Null(failure);
    }

    private static void RenderWindow(
        MainWindowViewModel viewModel,
        int width,
        int height,
        double dpi,
        string? screenshotPath)
    {
        var window = new MainWindow(viewModel)
        {
            Width = width,
            Height = height,
        };
        var root = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
        root.DataContext = viewModel;
        if (root is Panel panel)
        {
            panel.Background = Assert.IsAssignableFrom<Brush>(
                Application.Current.Resources["WindowBrush"]);
        }
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();

        var pixelWidth = (int)Math.Round(width * dpi / 96);
        var pixelHeight = (int)Math.Round(height * dpi / 96);
        var bitmap = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            dpi,
            dpi,
            PixelFormats.Pbgra32);
        bitmap.Render(root);
        Assert.Equal(pixelWidth, bitmap.PixelWidth);
        Assert.Equal(pixelHeight, bitmap.PixelHeight);

        if (!string.IsNullOrWhiteSpace(screenshotPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath)!);
            using var stream = File.Create(screenshotPath);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
        }

    }
}
