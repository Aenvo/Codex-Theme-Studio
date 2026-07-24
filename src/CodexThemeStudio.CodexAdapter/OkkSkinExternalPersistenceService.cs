using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;
using Microsoft.Win32;

namespace CodexThemeStudio.CodexAdapter;

internal interface IOkkSkinStartupStore
{
    Task<OperationResult<string?>> ReadAsync(CancellationToken cancellationToken);

    Task<OperationResult> RemoveAsync(
        string expectedCommand,
        CancellationToken cancellationToken);
}

internal interface IOkkSkinProcessController
{
    Task<OperationResult<IReadOnlyList<int>>> FindAsync(
        string nodePath,
        string agentPath,
        CancellationToken cancellationToken);

    Task<OperationResult> StopAsync(
        IReadOnlyList<int> processIds,
        CancellationToken cancellationToken);
}

public sealed class OkkSkinExternalPersistenceService : IExternalPersistenceService
{
    private const int MaximumStateBytes = 64 * 1024;
    private readonly string root;
    private readonly IOkkSkinStartupStore startupStore;
    private readonly IOkkSkinProcessController processController;
    private readonly SemaphoreSlim writeLock = new(1, 1);

    public OkkSkinExternalPersistenceService(string? root = null)
        : this(
            root,
            new WindowsOkkSkinStartupStore(),
            new WindowsOkkSkinProcessController())
    {
    }

    internal OkkSkinExternalPersistenceService(
        string? root,
        IOkkSkinStartupStore startupStore,
        IOkkSkinProcessController processController)
    {
        this.root = Path.GetFullPath(root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "okkskin"));
        this.startupStore = startupStore ??
            throw new ArgumentNullException(nameof(startupStore));
        this.processController = processController ??
            throw new ArgumentNullException(nameof(processController));
    }

    public async Task<OperationResult<ExternalPersistenceStatus>> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        var inspected = await InspectAsync(cancellationToken);
        return inspected.IsSuccess
            ? OperationResult<ExternalPersistenceStatus>.Success(
                inspected.Value!.Status)
            : OperationResult<ExternalPersistenceStatus>.Failure(
                inspected.Error!);
    }

    public async Task<OperationResult<ExternalPersistenceDisableResult>> DisableAsync(
        CancellationToken cancellationToken)
    {
        if (!await writeLock.WaitAsync(0, cancellationToken))
        {
            return Failure(
                OperationErrorCode.Conflict,
                "OkkSkin 持久化状态正在被另一个操作修改。",
                "external.persistence.busy");
        }

        try
        {
            var inspected = await InspectAsync(cancellationToken);
            if (!inspected.IsSuccess)
            {
                return OperationResult<ExternalPersistenceDisableResult>.Failure(
                    inspected.Error!);
            }

            var current = inspected.Value!;
            if (!current.Status.RequiresAction)
            {
                return Success(
                    ExternalPersistenceDisableOutcome.Success,
                    current.Status,
                    "OkkSkin 持久化已经停用。");
            }

            if (!current.Status.CanDisableSafely)
            {
                return Failure(
                    OperationErrorCode.Conflict,
                    "OkkSkin 持久化身份无法安全确认；未修改状态、启动项或进程。",
                    "external.persistence.identity_unverified");
            }

            var residuals = new List<string>();
            var stateDisabled = !current.Status.IsConfigured;
            if (current.State is not null && current.Status.IsConfigured)
            {
                var write = await WriteDisabledStateAsync(
                    current.State,
                    cancellationToken);
                if (!write.IsSuccess)
                {
                    return OperationResult<ExternalPersistenceDisableResult>.Failure(
                        write.Error!);
                }

                stateDisabled = true;
            }

            var startupRemoved = !current.Status.IsStartupRegistered;
            if (current.RunCommand is not null)
            {
                var remove = await startupStore.RemoveAsync(
                    current.RunCommand,
                    cancellationToken);
                if (remove.IsSuccess)
                {
                    startupRemoved = true;
                }
                else
                {
                    residuals.Add(remove.Error!.UserMessage);
                }
            }

            var agentStopped = !current.Status.IsAgentRunning;
            if (current.AgentProcessIds.Count > 0)
            {
                var stop = await processController.StopAsync(
                    current.AgentProcessIds,
                    cancellationToken);
                if (stop.IsSuccess)
                {
                    agentStopped = true;
                }
                else
                {
                    residuals.Add(stop.Error!.UserMessage);
                }
            }

            var verified = await InspectAsync(cancellationToken);
            ExternalPersistenceStatus status;
            if (verified.IsSuccess)
            {
                status = verified.Value!.Status;
                residuals.AddRange(status.Residuals);
                if (status.IsConfigured)
                {
                    residuals.Add("OkkSkin 状态仍标记为启用。");
                }

                if (status.IsStartupRegistered)
                {
                    residuals.Add("OkkSkin 当前用户启动项仍存在。");
                }

                if (status.IsAgentRunning)
                {
                    residuals.Add("OkkSkin Agent 仍在运行。");
                }
            }
            else
            {
                residuals.Add(
                    $"无法复核 OkkSkin 停用状态：{verified.Error!.UserMessage}");
                status = current.Status with
                {
                    IsConfigured = !stateDisabled,
                    IsStartupRegistered = !startupRemoved,
                    IsAgentRunning = !agentStopped,
                    CanDisableSafely = false,
                };
            }

            residuals = residuals
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var isComplete =
                stateDisabled &&
                startupRemoved &&
                agentStopped &&
                residuals.Count == 0 &&
                !status.RequiresAction;
            var finalStatus = status with
            {
                Residuals = residuals,
                UserMessage = isComplete
                    ? "OkkSkin 持久化已停用；主题和缓存保持不变。"
                    : "OkkSkin 已执行停用，但仍存在需要处理的残留。",
            };

            return Success(
                isComplete
                    ? ExternalPersistenceDisableOutcome.Success
                    : ExternalPersistenceDisableOutcome.Partial,
                finalStatus,
                finalStatus.UserMessage);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private async Task<OperationResult<OkkSkinInspection>> InspectAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            JsonObject? state = null;
            var configured = false;
            if (Directory.Exists(root))
            {
                if (IsReparsePoint(root))
                {
                    return InspectionFailure(
                        OperationErrorCode.InvalidPath,
                        "OkkSkin 数据目录不能是符号链接或 Junction。",
                        "external.persistence.root_reparse");
                }

                var statePath = Path.Combine(root, "state.json");
                if (File.Exists(statePath))
                {
                    if (IsReparsePoint(statePath))
                    {
                        return InspectionFailure(
                            OperationErrorCode.InvalidPath,
                            "OkkSkin 状态文件不能是符号链接或 Junction。",
                            "external.persistence.state_reparse");
                    }

                    var info = new FileInfo(statePath);
                    if (info.Length is <= 0 or > MaximumStateBytes)
                    {
                        return InspectionFailure(
                            OperationErrorCode.InvalidResponse,
                            "OkkSkin 状态文件大小无效。",
                            "external.persistence.state_size_invalid");
                    }

                    state = JsonNode.Parse(
                        await File.ReadAllBytesAsync(
                            statePath,
                            cancellationToken)) as JsonObject;
                    if (state is null ||
                        state["skinId"] is not JsonValue skinIdValue ||
                        !skinIdValue.TryGetValue<string>(out var skinId) ||
                        string.IsNullOrWhiteSpace(skinId) ||
                        state["enabled"] is not JsonValue enabledValue ||
                        !enabledValue.TryGetValue<bool>(out configured))
                    {
                        return InspectionFailure(
                            OperationErrorCode.InvalidResponse,
                            "OkkSkin 状态文件格式无效。",
                            "external.persistence.state_invalid");
                    }
                }
            }

            var startup = await startupStore.ReadAsync(cancellationToken);
            if (!startup.IsSuccess)
            {
                return OperationResult<OkkSkinInspection>.Failure(
                    startup.Error!);
            }

            var runCommand = startup.Value;
            var residuals = new List<string>();
            OkkSkinLauncherInfo? launcher = null;
            var launcherPath = Path.Combine(root, "OkkSkin.vbs");
            if (File.Exists(launcherPath))
            {
                launcher = TryReadLauncher(launcherPath);
                if (launcher is null && (configured || runCommand is not null))
                {
                    residuals.Add("OkkSkin Agent 启动器无法安全识别。");
                }
            }
            else if (configured || runCommand is not null)
            {
                residuals.Add("OkkSkin Agent 启动器不存在。");
            }

            if (runCommand is not null &&
                !IsExpectedRunCommand(runCommand, launcherPath))
            {
                residuals.Add("OkkSkin 启动项目标与已验证启动器不一致。");
            }

            IReadOnlyList<int> agentProcessIds = [];
            if (launcher is not null)
            {
                var processes = await processController.FindAsync(
                    launcher.NodePath,
                    launcher.AgentPath,
                    cancellationToken);
                if (processes.IsSuccess)
                {
                    agentProcessIds = processes.Value!;
                }
                else if (configured || runCommand is not null)
                {
                    residuals.Add(processes.Error!.UserMessage);
                }
            }

            var status = new ExternalPersistenceStatus(
                "OkkSkin",
                configured,
                runCommand is not null,
                agentProcessIds.Count > 0,
                residuals.Count == 0,
                residuals,
                configured || runCommand is not null || agentProcessIds.Count > 0
                    ? "检测到 OkkSkin 持久化。"
                    : "OkkSkin 持久化未启用。");
            return OperationResult<OkkSkinInspection>.Success(
                new OkkSkinInspection(
                    state,
                    runCommand,
                    launcher,
                    agentProcessIds,
                    status));
        }
        catch (OperationCanceledException)
        {
            return InspectionFailure(
                OperationErrorCode.Cancelled,
                "OkkSkin 持久化状态读取已取消。",
                "external.persistence.cancelled");
        }
        catch (UnauthorizedAccessException)
        {
            return InspectionFailure(
                OperationErrorCode.AccessDenied,
                "没有权限读取 OkkSkin 持久化状态。",
                "external.persistence.read_access_denied");
        }
        catch (Exception exception)
            when (exception is IOException or JsonException or InvalidOperationException)
        {
            return InspectionFailure(
                OperationErrorCode.InvalidResponse,
                "无法安全读取 OkkSkin 持久化状态。",
                "external.persistence.read_failed");
        }
    }

    private async Task<OperationResult> WriteDisabledStateAsync(
        JsonObject state,
        CancellationToken cancellationToken)
    {
        var statePath = Path.Combine(root, "state.json");
        var temporaryPath = Path.Combine(
            root,
            $".state.{Guid.NewGuid():N}.tmp");
        try
        {
            state["enabled"] = false;
            state["appliedPid"] = null;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                state,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                });
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Replace(temporaryPath, statePath, null);
            return OperationResult.Success();
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Failure(
                OperationErrorCode.Cancelled,
                "OkkSkin 持久化停用已取消。",
                "external.persistence.write_cancelled");
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResult.Failure(
                OperationErrorCode.AccessDenied,
                "没有权限原子更新 OkkSkin 状态。",
                "external.persistence.write_access_denied");
        }
        catch (IOException)
        {
            return OperationResult.Failure(
                OperationErrorCode.StorageUnavailable,
                "无法原子更新 OkkSkin 状态。",
                "external.persistence.write_failed");
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private OkkSkinLauncherInfo? TryReadLauncher(string launcherPath)
    {
        if (!Path.GetFullPath(launcherPath).StartsWith(
                root.TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) ||
            IsReparsePoint(launcherPath))
        {
            return null;
        }

        var info = new FileInfo(launcherPath);
        if (info.Length is <= 0 or > 16 * 1024)
        {
            return null;
        }

        var match = LauncherPattern.Match(File.ReadAllText(
            launcherPath,
            Encoding.UTF8));
        if (!match.Success)
        {
            return null;
        }

        var nodePath = Path.GetFullPath(match.Groups["node"].Value);
        var agentPath = Path.GetFullPath(match.Groups["agent"].Value);
        if (!Path.IsPathFullyQualified(nodePath) ||
            !Path.IsPathFullyQualified(agentPath) ||
            !string.Equals(
                Path.GetFileName(nodePath),
                "node.exe",
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                Path.GetFileName(agentPath),
                "agent.mjs",
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(nodePath) ||
            !File.Exists(agentPath) ||
            IsReparsePoint(nodePath) ||
            IsReparsePoint(agentPath))
        {
            return null;
        }

        return new OkkSkinLauncherInfo(nodePath, agentPath);
    }

    private static bool IsExpectedRunCommand(
        string command,
        string launcherPath)
    {
        var match = RunPattern.Match(command.Trim());
        if (!match.Success)
        {
            return false;
        }

        var wscriptPath = Path.GetFullPath(match.Groups["wscript"].Value);
        var actualLauncherPath = Path.GetFullPath(
            match.Groups["launcher"].Value);
        return string.Equals(
                   wscriptPath,
                   Path.Combine(Environment.SystemDirectory, "wscript.exe"),
                   StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   actualLauncherPath,
                   Path.GetFullPath(launcherPath),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static OperationResult<OkkSkinInspection> InspectionFailure(
        OperationErrorCode code,
        string message,
        string diagnosticCode) =>
        OperationResult<OkkSkinInspection>.Failure(
            code,
            message,
            diagnosticCode);

    private static OperationResult<ExternalPersistenceDisableResult> Failure(
        OperationErrorCode code,
        string message,
        string diagnosticCode) =>
        OperationResult<ExternalPersistenceDisableResult>.Failure(
            code,
            message,
            diagnosticCode);

    private static OperationResult<ExternalPersistenceDisableResult> Success(
        ExternalPersistenceDisableOutcome outcome,
        ExternalPersistenceStatus status,
        string message) =>
        OperationResult<ExternalPersistenceDisableResult>.Success(
            new ExternalPersistenceDisableResult(
                outcome,
                status,
                message));

    private sealed record OkkSkinLauncherInfo(
        string NodePath,
        string AgentPath);

    private sealed record OkkSkinInspection(
        JsonObject? State,
        string? RunCommand,
        OkkSkinLauncherInfo? Launcher,
        IReadOnlyList<int> AgentProcessIds,
        ExternalPersistenceStatus Status);

    private static readonly Regex LauncherPattern = new(
        """"^\s*CreateObject\("WScript\.Shell"\)\.Run\s+"""(?<node>[^"]+)""\s+""(?<agent>[^"]+)""",\s*0,\s*False\s*$"""",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex RunPattern = new(
        """^\s*"(?<wscript>[^"]+)"\s+"(?<launcher>[^"]+)"\s*$""",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
}

internal sealed class WindowsOkkSkinStartupStore : IOkkSkinStartupStore
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OkkSkin";

    public Task<OperationResult<string?>> ReadAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(
                OperationResult<string?>.Failure(
                    OperationErrorCode.UnsupportedVersion,
                    "OkkSkin 启动项管理仅支持 Windows。",
                    "external.persistence.windows_required"));
        }

        return ReadWindows();
    }

    [SupportedOSPlatform("windows")]
    private static Task<OperationResult<string?>> ReadWindows()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            var value = key?.GetValue(
                ValueName,
                null,
                RegistryValueOptions.DoNotExpandEnvironmentNames);
            return value is null
                ? Task.FromResult(
                    OperationResult<string?>.SuccessOptional(null))
                : value is string command
                    ? Task.FromResult(
                        OperationResult<string?>.Success(command))
                    : Task.FromResult(
                        OperationResult<string?>.Failure(
                            OperationErrorCode.InvalidResponse,
                            "OkkSkin 启动项类型无效。",
                            "external.persistence.startup_type_invalid"));
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(
                OperationResult<string?>.Failure(
                    OperationErrorCode.AccessDenied,
                    "没有权限读取 OkkSkin 启动项。",
                    "external.persistence.startup_read_access_denied"));
        }
    }

    public Task<OperationResult> RemoveAsync(
        string expectedCommand,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(
                OperationResult.Failure(
                    OperationErrorCode.UnsupportedVersion,
                    "OkkSkin 启动项管理仅支持 Windows。",
                    "external.persistence.windows_required"));
        }

        return RemoveWindows(expectedCommand);
    }

    [SupportedOSPlatform("windows")]
    private static Task<OperationResult> RemoveWindows(
        string expectedCommand)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                RunKeyPath,
                writable: true);
            var current = key?.GetValue(
                ValueName,
                null,
                RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            if (current is null)
            {
                return Task.FromResult(OperationResult.Success());
            }

            if (!string.Equals(
                    current,
                    expectedCommand,
                    StringComparison.Ordinal))
            {
                return Task.FromResult(
                    OperationResult.Failure(
                        OperationErrorCode.Conflict,
                        "OkkSkin 启动项在操作期间发生变化；未删除。",
                        "external.persistence.startup_changed"));
            }

            key!.DeleteValue(ValueName, throwOnMissingValue: false);
            return Task.FromResult(OperationResult.Success());
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(
                OperationResult.Failure(
                    OperationErrorCode.AccessDenied,
                    "没有权限移除 OkkSkin 启动项。",
                    "external.persistence.startup_remove_access_denied"));
        }
        catch (IOException)
        {
            return Task.FromResult(
                OperationResult.Failure(
                    OperationErrorCode.StorageUnavailable,
                    "无法移除 OkkSkin 启动项。",
                    "external.persistence.startup_remove_failed"));
        }
    }
}

internal sealed class WindowsOkkSkinProcessController : IOkkSkinProcessController
{
    public Task<OperationResult<IReadOnlyList<int>>> FindAsync(
        string nodePath,
        string agentPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(
                OperationResult<IReadOnlyList<int>>.Failure(
                    OperationErrorCode.UnsupportedVersion,
                    "OkkSkin Agent 检测仅支持 Windows。",
                    "external.persistence.process_windows_required"));
        }

        var matches = new List<int>();
        foreach (var process in Process.GetProcessesByName(
                     Path.GetFileNameWithoutExtension(nodePath)))
        {
            using (process)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!string.Equals(
                            process.MainModule?.FileName,
                            nodePath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var commandLine = ReadCommandLine(process);
                    if (commandLine is null)
                    {
                        return Task.FromResult(
                            OperationResult<IReadOnlyList<int>>.Failure(
                                OperationErrorCode.AccessDenied,
                                "无法安全读取候选 OkkSkin Agent 命令行。",
                                "external.persistence.agent_command_line_unavailable"));
                    }

                    var arguments = ParseCommandLine(commandLine);
                    if (arguments.Count == 2 &&
                        string.Equals(
                            Path.GetFullPath(arguments[0]),
                            nodePath,
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            Path.GetFullPath(arguments[1]),
                            agentPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(process.Id);
                    }
                }
                catch (Exception exception)
                    when (exception is
                          System.ComponentModel.Win32Exception or
                          InvalidOperationException or
                          ArgumentException)
                {
                    return Task.FromResult(
                        OperationResult<IReadOnlyList<int>>.Failure(
                            OperationErrorCode.AccessDenied,
                            "无法安全核对候选 OkkSkin Agent 进程。",
                            "external.persistence.agent_inspection_failed"));
                }
            }
        }

        return Task.FromResult(
            OperationResult<IReadOnlyList<int>>.Success(matches));
    }

    public async Task<OperationResult> StopAsync(
        IReadOnlyList<int> processIds,
        CancellationToken cancellationToken)
    {
        foreach (var processId in processIds.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var process = Process.GetProcessById(processId);
                process.Kill(entireProcessTree: false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (ArgumentException)
            {
                // The exact process already exited.
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                return OperationResult.Failure(
                    OperationErrorCode.Timeout,
                    "OkkSkin Agent 未在限定时间内停止。",
                    "external.persistence.agent_stop_timeout");
            }
            catch (Exception exception)
                when (exception is
                      System.ComponentModel.Win32Exception or
                      InvalidOperationException)
            {
                return OperationResult.Failure(
                    OperationErrorCode.ExternalToolFailure,
                    "无法停止已验证的 OkkSkin Agent。",
                    "external.persistence.agent_stop_failed");
            }
        }

        return OperationResult.Success();
    }

    private static string? ReadCommandLine(Process process)
    {
        var status = NtQueryInformationProcess(
            process.Handle,
            ProcessCommandLineInformation,
            IntPtr.Zero,
            0,
            out var requiredLength);
        if (requiredLength <= 0 &&
            status != StatusInfoLengthMismatch)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal(requiredLength);
        try
        {
            status = NtQueryInformationProcess(
                process.Handle,
                ProcessCommandLineInformation,
                buffer,
                requiredLength,
                out _);
            if (status != 0)
            {
                return null;
            }

            var commandLine = Marshal.PtrToStructure<UnicodeString>(buffer);
            return commandLine.Buffer == IntPtr.Zero
                ? null
                : Marshal.PtrToStringUni(
                    commandLine.Buffer,
                    commandLine.Length / sizeof(char));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IReadOnlyList<string> ParseCommandLine(string commandLine)
    {
        var pointer = CommandLineToArgvW(commandLine, out var count);
        if (pointer == IntPtr.Zero || count <= 0)
        {
            return [];
        }

        try
        {
            var arguments = new string[count];
            for (var index = 0; index < count; index++)
            {
                arguments[index] = Marshal.PtrToStringUni(
                    Marshal.ReadIntPtr(pointer, index * IntPtr.Size))!;
            }

            return arguments;
        }
        finally
        {
            _ = LocalFree(pointer);
        }
    }

    private const int ProcessCommandLineInformation = 60;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct UnicodeString
    {
        public readonly ushort Length;
        public readonly ushort MaximumLength;
        public readonly IntPtr Buffer;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        IntPtr processInformation,
        int processInformationLength,
        out int returnLength);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(
        string commandLine,
        out int argumentCount);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
