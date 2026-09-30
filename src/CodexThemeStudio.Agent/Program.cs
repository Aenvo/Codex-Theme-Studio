using System.Text.Json;
using CodexThemeStudio.CodexAdapter;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

const string ServiceName = "CodexThemeStudio.Agent";
const string ServiceVersion = "0.2.0";
const int ProtocolVersion = 1;

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

if (args is [
        "packaged-renderer-probe",
        "--aumid", var applicationUserModelId,
        "--executable", var expectedExecutablePath,
        "--node", var nodeExecutablePath,
        "--injector", var injectorScriptPath,
        "--profile-dir", var managedUserDataPath,
        "--wait-seconds", var waitSecondsText,
        "--result-file", var resultFilePath] &&
    int.TryParse(waitSecondsText, out var waitSeconds) &&
    waitSeconds is >= 0 and <= 600 &&
    Path.IsPathFullyQualified(resultFilePath))
{
    await WriteProbeResultAsync(resultFilePath, new
    {
        state = "waiting",
        startedAtUtc = DateTimeOffset.UtcNow,
    });
    PackagedRendererProbeProgress? lastProgress = null;
    var runner = new PackagedRendererProbeRunner(
        nodeExecutablePath,
        injectorScriptPath);
    var result = await runner.RunAsync(
        applicationUserModelId,
        expectedExecutablePath,
        managedUserDataPath,
        TimeSpan.FromSeconds(waitSeconds),
        async progress =>
        {
            lastProgress = progress;
            await WriteProbeResultAsync(resultFilePath, new
            {
                state = "running",
                updatedAtUtc = DateTimeOffset.UtcNow,
                progress,
            });
        },
        CancellationToken.None);
    if (!result.IsSuccess)
    {
        await WriteProbeResultAsync(resultFilePath, new
        {
            state = "failed",
            completedAtUtc = DateTimeOffset.UtcNow,
            error = new
            {
                code = result.Error!.Code.ToString(),
                userMessage = result.Error.UserMessage,
                diagnosticCode = result.Error.DiagnosticCode,
            },
            progress = lastProgress,
            managedProfilePopulated = Directory.Exists(managedUserDataPath) &&
                Directory.EnumerateFileSystemEntries(managedUserDataPath).Any(),
        });
        WriteFailure(result.Error!);
        return 2;
    }

    await WriteProbeResultAsync(resultFilePath, new
    {
        state = "succeeded",
        completedAtUtc = DateTimeOffset.UtcNow,
        portProbe = result.Value,
    });
    WriteSuccess(new { portProbe = result.Value });
    return 0;
}

if (args is ["package-debug-disable", "--package", var recoveryPackageFullName] &&
    !string.IsNullOrWhiteSpace(recoveryPackageFullName))
{
    if (!PackageDebugSettingsController.Disable(recoveryPackageFullName))
    {
        WriteFailure(new OperationError(
            OperationErrorCode.ExternalToolFailure,
            "无法恢复 ChatGPT 包调试状态。",
            "package_debug.disable_failed"));
        return 2;
    }

    WriteSuccess(new { packageDebugDisabled = true });
    return 0;
}

if (args is ["launch-themed", "--config", var launchConfigurationPath] &&
    Path.IsPathFullyQualified(launchConfigurationPath))
{
    var launchResult = await LaunchThemedCodexAsync(
        launchConfigurationPath,
        CancellationToken.None);
    if (!launchResult.IsSuccess)
    {
        WriteFailure(launchResult.Error!);
        return 2;
    }

    WriteSuccess(new
    {
        launch = launchResult.Value!.Launch,
        runtime = launchResult.Value.Runtime,
    });
    return 0;
}

if (args is ["self-test"])
{
    WriteSuccess(new
    {
        capabilities = new[]
        {
            "run",
            "once",
            "signal-stop",
            "packaged-renderer-probe",
            "packaged-renderer-probe-v2",
            "launch-themed",
            "package-debug-disable",
        },
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

    var launchConfiguration = configuration.Value!;
    var policy = PersistenceAgentConfigurationPolicy.Validate(
        launchConfiguration,
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
        new PersistenceAgentStateStore(launchConfiguration.StateFilePath));
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

async Task<OperationResult<ThemedLaunchResult>> LaunchThemedCodexAsync(
    string configurationPath,
    CancellationToken cancellationToken)
{
    var configurationStore = new PersistenceAgentConfigurationStore(
        configurationPath);
    var configuration = await configurationStore.ReadAsync(cancellationToken);
    if (!configuration.IsSuccess)
    {
        return OperationResult<ThemedLaunchResult>.Failure(configuration.Error!);
    }

    var launchConfiguration = configuration.Value!;
    var policy = PersistenceAgentConfigurationPolicy.Validate(
        launchConfiguration,
        configurationPath);
    if (!policy.IsSuccess)
    {
        return OperationResult<ThemedLaunchResult>.Failure(policy.Error!);
    }

    if (!launchConfiguration.Enabled || launchConfiguration.Suspended)
    {
        return OperationResult<ThemedLaunchResult>.Failure(
            OperationErrorCode.ValidationFailed,
            "持久主题当前未启用。",
            "themed_launch.persistence_disabled");
    }

    var client = new InjectorCommandClient(
        launchConfiguration.NodeExecutablePath,
        launchConfiguration.InjectorScriptPath,
        targetSelection: new CodexTargetSelectionService());
    var discovery = await client.DiscoverAsync(cancellationToken);
    if (!discovery.IsSuccess)
    {
        return OperationResult<ThemedLaunchResult>.Failure(discovery.Error!);
    }

    var installation = discovery.Value!.Installation;
    if (!ProcessIdentityPolicy.IsOfficialInstallation(installation))
    {
        return OperationResult<ThemedLaunchResult>.Failure(
            OperationErrorCode.ValidationFailed,
            "受管主题启动仅支持当前用户注册的官方 Microsoft Store ChatGPT。",
            "themed_launch.official_store_required");
    }

    PackagedRendererPortProbeResult? launched = null;
    var processes = discovery.Value.Processes;
    if (processes.Count > 1)
    {
        return OperationResult<ThemedLaunchResult>.Failure(
            OperationErrorCode.Conflict,
            "检测到多个 ChatGPT 主进程，未执行受管主题启动。",
            "themed_launch.multiple_processes");
    }

    if (processes.Count == 1 && processes[0].RendererPort is null)
    {
        return OperationResult<ThemedLaunchResult>.Failure(
            OperationErrorCode.Conflict,
            "当前 ChatGPT 不是由主题启动器打开。请先正常关闭，再运行主题启动器。",
            "themed_launch.unmanaged_process_running");
    }

    if (processes.Count == 0)
    {
        var launch = await new PackagedRendererProbeRunner(
                launchConfiguration.NodeExecutablePath,
                launchConfiguration.InjectorScriptPath)
            .RunAsync(
                $"{ProcessIdentityPolicy.OfficialPackageFamilyName}!App",
                installation.ExecutablePath,
                managedUserDataPath: null,
                waitForProcessExit: TimeSpan.Zero,
                reportProgress: null,
                cancellationToken: cancellationToken,
                terminateLaunchedProcessOnFailure: false)
            .ConfigureAwait(false);
        if (!launch.IsSuccess)
        {
            return OperationResult<ThemedLaunchResult>.Failure(launch.Error!);
        }

        launched = launch.Value;
    }

    var engine = new PersistenceAgentEngine(
        new PersistenceSnapshotStore(),
        client,
        client,
        client,
        new PersistenceAgentStateStore(launchConfiguration.StateFilePath));
    var runtime = await engine.RunCycleAsync(
        launchConfiguration,
        cancellationToken);
    if (!runtime.IsSuccess)
    {
        return OperationResult<ThemedLaunchResult>.Failure(runtime.Error!);
    }

    if (runtime.Value!.State != ThemeRuntimeState.Persistent)
    {
        return OperationResult<ThemedLaunchResult>.Failure(
            OperationErrorCode.ValidationFailed,
            runtime.Value.UserMessage,
            "themed_launch.runtime_not_persistent");
    }

    return OperationResult<ThemedLaunchResult>.Success(
        new ThemedLaunchResult(launched, runtime.Value!));
}

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

async Task WriteProbeResultAsync(string path, object value)
{
    var fullPath = Path.GetFullPath(path);
    var directory = Path.GetDirectoryName(fullPath)!;
    Directory.CreateDirectory(directory);
    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
    {
        throw new InvalidOperationException("Probe result directory cannot be a reparse point.");
    }

    var temporaryPath = Path.Combine(
        directory,
        $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
    try
    {
        await File.WriteAllTextAsync(
            temporaryPath,
            JsonSerializer.Serialize(value, jsonOptions),
            new System.Text.UTF8Encoding(false));
        if (File.Exists(fullPath))
        {
            File.Replace(temporaryPath, fullPath, null);
        }
        else
        {
            File.Move(temporaryPath, fullPath);
        }
    }
    finally
    {
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }
    }
}

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

internal sealed record ThemedLaunchResult(
    PackagedRendererPortProbeResult? Launch,
    ThemeRuntimeStatus Runtime);
