using System.IO;
using System.Windows;
using CodexThemeStudio.CodexAdapter;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Desktop.Controls;
using CodexThemeStudio.Desktop.Services;
using CodexThemeStudio.Update;

namespace CodexThemeStudio.Desktop;

public partial class App : Application
{
    private readonly Guid diagnosticSessionId = Guid.NewGuid();
    private LocalDiagnosticService? diagnostics;
    private IColorHistoryService? colorHistory;
    private string appVersion = "unknown";
    private bool terminatingAfterFailure;

    static App()
    {
        WindowsEnvironmentBootstrap.EnsureWindowsDirectoryEnvironmentVariable();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        diagnostics = new LocalDiagnosticService(
            LocalDiagnosticService.GetDefaultLogDirectory());
        appVersion = DiagnosticEventFactory.GetApplicationVersion(
            typeof(App).Assembly);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _ = await diagnostics.WriteAsync(
            CreateLifecycleEvent(
                DiagnosticLevel.Information,
                "desktop.started",
                DiagnosticOutcome.Started),
            CancellationToken.None);

        try
        {
            var serviceCorrelationId = Guid.NewGuid();
            _ = await diagnostics.WriteAsync(
                CreateLifecycleEvent(
                    DiagnosticLevel.Information,
                    "desktop.services.started",
                    DiagnosticOutcome.Started,
                    operation: "desktop.services",
                    correlationId: serviceCorrelationId),
                CancellationToken.None);
            var services = await AppServices.CreateAsync(
                CancellationToken.None,
                diagnostics,
                diagnosticSessionId,
                appVersion);
            colorHistory = services.ColorHistory;
            _ = await diagnostics.WriteAsync(
                CreateLifecycleEvent(
                    DiagnosticLevel.Information,
                    "desktop.services.completed",
                    DiagnosticOutcome.Succeeded,
                    operation: "desktop.services",
                    correlationId: serviceCorrelationId),
                CancellationToken.None);
            var window = new MainWindow(services.MainWindowViewModel);
            MainWindow = window;
            window.Show();
            _ = await diagnostics.WriteAsync(
                CreateLifecycleEvent(
                    DiagnosticLevel.Information,
                    "desktop.window.shown",
                    DiagnosticOutcome.State,
                    operation: "desktop.window"),
                CancellationToken.None);
            await services.MainWindowViewModel.InitializeAsync();
            _ = await diagnostics.WriteAsync(
                CreateLifecycleEvent(
                    DiagnosticLevel.Information,
                    "desktop.ready",
                    DiagnosticOutcome.Succeeded),
                CancellationToken.None);
            var updateToken = UpdateStartupCoordinator.GetToken(e.Args);
            if (updateToken is not null)
            {
                try
                {
                    await services.MainWindowViewModel
                        .WaitForBackgroundInitializationAsync();
                    var updateResult = await new UpdateStartupCoordinator()
                        .MarkHealthyAndWaitForResultAsync(
                            updateToken,
                            CancellationToken.None);
                    if (updateResult is not null)
                    {
                        await services.MainWindowViewModel
                            .HandleUpdateInstallResultAsync(updateResult);
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or
                    System.Text.Json.JsonException)
                {
                    services.MainWindowViewModel.ReportUpdateResultReadFailure();
                }
            }
        }
        catch (Exception exception)
        {
            terminatingAfterFailure = true;
            _ = diagnostics.WriteCritical(
                CreateLifecycleEvent(
                    DiagnosticLevel.Error,
                    "desktop.startup.failed",
                    DiagnosticOutcome.Failed,
                    exception));
            var dialog = AppDialogWindow.CreateInformation(
                "启动失败",
                $"Codex Theme Studio 无法完成启动。\n\n{exception.Message}\n\n" +
                "如果数据位于外置磁盘，请重新连接原磁盘后重试；应用不会创建替代空库。",
                owner: null,
                isError: true);
            dialog.ShowDialog();
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Task.Run(async () =>
            {
                if (colorHistory is not null)
                {
                    await colorHistory.FlushAsync(CancellationToken.None);
                }
            }).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
        }

        if (diagnostics is not null && !terminatingAfterFailure)
        {
            _ = diagnostics.WriteCritical(
                CreateLifecycleEvent(
                    DiagnosticLevel.Information,
                    "desktop.stopped",
                    DiagnosticOutcome.Succeeded));
        }

        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(
        object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) =>
        RecordUnhandled(e.Exception);

    private void OnUnhandledException(
        object? sender,
        UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            RecordUnhandled(exception);
        }
    }

    private void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e) =>
        RecordUnhandled(e.Exception);

    private void RecordUnhandled(Exception exception)
    {
        terminatingAfterFailure = true;
        if (diagnostics is null)
        {
            return;
        }

        _ = diagnostics.WriteCritical(
            CreateLifecycleEvent(
                DiagnosticLevel.Error,
                "desktop.unhandled_exception",
                DiagnosticOutcome.Failed,
                exception));
    }

    private DiagnosticEvent CreateLifecycleEvent(
        DiagnosticLevel level,
        string eventName,
        DiagnosticOutcome outcome,
        Exception? exception = null,
        string operation = "desktop.lifecycle",
        Guid? correlationId = null) =>
        DiagnosticEventFactory.Create(
            DiagnosticSource.Desktop,
            level,
            eventName,
            outcome,
            diagnosticSessionId,
            appVersion,
            operation: operation,
            correlationId: correlationId,
            exception: exception);
}
