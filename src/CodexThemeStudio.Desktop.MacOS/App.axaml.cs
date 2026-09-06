using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CodexThemeStudio.Desktop.MacOS.Services;
using CodexThemeStudio.Desktop.MacOS.ViewModels;
using CodexThemeStudio.Desktop.MacOS.Views;

namespace CodexThemeStudio.Desktop.MacOS;

public sealed partial class App : Avalonia.Application
{
    private SettingsWindow? settingsWindow;
    private AboutWindow? aboutWindow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel(
                FakeMacThemeStudioClient.CreateDefault());
            var mainWindow = new MainWindow
            {
                DataContext = viewModel,
            };
            desktop.MainWindow = mainWindow;
            _ = viewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void Settings_OnClick(object? sender, EventArgs args)
    {
        ShowSettingsWindow();
    }

    internal void ShowSettingsWindow()
    {
        if (settingsWindow is null)
        {
            settingsWindow = new SettingsWindow();
            settingsWindow.Closed += (_, _) => settingsWindow = null;
        }

        ShowOwnedOrActivate(settingsWindow);
    }

    private void About_OnClick(object? sender, EventArgs args)
    {
        if (aboutWindow is null)
        {
            aboutWindow = new AboutWindow();
            aboutWindow.Closed += (_, _) => aboutWindow = null;
        }

        ShowOwnedOrActivate(aboutWindow);
    }

    private static void ShowOwnedOrActivate(Window window)
    {
        if (window.IsVisible)
        {
            window.Activate();
            return;
        }

        if (Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }
    }
}
