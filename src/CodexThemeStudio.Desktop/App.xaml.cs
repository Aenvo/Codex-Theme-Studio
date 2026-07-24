using System.Windows;
using CodexThemeStudio.CodexAdapter;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Desktop.Controls;

namespace CodexThemeStudio.Desktop;

public partial class App : Application
{
    private readonly Guid diagnosticSessionId = Guid.NewGuid();
    private LocalDiagnosticService? diagnostics;
    private string appVersion = "unknown";
    private bool terminatingAfterFailure;

    static App()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("windir")))
        {
            var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
            if (!string.IsNullOrWhiteSpace(systemRoot))
            {
                Environment.SetEnvironmentVariable("windir", systemRoot);
            }
        }
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
