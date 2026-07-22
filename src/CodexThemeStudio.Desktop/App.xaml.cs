using System.Windows;
using CodexThemeStudio.Desktop.Services;

namespace CodexThemeStudio.Desktop;

public partial class App : Application
{
    private SystemThemeService? systemTheme;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        systemTheme = new SystemThemeService(this);
        systemTheme.Start();

        try
        {
            var services = await AppServices.CreateAsync(CancellationToken.None);
            var window = new MainWindow(services.MainWindowViewModel);
            MainWindow = window;
            window.Show();
            await services.MainWindowViewModel.InitializeAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Codex Theme Studio 无法完成启动。\n\n{exception.Message}\n\n" +
                "如果数据位于外置磁盘，请重新连接原磁盘后重试；应用不会创建替代空库。",
                "启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        systemTheme?.Dispose();
        base.OnExit(e);
    }
}
