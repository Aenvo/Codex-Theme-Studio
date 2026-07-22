using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;
using Microsoft.Win32;

namespace CodexThemeStudio.CodexAdapter;

public sealed record ManagedAgentInstallation(
    string VersionDirectory,
    string AgentExecutablePath,
    string NodeExecutablePath,
    string InjectorScriptPath,
    string ContentFingerprint);

public interface IManagedAgentInstaller
{
    Task<OperationResult<ManagedAgentInstallation>> InstallAsync(
        string sourceDirectory,
        string stableAgentRoot,
        CancellationToken cancellationToken);
}

public interface IAgentStartupManager
{
    Task<OperationResult> InstallAsync(
        string agentExecutablePath,
        string configurationPath,
        CancellationToken cancellationToken);

    Task<OperationResult> RemoveAsync(CancellationToken cancellationToken);

    Task<OperationResult<bool>> IsInstalledAsync(
        CancellationToken cancellationToken);
}

public interface IAgentProcessController
{
    Task<OperationResult> StartAsync(
        string agentExecutablePath,
        string configurationPath,
        CancellationToken cancellationToken);

    Task<OperationResult> SignalStopAsync(CancellationToken cancellationToken);
}

public sealed class ManagedAgentInstaller : IManagedAgentInstaller
{
    public async Task<OperationResult<ManagedAgentInstallation>> InstallAsync(
        string sourceDirectory,
        string stableAgentRoot,
        CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(sourceDirectory) ||
            !Path.IsPathFullyQualified(stableAgentRoot))
        {
            return Failure<ManagedAgentInstallation>(
                OperationErrorCode.InvalidPath,
                "Agent 源目录和稳定安装目录必须是绝对路径。",
                "persistence.install.path_invalid");
        }

        var source = Path.GetFullPath(sourceDirectory);
        var root = Path.GetFullPath(stableAgentRoot);
        var nestedAgentSource = Path.Combine(source, "agent");
        var agentBundleSource =
            File.Exists(Path.Combine(nestedAgentSource, "CodexThemeStudio.Agent.exe"))
                ? nestedAgentSource
                : source;
        var agentSource = Path.Combine(
            agentBundleSource,
            "CodexThemeStudio.Agent.exe");
        var nodeSource = Path.Combine(source, "runtime", "node", "node.exe");
        var injectorSource = Path.Combine(
            source,
            "runtime",
            "injector",
            "index.mjs");
        if (!File.Exists(agentSource) ||
            !File.Exists(nodeSource) ||
            !File.Exists(injectorSource))
        {
            return Failure<ManagedAgentInstallation>(
                OperationErrorCode.NotFound,
                "Agent 安装包不完整，缺少 Agent、Node Runtime 或 Injector。",
                "persistence.install.bundle_incomplete");
        }

        try
        {
            var files = Directory.EnumerateFiles(
                    agentBundleSource,
                    "*",
                    SearchOption.AllDirectories)
                .Select(path => new AgentInstallFile(
                    path,
                    Path.GetRelativePath(agentBundleSource, path)))
                .Concat(
                    string.Equals(
                        agentBundleSource,
                        source,
                        StringComparison.OrdinalIgnoreCase)
                        ? []
                        : EnumerateSharedRuntimeFiles(source))
                .GroupBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Single())
                .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
                .ToArray();
            using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = file.RelativePath.Replace('\\', '/');
                aggregate.AppendData(Encoding.UTF8.GetBytes(relative));
                await using var stream = File.OpenRead(file.SourcePath);
                var hash = await SHA256.HashDataAsync(stream, cancellationToken);
                aggregate.AppendData(hash);
            }

            var fingerprint = Convert
                .ToHexString(aggregate.GetHashAndReset())
                .ToLowerInvariant();
            var versions = Path.Combine(root, "versions");
            var destination = Path.Combine(versions, fingerprint);
            if (!Directory.Exists(destination))
            {
                var staging = Path.Combine(
                    versions,
                    $".{fingerprint}.{Guid.NewGuid():N}.staging");
                Directory.CreateDirectory(staging);
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var target = Path.Combine(staging, file.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file.SourcePath, target, overwrite: false);
                }

                Directory.Move(staging, destination);
            }

            var installedAgent = Path.Combine(
                destination,
                "CodexThemeStudio.Agent.exe");
            var installedNode = Path.Combine(
                destination,
                "runtime",
                "node",
                "node.exe");
            var installedInjector = Path.Combine(
                destination,
                "runtime",
                "injector",
                "index.mjs");
            if (!File.Exists(installedAgent) ||
                !File.Exists(installedNode) ||
                !File.Exists(installedInjector))
            {
                return Failure<ManagedAgentInstallation>(
                    OperationErrorCode.InvalidResponse,
                    "稳定 Agent 目录内容与安装包不一致。",
                    "persistence.install.destination_invalid");
            }

            return OperationResult<ManagedAgentInstallation>.Success(
                new ManagedAgentInstallation(
                    destination,
                    installedAgent,
                    installedNode,
                    installedInjector,
                    fingerprint));
        }
        catch (OperationCanceledException)
        {
            return Failure<ManagedAgentInstallation>(
                OperationErrorCode.Cancelled,
                "Agent 安装已取消。",
                "persistence.install.cancelled");
        }
        catch (UnauthorizedAccessException)
        {
            return Failure<ManagedAgentInstallation>(
                OperationErrorCode.AccessDenied,
                "没有权限安装当前用户 Agent。",
                "persistence.install.access_denied");
        }
        catch (IOException)
        {
            return Failure<ManagedAgentInstallation>(
                OperationErrorCode.StorageUnavailable,
                "无法安装当前用户 Agent。",
                "persistence.install.io_failure");
        }
    }

    private static OperationResult<T> Failure<T>(
        OperationErrorCode code,
        string message,
        string diagnosticCode) =>
        OperationResult<T>.Failure(code, message, diagnosticCode);

    private static IEnumerable<AgentInstallFile> EnumerateSharedRuntimeFiles(
        string source)
    {
        foreach (var relativeRoot in new[]
                 {
                     Path.Combine("runtime", "node"),
                     Path.Combine("runtime", "injector"),
                 })
        {
            var directory = Path.Combine(source, relativeRoot);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(
                         directory,
                         "*",
                         SearchOption.AllDirectories))
            {
                yield return new AgentInstallFile(
                    path,
                    Path.GetRelativePath(source, path));
            }
        }
    }

    private sealed record AgentInstallFile(
        string SourcePath,
        string RelativePath);
}

public sealed class WindowsRunStartupManager : IAgentStartupManager
{
    public const string ValueName = "CodexThemeStudio.PersistenceAgent";
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    public Task<OperationResult> InstallAsync(
        string agentExecutablePath,
        string configurationPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(
                OperationResult.Failure(
                    OperationErrorCode.UnsupportedVersion,
                    "当前用户启动项仅支持 Windows。",
                    "persistence.startup.windows_required"));
        }

        if (!IsSafeAbsoluteFile(agentExecutablePath) ||
            !IsSafeAbsoluteFile(configurationPath) ||
            !File.Exists(agentExecutablePath) ||
            !File.Exists(configurationPath))
        {
            return Task.FromResult(
                OperationResult.Failure(
                    OperationErrorCode.InvalidPath,
                    "Agent 启动路径无效。",
                    "persistence.startup.path_invalid"));
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return Task.FromResult(
                    OperationResult.Failure(
                        OperationErrorCode.AccessDenied,
                        "无法打开当前用户启动项。",
                        "persistence.startup.key_unavailable"));
            }

            key.SetValue(
                ValueName,
                BuildCommand(agentExecutablePath, configurationPath),
                RegistryValueKind.String);
            return Task.FromResult(OperationResult.Success());
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(
                OperationResult.Failure(
                    OperationErrorCode.AccessDenied,
                    "没有权限创建当前用户启动项。",
                    "persistence.startup.access_denied"));
        }
    }

    public Task<OperationResult> RemoveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(
                OperationResult.Failure(
                    OperationErrorCode.UnsupportedVersion,
                    "当前用户启动项仅支持 Windows。",
                    "persistence.startup.windows_required"));
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            return Task.FromResult(OperationResult.Success());
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(
                OperationResult.Failure(
                    OperationErrorCode.AccessDenied,
                    "无法移除当前用户启动项。",
                    "persistence.startup.remove_failed"));
        }
    }

    public Task<OperationResult<bool>> IsInstalledAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(
                OperationResult<bool>.Failure(
                    OperationErrorCode.UnsupportedVersion,
                    "当前用户启动项仅支持 Windows。",
                    "persistence.startup.windows_required"));
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return Task.FromResult(
                OperationResult<bool>.Success(
                    key?.GetValue(ValueName) is string value &&
                    !string.IsNullOrWhiteSpace(value)));
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(
                OperationResult<bool>.Failure(
                    OperationErrorCode.AccessDenied,
                    "无法读取当前用户启动项。",
                    "persistence.startup.read_failed"));
        }
    }

    private static bool IsSafeAbsoluteFile(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Path.IsPathFullyQualified(path) &&
        path.IndexOf('"') < 0 &&
        path.IndexOfAny(['\r', '\n']) < 0;

    private static string Quote(string path) => $"\"{Path.GetFullPath(path)}\"";

    public static string BuildCommand(
        string agentExecutablePath,
        string configurationPath)
    {
        if (!IsSafeAbsoluteFile(agentExecutablePath) ||
            !IsSafeAbsoluteFile(configurationPath))
        {
            throw new ArgumentException("Startup paths must be safe absolute paths.");
        }

        return $"{Quote(agentExecutablePath)} run --config {Quote(configurationPath)}";
    }
}

public sealed class AgentProcessController : IAgentProcessController
{
    public Task<OperationResult> StartAsync(
        string agentExecutablePath,
        string configurationPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var startInfo = new ProcessStartInfo(agentExecutablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--config");
            startInfo.ArgumentList.Add(configurationPath);
            Process.Start(startInfo)?.Dispose();
            return Task.FromResult(OperationResult.Success());
        }
        catch (Exception exception)
            when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return Task.FromResult(
                OperationResult.Failure(
                    OperationErrorCode.ExternalToolFailure,
                    "无法启动持久化 Agent。",
                    "persistence.agent.start_failed"));
        }
    }

    public Task<OperationResult> SignalStopAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            PersistenceAgentRunner.SignalStop()
                ? OperationResult.Success()
                : OperationResult.Failure(
                    OperationErrorCode.ExternalToolFailure,
                    "无法通知持久化 Agent 退出。",
                    "persistence.agent.stop_failed"));
    }
}

public sealed class PersistenceService : IPersistenceService
{
    private readonly ICodexThemeRuntime runtime;
    private readonly IThemeAssetStore assetStore;
    private readonly IThemeRepository repository;
    private readonly IPersistenceSnapshotStore snapshotStore;
    private readonly IManagedAgentInstaller installer;
    private readonly IAgentStartupManager startupManager;
    private readonly IAgentProcessController processController;
    private readonly IPersistenceAgentConfigurationStore configurationStore;
    private readonly string sourceBundleDirectory;
    private readonly string stableRoot;
    private readonly string configurationPath;
    private readonly SemaphoreSlim writeLock = new(1, 1);

    public PersistenceService(
        ICodexThemeRuntime runtime,
        IThemeAssetStore assetStore,
        IThemeRepository repository,
        IPersistenceSnapshotStore snapshotStore,
        IManagedAgentInstaller installer,
        IAgentStartupManager startupManager,
        IAgentProcessController processController,
        string sourceBundleDirectory,
        string? stableRoot = null)
    {
        this.runtime = runtime;
        this.assetStore = assetStore;
        this.repository = repository;
        this.snapshotStore = snapshotStore;
        this.installer = installer;
        this.startupManager = startupManager;
        this.processController = processController;
        this.sourceBundleDirectory = Path.GetFullPath(sourceBundleDirectory);
        this.stableRoot = Path.GetFullPath(stableRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexThemeStudio"));
        configurationPath = Path.Combine(this.stableRoot, "Agent", "config.json");
        configurationStore = new PersistenceAgentConfigurationStore(configurationPath);
    }

    public Task<OperationResult<ThemeRuntimeStatus>> EnableAsync(
        ThemePackage theme,
        CancellationToken cancellationToken) =>
        EnableAsync(theme, new PersistenceOptions(), cancellationToken);

    public async Task<OperationResult<ThemeRuntimeStatus>> EnableAsync(
        ThemePackage theme,
        PersistenceOptions options,
        CancellationToken cancellationToken)
    {
        if (!await writeLock.WaitAsync(0, cancellationToken))
        {
            return Busy();
        }

        try
        {
            var installation = await installer.InstallAsync(
                sourceBundleDirectory,
                Path.Combine(stableRoot, "Agent"),
                cancellationToken);
            if (!installation.IsSuccess)
            {
                return OperationResult<ThemeRuntimeStatus>.Failure(installation.Error!);
            }

            var snapshotRoot = ResolveSnapshotRoot(options);
            if (!snapshotRoot.IsSuccess)
            {
                return OperationResult<ThemeRuntimeStatus>.Failure(snapshotRoot.Error!);
            }

            var snapshot = await snapshotStore.CreateAsync(
                theme,
                assetStore,
                snapshotRoot.Value!,
                createRoot: true,
                cancellationToken);
            if (!snapshot.IsSuccess)
            {
                return OperationResult<ThemeRuntimeStatus>.Failure(snapshot.Error!);
            }

            var activate = await snapshotStore.ActivateAsync(
                snapshot.Value!,
                cancellationToken);
            if (!activate.IsSuccess)
            {
                return OperationResult<ThemeRuntimeStatus>.Failure(activate.Error!);
            }

            var config = CreateConfiguration(
                options.StorageMode,
                snapshotRoot.Value!,
                installation.Value!,
                enabled: true,
                suspended: false);
            var configWrite = await configurationStore.WriteAsync(
                config,
                cancellationToken);
            if (!configWrite.IsSuccess)
            {
                return OperationResult<ThemeRuntimeStatus>.Failure(configWrite.Error!);
            }

            var startup = await startupManager.InstallAsync(
                installation.Value!.AgentExecutablePath,
                configurationPath,
                cancellationToken);
            if (!startup.IsSuccess)
            {
                await configurationStore.WriteAsync(
                    config with { Enabled = false },
                    CancellationToken.None);
                return OperationResult<ThemeRuntimeStatus>.Failure(startup.Error!);
            }

            var apply = await runtime.ApplyTemporaryAsync(theme, cancellationToken);
            if (!apply.IsSuccess &&
                apply.Error!.Code != OperationErrorCode.CodexNotFound)
            {
                await startupManager.RemoveAsync(CancellationToken.None);
                await configurationStore.WriteAsync(
                    config with { Enabled = false },
                    CancellationToken.None);
                return OperationResult<ThemeRuntimeStatus>.Failure(apply.Error!);
            }

            var current = await repository.SetCurrentPersistentAsync(
                theme.Id,
                cancellationToken);
            if (!current.IsSuccess)
            {
                await startupManager.RemoveAsync(CancellationToken.None);
                await configurationStore.WriteAsync(
                    config with { Enabled = false },
                    CancellationToken.None);
                return OperationResult<ThemeRuntimeStatus>.Failure(current.Error!);
            }

            var start = await processController.StartAsync(
                installation.Value.AgentExecutablePath,
                configurationPath,
                cancellationToken);
            if (!start.IsSuccess)
            {
                await startupManager.RemoveAsync(CancellationToken.None);
                await configurationStore.WriteAsync(
                    config with { Enabled = false },
                    CancellationToken.None);
                await runtime.RestoreAsync(CancellationToken.None);
                await repository.SetCurrentPersistentAsync(
                    null,
                    CancellationToken.None);
                return OperationResult<ThemeRuntimeStatus>.Failure(start.Error!);
            }

            return OperationResult<ThemeRuntimeStatus>.Success(
                ToPersistentStatus(
                    apply.IsSuccess ? apply.Value! : null,
                    theme.Id,
                    apply.IsSuccess
                        ? "持久化已启用，当前 Codex 已应用主题。"
                        : "持久化已启用；Agent 将在 Codex 启动后自动应用主题。"));
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async Task<OperationResult<ThemeRuntimeStatus>> SwitchAsync(
        ThemePackage theme,
        CancellationToken cancellationToken)
    {
        if (!await writeLock.WaitAsync(0, cancellationToken))
        {
            return Busy();
        }

        try
        {
            var configResult = await configurationStore.ReadAsync(cancellationToken);
            if (!configResult.IsSuccess || !configResult.Value!.Enabled)
            {
                return OperationResult<ThemeRuntimeStatus>.Failure(
                    OperationErrorCode.Conflict,
                    "持久化尚未启用。",
                    "persistence.switch.not_enabled");
            }

            var config = configResult.Value;
            await configurationStore.WriteAsync(
                config with { Suspended = true },
                cancellationToken);
            await processController.SignalStopAsync(cancellationToken);
            var previous = await snapshotStore.GetCurrentDescriptorAsync(
                config.SnapshotRoot,
                cancellationToken);
            var snapshot = await snapshotStore.CreateAsync(
                theme,
                assetStore,
                config.SnapshotRoot,
                createRoot: false,
                cancellationToken);
            if (!previous.IsSuccess || !snapshot.IsSuccess)
            {
                await ResumeAsync(config, cancellationToken);
                return OperationResult<ThemeRuntimeStatus>.Failure(
                    (previous.IsSuccess ? snapshot.Error : previous.Error)!);
            }

            var activate = await snapshotStore.ActivateAsync(
                snapshot.Value!,
                cancellationToken);
            if (!activate.IsSuccess)
            {
                await ResumeAsync(config, cancellationToken);
                return OperationResult<ThemeRuntimeStatus>.Failure(activate.Error!);
            }

            var apply = await runtime.SwitchTemporaryAsync(theme, cancellationToken);
            if (!apply.IsSuccess &&
                apply.Error!.Code != OperationErrorCode.CodexNotFound)
            {
                await snapshotStore.ActivateAsync(
                    previous.Value!,
                    CancellationToken.None);

                await ResumeAsync(config, CancellationToken.None);
                return OperationResult<ThemeRuntimeStatus>.Failure(apply.Error!);
            }

            var current = await repository.SetCurrentPersistentAsync(
                theme.Id,
                cancellationToken);
            if (!current.IsSuccess)
            {
                await snapshotStore.ActivateAsync(
                    previous.Value!,
                    CancellationToken.None);

                await ResumeAsync(config, CancellationToken.None);
                return OperationResult<ThemeRuntimeStatus>.Failure(current.Error!);
            }

            var resume = await ResumeAsync(config, cancellationToken);
            if (!resume.IsSuccess)
            {
                return OperationResult<ThemeRuntimeStatus>.Failure(resume.Error!);
            }

            return OperationResult<ThemeRuntimeStatus>.Success(
                ToPersistentStatus(
                    apply.IsSuccess ? apply.Value! : null,
                    theme.Id,
                    "持久主题快照已原子切换。"));
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async Task<OperationResult<ThemeRuntimeStatus>> DisableAsync(
        CancellationToken cancellationToken)
    {
        if (!await writeLock.WaitAsync(0, cancellationToken))
        {
            return Busy();
        }

        try
        {
            var remove = await startupManager.RemoveAsync(cancellationToken);
            if (!remove.IsSuccess)
            {
                return OperationResult<ThemeRuntimeStatus>.Failure(remove.Error!);
            }

            var config = await configurationStore.ReadAsync(cancellationToken);
            if (config.IsSuccess)
            {
                var write = await configurationStore.WriteAsync(
                    config.Value! with { Enabled = false, Suspended = false },
                    cancellationToken);
                if (!write.IsSuccess)
                {
                    return OperationResult<ThemeRuntimeStatus>.Failure(write.Error!);
                }
            }

            var stop = await processController.SignalStopAsync(cancellationToken);
            if (!stop.IsSuccess)
            {
                return OperationResult<ThemeRuntimeStatus>.Failure(stop.Error!);
            }

            var restore = await runtime.RestoreAsync(cancellationToken);
            if (!restore.IsSuccess)
            {
                return restore;
            }

            var clear = await repository.SetCurrentPersistentAsync(
                null,
                cancellationToken);
            if (!clear.IsSuccess)
            {
                return OperationResult<ThemeRuntimeStatus>.Failure(clear.Error!);
            }

            return OperationResult<ThemeRuntimeStatus>.Success(
                restore.Value! with
                {
                    IsPersistenceEnabled = false,
                    UserMessage = "持久化已停用，当前 Codex 已还原；主题库保持不变。",
                });
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async Task<OperationResult<ThemeRuntimeStatus>> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        var startup = await startupManager.IsInstalledAsync(cancellationToken);
        if (!startup.IsSuccess)
        {
            return OperationResult<ThemeRuntimeStatus>.Failure(startup.Error!);
        }

        var config = await configurationStore.ReadAsync(cancellationToken);
        if (!config.IsSuccess)
        {
            if (!startup.Value &&
                config.Error!.Code == OperationErrorCode.NotFound)
            {
                return OperationResult<ThemeRuntimeStatus>.Success(
                    new ThemeRuntimeStatus(
                        ThemeRuntimeState.Default,
                        null,
                        false,
                        null,
                        DateTimeOffset.UtcNow,
                        "持久化未启用。"));
            }

            return OperationResult<ThemeRuntimeStatus>.Failure(config.Error!);
        }

        if (config.Value!.Enabled != startup.Value)
        {
            return OperationResult<ThemeRuntimeStatus>.Failure(
                OperationErrorCode.Conflict,
                "持久化配置与当前用户启动项不一致，需要修复或停用。",
                "persistence.status.startup_mismatch");
        }

        if (!config.Value.Enabled)
        {
            return OperationResult<ThemeRuntimeStatus>.Success(
                new ThemeRuntimeStatus(
                    ThemeRuntimeState.Default,
                    null,
                    false,
                    null,
                    DateTimeOffset.UtcNow,
                    "持久化未启用。"));
        }

        var snapshot = await snapshotStore.ReadCurrentAsync(
            config.Value.SnapshotRoot,
            cancellationToken);
        if (!snapshot.IsSuccess)
        {
            return OperationResult<ThemeRuntimeStatus>.Failure(snapshot.Error!);
        }

        var runtimeStatus = await runtime.GetStatusAsync(cancellationToken);
        return runtimeStatus.IsSuccess
            ? OperationResult<ThemeRuntimeStatus>.Success(
                ToPersistentStatus(
                    runtimeStatus.Value!,
                    snapshot.Value!.Theme.Id,
                    "持久化已启用。"))
            : OperationResult<ThemeRuntimeStatus>.Failure(runtimeStatus.Error!);
    }

    private async Task<OperationResult> ResumeAsync(
        PersistenceAgentConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var write = await configurationStore.WriteAsync(
            configuration with { Suspended = false },
            cancellationToken);
        if (!write.IsSuccess)
        {
            return write;
        }

        return await processController.StartAsync(
            configuration.AgentExecutablePath,
            configurationPath,
            cancellationToken);
    }

    private OperationResult<string> ResolveSnapshotRoot(PersistenceOptions options)
    {
        if (options.StorageMode == PersistenceStorageMode.StableLocal)
        {
            return OperationResult<string>.Success(
                Path.Combine(stableRoot, "Runtime", "Persistence"));
        }

        if (string.IsNullOrWhiteSpace(options.DataRoot) ||
            !Path.IsPathFullyQualified(options.DataRoot) ||
            !Directory.Exists(options.DataRoot))
        {
            return OperationResult<string>.Failure(
                OperationErrorCode.StorageUnavailable,
                "严格存储模式的数据目录当前不可用；不会创建替代空目录。",
                "persistence.strict_data_root.unavailable");
        }

        var dataRoot = Path.GetFullPath(options.DataRoot);
        if ((File.GetAttributes(dataRoot) & FileAttributes.ReparsePoint) != 0)
        {
            return OperationResult<string>.Failure(
                OperationErrorCode.InvalidPath,
                "严格存储模式的数据目录不能是符号链接或 Junction。",
                "persistence.strict_data_root.reparse");
        }

        return OperationResult<string>.Success(
            Path.Combine(
                dataRoot,
                "runtime",
                "persistence"));
    }

    private PersistenceAgentConfiguration CreateConfiguration(
        PersistenceStorageMode mode,
        string snapshotRoot,
        ManagedAgentInstallation installation,
        bool enabled,
        bool suspended) =>
        new(
            PersistenceAgentConfiguration.CurrentSchemaVersion,
            enabled,
            suspended,
            mode,
            snapshotRoot,
            installation.AgentExecutablePath,
            installation.NodeExecutablePath,
            installation.InjectorScriptPath,
            Path.Combine(stableRoot, "Agent", "state.json"),
            Path.Combine(stableRoot, "Logs", "agent.jsonl"));

    private static ThemeRuntimeStatus ToPersistentStatus(
        ThemeRuntimeStatus? runtimeStatus,
        Guid themeId,
        string message) =>
        runtimeStatus is null
            ? new ThemeRuntimeStatus(
                ThemeRuntimeState.Persistent,
                themeId,
                true,
                null,
                DateTimeOffset.UtcNow,
                message,
                themeId)
            : runtimeStatus with
            {
                State = ThemeRuntimeState.Persistent,
                ThemeId = themeId,
                SelectedThemeId = themeId,
                IsPersistenceEnabled = true,
                UserMessage = message,
            };

    private static OperationResult<ThemeRuntimeStatus> Busy() =>
        OperationResult<ThemeRuntimeStatus>.Failure(
            OperationErrorCode.Conflict,
            "另一个持久化写操作正在执行。",
            "persistence.operation_busy");
}
