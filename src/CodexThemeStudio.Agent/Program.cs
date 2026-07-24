using System.Text.Json;
using CodexThemeStudio.CodexAdapter;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

const string ServiceName = "CodexThemeStudio.Agent";
const string ServiceVersion = "0.2.0";
const int ProtocolVersion = 1;

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

if (args is ["self-test"])
{
    WriteSuccess(new
    {
        capabilities = new[] { "run", "once", "signal-stop" },
        mutexName = PersistenceAgentRunner.MutexName,
    });
    return 0;
}

if (args is ["signal-stop"])
{
    WriteSuccess(new { signaled = PersistenceAgentRunner.SignalStop() });
    return 0;
}

if (args is [var command, "--config", var configurationPath] &&
    command is "run" or "once" &&
    Path.IsPathFullyQualified(configurationPath))
{
    if (command == "run")
    {
        ConsoleWindow.Hide();
        try
        {
            return await new PersistenceAgentRunner(configurationPath, ServiceVersion)
                .RunAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            var diagnostics = new LocalDiagnosticService(
                LocalDiagnosticService.GetDefaultLogDirectory());
            _ = diagnostics.WriteCritical(
                DiagnosticEventFactory.Create(
                    DiagnosticSource.Agent,
                    DiagnosticLevel.Error,
                    "agent.unhandled_exception",
                    DiagnosticOutcome.Failed,
                    Guid.NewGuid(),
                    ServiceVersion,
                    operation: "persistence.agent",
                    exception: exception));
            throw;
        }
    }

    var configurationStore =
        new PersistenceAgentConfigurationStore(configurationPath);
    var configuration = await configurationStore.ReadAsync(CancellationToken.None);
    if (!configuration.IsSuccess)
    {
        WriteFailure(configuration.Error!);
        return 2;
    }

    var policy = PersistenceAgentConfigurationPolicy.Validate(
        configuration.Value!,
        configurationPath);
    if (!policy.IsSuccess)
    {
        WriteFailure(policy.Error!);
        return 2;
    }

    var client = new InjectorCommandClient(
        configuration.Value!.NodeExecutablePath,
        configuration.Value.InjectorScriptPath,
        targetSelection: new CodexTargetSelectionService());
    var engine = new PersistenceAgentEngine(
        new PersistenceSnapshotStore(),
        client,
        client,
        client,
        new PersistenceAgentStateStore(configuration.Value.StateFilePath));
    var result = await engine.RunCycleAsync(
        configuration.Value,
        CancellationToken.None);
    if (!result.IsSuccess)
    {
        WriteFailure(result.Error!);
        return 2;
    }

    WriteSuccess(new
    {
        runtime = result.Value,
    });
    return 0;
}

WriteFailure(new OperationError(
    OperationErrorCode.ValidationFailed,
    "命令或参数无效。",
    "agent.arguments_invalid"));
return 2;

void WriteSuccess(object payload) =>
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        service = ServiceName,
        version = ServiceVersion,
        protocolVersion = ProtocolVersion,
        status = "ok",
        payload,
    }, jsonOptions));

void WriteFailure(OperationError error) =>
    Console.Error.WriteLine(JsonSerializer.Serialize(new
    {
        service = ServiceName,
        version = ServiceVersion,
        protocolVersion = ProtocolVersion,
        status = "error",
        error = new
        {
            code = error.Code.ToString(),
            userMessage = error.UserMessage,
            diagnosticCode = error.DiagnosticCode,
        },
    }, jsonOptions));

internal static class ConsoleWindow
{
    public static void Hide()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var window = GetConsoleWindow();
        if (window != IntPtr.Zero)
        {
            ShowWindow(window, 0);
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(
        System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);
}
