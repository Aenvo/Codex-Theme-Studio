using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    private const int ManifestSchemaVersion = 1;
    private const int MaximumManifestBytes = 1024 * 1024;
    private const int MaximumInstallFiles = 4096;
    private const long MaximumInstallBytes = 1024L * 1024L * 1024L;
    private const string ManifestRelativePath = "agent/agent-bundle-manifest.json";
    private static readonly JsonSerializerOptions ManifestJsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
        };

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
        if (!Directory.Exists(source) ||
            IsReparsePoint(source))
        {
            return Failure<ManagedAgentInstallation>(
                OperationErrorCode.NotFound,
                "Agent 安装包不完整或来源不受信任。",
                "persistence.install.bundle_incomplete");
        }

        try
        {
            var manifestResult = await ReadManifestAsync(
                source,
                cancellationToken);
            if (!manifestResult.IsSuccess)
            {
                return OperationResult<ManagedAgentInstallation>.Failure(
                    manifestResult.Error!);
            }

            var filesResult = await ValidateFilesAsync(
                source,
                manifestResult.Value!,
                cancellationToken);
            if (!filesResult.IsSuccess)
            {
                return OperationResult<ManagedAgentInstallation>.Failure(
                    filesResult.Error!);
            }

            var files = filesResult.Value!;
            using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                aggregate.AppendData(Encoding.UTF8.GetBytes(file.DestinationRelativePath));
                aggregate.AppendData(file.Hash);
            }

            var fingerprint = Convert
                .ToHexString(aggregate.GetHashAndReset())
                .ToLowerInvariant();
            var versions = Path.Combine(root, "versions");
            Directory.CreateDirectory(root);
            if (IsReparsePoint(root))
            {
                return Failure<ManagedAgentInstallation>(
                    OperationErrorCode.InvalidPath,
                    "稳定 Agent 安装目录不能是重解析点。",
                    "persistence.install.path_invalid");
            }

            Directory.CreateDirectory(versions);
            if (IsReparsePoint(versions))
            {
                return Failure<ManagedAgentInstallation>(
                    OperationErrorCode.InvalidPath,
                    "稳定 Agent 版本目录不能是重解析点。",
                    "persistence.install.path_invalid");
            }

            var destination = Path.Combine(versions, fingerprint);
            if (Directory.Exists(destination))
            {
                var existing = await VerifyInstalledFilesAsync(
                    destination,
                    files,
                    cancellationToken);
                if (!existing.IsSuccess)
                {
                    return OperationResult<ManagedAgentInstallation>.Failure(
                        existing.Error!);
                }
            }
            else
            {
                var staging = Path.Combine(
                    versions,
                    $".{fingerprint}.{Guid.NewGuid():N}.staging");
                Directory.CreateDirectory(staging);
                try
                {
                    foreach (var file in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var target = ResolveRelativePath(
                            staging,
                            file.DestinationRelativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(file.SourcePath, target, overwrite: false);
                    }

                    var staged = await VerifyInstalledFilesAsync(
                        staging,
                        files,
                        cancellationToken);
                    if (!staged.IsSuccess)
                    {
                        return OperationResult<ManagedAgentInstallation>.Failure(
                            staged.Error!);
                    }

                    Directory.Move(staging, destination);
                }
                finally
                {
                    if (Directory.Exists(staging))
                    {
                        Directory.Delete(staging, recursive: true);
                    }
                }
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
        catch (JsonException)
        {
            return Failure<ManagedAgentInstallation>(
                OperationErrorCode.ValidationFailed,
                "Agent 安装清单无效。",
                "persistence.install.manifest_invalid");
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidDataException or
            NotSupportedException or
            PathTooLongException)
        {
            return Failure<ManagedAgentInstallation>(
                OperationErrorCode.ValidationFailed,
                "Agent 安装清单包含无效路径。",
                "persistence.install.manifest_invalid");
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

    private static async Task<OperationResult<AgentBundleManifest>>
        ReadManifestAsync(
            string source,
            CancellationToken cancellationToken)
    {
        var manifestPath = ResolveRelativePath(source, ManifestRelativePath);
        if (!IsRegularFileWithinRoot(manifestPath, source))
        {
            return Failure<AgentBundleManifest>(
                OperationErrorCode.NotFound,
                "Agent 安装包缺少受信任的安装清单。",
                "persistence.install.manifest_missing");
        }

        var info = new FileInfo(manifestPath);
        if (info.Length is <= 0 or > MaximumManifestBytes)
        {
            return Failure<AgentBundleManifest>(
                OperationErrorCode.ValidationFailed,
                "Agent 安装清单大小无效。",
                "persistence.install.manifest_invalid");
        }

        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<AgentBundleManifest>(
            stream,
            ManifestJsonOptions,
            cancellationToken);
        if (manifest is null ||
            manifest.SchemaVersion != ManifestSchemaVersion ||
            manifest.Files is null ||
            manifest.Files.Count is <= 0 or > MaximumInstallFiles)
        {
            return Failure<AgentBundleManifest>(
                OperationErrorCode.ValidationFailed,
                "Agent 安装清单版本或文件数量无效。",
                "persistence.install.manifest_invalid");
        }

        return OperationResult<AgentBundleManifest>.Success(manifest);
    }

    private static async Task<OperationResult<AgentInstallFile[]>>
        ValidateFilesAsync(
            string source,
            AgentBundleManifest manifest,
            CancellationToken cancellationToken)
    {
        var destinationPaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var files = new List<AgentInstallFile>(manifest.Files.Count);
        long totalBytes = 0;
        foreach (var entry in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryNormalizeRelativePath(entry.Source, out var sourceRelative) ||
                !TryNormalizeRelativePath(
                    entry.Destination,
                    out var destinationRelative) ||
                !destinationPaths.Add(destinationRelative) ||
                entry.Bytes is <= 0 or > MaximumInstallBytes ||
                !TryParseSha256(entry.Sha256, out var expectedHash))
            {
                return Failure<AgentInstallFile[]>(
                    OperationErrorCode.ValidationFailed,
                    "Agent 安装清单包含无效路径、大小或哈希。",
                    "persistence.install.manifest_invalid");
            }

            totalBytes = checked(totalBytes + entry.Bytes);
            if (totalBytes > MaximumInstallBytes)
            {
                return Failure<AgentInstallFile[]>(
                    OperationErrorCode.ValidationFailed,
                    "Agent 安装清单总大小超出限制。",
                    "persistence.install.manifest_invalid");
            }

            var sourcePath = ResolveRelativePath(source, sourceRelative);
            if (!IsRegularFileWithinRoot(sourcePath, source))
            {
                return Failure<AgentInstallFile[]>(
                    OperationErrorCode.NotFound,
                    "Agent 安装包缺少清单声明的文件。",
                    "persistence.install.bundle_incomplete");
            }

            var info = new FileInfo(sourcePath);
            if (info.Length != entry.Bytes)
            {
                return Failure<AgentInstallFile[]>(
                    OperationErrorCode.InvalidResponse,
                    "Agent 安装包文件大小与清单不一致。",
                    "persistence.install.bundle_mismatch");
            }

            await using var stream = File.OpenRead(sourcePath);
            var actualHash = await SHA256.HashDataAsync(stream, cancellationToken);
            if (!actualHash.AsSpan().SequenceEqual(expectedHash))
            {
                return Failure<AgentInstallFile[]>(
                    OperationErrorCode.InvalidResponse,
                    "Agent 安装包文件哈希与清单不一致。",
                    "persistence.install.bundle_mismatch");
            }

            files.Add(new AgentInstallFile(
                sourcePath,
                destinationRelative,
                actualHash,
                entry.Bytes));
        }

        var required = new[]
        {
            "CodexThemeStudio.Agent.exe",
            "runtime/node/node.exe",
            "runtime/injector/index.mjs",
        };
        if (required.Any(path => !destinationPaths.Contains(path)))
        {
            return Failure<AgentInstallFile[]>(
                OperationErrorCode.ValidationFailed,
                "Agent 安装清单缺少必需入口。",
                "persistence.install.manifest_invalid");
        }

        return OperationResult<AgentInstallFile[]>.Success(
            files.OrderBy(
                    file => file.DestinationRelativePath,
                    StringComparer.Ordinal)
                .ToArray());
    }

    private static async Task<OperationResult> VerifyInstalledFilesAsync(
        string root,
        IReadOnlyList<AgentInstallFile> files,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root) || IsReparsePoint(root))
        {
            return OperationResult.Failure(
                OperationErrorCode.InvalidResponse,
                "稳定 Agent 目录内容不完整。",
                "persistence.install.destination_invalid");
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = ResolveRelativePath(
                root,
                file.DestinationRelativePath);
            if (!IsRegularFileWithinRoot(path, root) ||
                new FileInfo(path).Length != file.Bytes)
            {
                return OperationResult.Failure(
                    OperationErrorCode.InvalidResponse,
                    "稳定 Agent 目录内容不完整。",
                    "persistence.install.destination_invalid");
            }

            await using var stream = File.OpenRead(path);
            var actualHash = await SHA256.HashDataAsync(stream, cancellationToken);
            if (!actualHash.AsSpan().SequenceEqual(file.Hash))
            {
                return OperationResult.Failure(
                    OperationErrorCode.InvalidResponse,
                    "稳定 Agent 目录内容校验失败。",
                    "persistence.install.destination_invalid");
            }
        }

        return OperationResult.Success();
    }

    private static bool TryNormalizeRelativePath(
        string? value,
        out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) ||
            Path.IsPathFullyQualified(value))
        {
            return false;
        }

        var segments = value
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.None);
        if (segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment) ||
                segment is "." or ".."))
        {
            return false;
        }

        normalized = string.Join('/', segments);
        return true;
    }

    private static bool TryParseSha256(
        string? value,
        out byte[] hash)
    {
        hash = [];
        if (value is not { Length: 64 })
        {
            return false;
        }

        try
        {
            hash = Convert.FromHexString(value);
            return hash.Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string ResolveRelativePath(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(
            fullRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Relative path escaped its root.");
        }

        return path;
    }

    private static bool IsRegularFileWithinRoot(string path, string root)
    {
        if (!File.Exists(path) || IsReparsePoint(path))
        {
            return false;
        }

        var fullRoot = Path.GetFullPath(root);
        var directory = new DirectoryInfo(Path.GetDirectoryName(path)!);
        while (!string.Equals(
                   directory.FullName,
                   fullRoot,
                   StringComparison.OrdinalIgnoreCase))
        {
            if (!directory.Exists ||
                IsReparsePoint(directory.FullName) ||
                directory.Parent is null)
            {
                return false;
            }

            directory = directory.Parent;
        }

        return true;
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static OperationResult<T> Failure<T>(
        OperationErrorCode code,
        string message,
        string diagnosticCode) =>
        OperationResult<T>.Failure(code, message, diagnosticCode);

    private sealed record AgentBundleManifest(
        int SchemaVersion,
        IReadOnlyList<AgentBundleManifestFile> Files);

    private sealed record AgentBundleManifestFile(
        string Source,
        string Destination,
        long Bytes,
        string Sha256);

    private sealed record AgentInstallFile(
        string SourcePath,
        string DestinationRelativePath,
        byte[] Hash,
        long Bytes);
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
    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultStartupTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan StartupStabilityDelay = TimeSpan.FromMilliseconds(250);
    private readonly string mutexName;
    private readonly string stopEventName;
    private readonly TimeSpan stopTimeout;
    private readonly TimeSpan startupTimeout;
    private readonly TimeSpan pollInterval;
    private readonly Func<ProcessStartInfo, Process?> startProcess;

    public AgentProcessController()
        : this(
            PersistenceAgentRunner.MutexName,
            PersistenceAgentRunner.StopEventName,
            DefaultStopTimeout,
            DefaultStartupTimeout,
            DefaultPollInterval,
            static startInfo => Process.Start(startInfo))
    {
    }

    internal AgentProcessController(
        string mutexName,
        string stopEventName,
        TimeSpan stopTimeout,
        TimeSpan startupTimeout,
        TimeSpan pollInterval,
        Func<ProcessStartInfo, Process?> startProcess)
    {
        this.mutexName = mutexName;
        this.stopEventName = stopEventName;
        this.stopTimeout = stopTimeout;
        this.startupTimeout = startupTimeout;
        this.pollInterval = pollInterval;
        this.startProcess = startProcess;
    }

    public async Task<OperationResult> StartAsync(
        string agentExecutablePath,
        string configurationPath,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return OperationResult.Failure(
                OperationErrorCode.NotImplemented,
                "持久化 Agent 仅支持 Windows。",
                "persistence.agent.platform_unsupported");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var released = await WaitForMutexStateAsync(
            expectedActive: false,
            startupTimeout,
            cancellationToken,
            "旧持久化 Agent 尚未完全退出，未启动新 Agent。",
            "persistence.agent.start_existing_timeout");
        if (!released.IsSuccess)
        {
            return released;
        }

        Process? process;
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
            process = startProcess(startInfo);
            if (process is null)
            {
                return OperationResult.Failure(
                    OperationErrorCode.ExternalToolFailure,
                    "无法启动持久化 Agent。",
                    "persistence.agent.start_failed");
            }
        }
        catch (Exception exception)
            when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return OperationResult.Failure(
                OperationErrorCode.ExternalToolFailure,
                "无法启动持久化 Agent。",
                "persistence.agent.start_failed");
        }

        using (process)
        {
            var deadline = DateTimeOffset.UtcNow + startupTimeout;
            DateTimeOffset? activeSince = null;
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.HasExited)
                {
                    return OperationResult.Failure(
                        OperationErrorCode.ExternalToolFailure,
                        "持久化 Agent 在取得单实例锁前退出。",
                        "persistence.agent.start_exited");
                }

                var active = ProbeMutex();
                if (!active.IsSuccess)
                {
                    return OperationResult.Failure(active.Error!);
                }

                if (active.Value)
                {
                    activeSince ??= DateTimeOffset.UtcNow;
                    if (DateTimeOffset.UtcNow - activeSince >= StartupStabilityDelay)
                    {
                        return OperationResult.Success();
                    }
                }
                else
                {
                    activeSince = null;
                }

                await Task.Delay(pollInterval, cancellationToken);
            }

            return OperationResult.Failure(
                OperationErrorCode.Timeout,
                "持久化 Agent 已启动，但未能确认其取得单实例锁。",
                "persistence.agent.start_timeout");
        }
    }

    public async Task<OperationResult> SignalStopAsync(
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return OperationResult.Failure(
                OperationErrorCode.NotImplemented,
                "持久化 Agent 仅支持 Windows。",
                "persistence.agent.platform_unsupported");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var stopEvent = EventWaitHandle.OpenExisting(stopEventName);
            if (!stopEvent.Set())
            {
                return OperationResult.Failure(
                    OperationErrorCode.ExternalToolFailure,
                    "无法通知持久化 Agent 退出。",
                    "persistence.agent.stop_failed");
            }
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return OperationResult.Success();
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResult.Failure(
                OperationErrorCode.AccessDenied,
                "无法通知持久化 Agent 退出。",
                "persistence.agent.stop_access_denied");
        }

        return await WaitForMutexStateAsync(
            expectedActive: false,
            stopTimeout,
            cancellationToken,
            "持久化 Agent 未在 10 秒内退出；未切换持久主题。",
            "persistence.agent.stop_timeout");
    }

    private async Task<OperationResult> WaitForMutexStateAsync(
        bool expectedActive,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string timeoutMessage,
        string timeoutCode)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var active = ProbeMutex();
            if (!active.IsSuccess)
            {
                return OperationResult.Failure(active.Error!);
            }

            if (active.Value == expectedActive)
            {
                return OperationResult.Success();
            }

            await Task.Delay(pollInterval, cancellationToken);
        }

        return OperationResult.Failure(
            OperationErrorCode.Timeout,
            timeoutMessage,
            timeoutCode);
    }

    private OperationResult<bool> ProbeMutex()
    {
        try
        {
            using var mutex = Mutex.OpenExisting(mutexName);
            return OperationResult<bool>.Success(true);
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return OperationResult<bool>.Success(false);
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResult<bool>.Failure(
                OperationErrorCode.AccessDenied,
                "无法确认持久化 Agent 的单实例状态。",
                "persistence.agent.mutex_access_denied");
        }
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

    internal PersistenceService(
        ICodexThemeRuntime runtime,
        IThemeAssetStore assetStore,
        IThemeRepository repository,
        IPersistenceSnapshotStore snapshotStore,
        IManagedAgentInstaller installer,
        IAgentStartupManager startupManager,
        IAgentProcessController processController,
        string sourceBundleDirectory,
        string stableRoot,
        IPersistenceAgentConfigurationStore configurationStore)
        : this(
            runtime,
            assetStore,
            repository,
            snapshotStore,
            installer,
            startupManager,
            processController,
            sourceBundleDirectory,
            stableRoot)
    {
        this.configurationStore = configurationStore;
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
            var eligibility = await runtime.GetStatusAsync(cancellationToken);
            if (!eligibility.IsSuccess)
            {
                return OperationResult<ThemeRuntimeStatus>.Failure(eligibility.Error!);
            }

            if (!eligibility.Value!.IsPersistenceEligible)
            {
                return OperationResult<ThemeRuntimeStatus>.Failure(
                    OperationErrorCode.ValidationFailed,
                    "请先临时应用主题；首次兼容验证会自动执行应用、清理和重新应用，成功后才能启用持久化。",
                    "compatibility.persistence_not_qualified");
            }

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
            var installation = await installer.InstallAsync(
                sourceBundleDirectory,
                Path.Combine(stableRoot, "Agent"),
                cancellationToken);
            if (!installation.IsSuccess)
            {
                return OperationResult<ThemeRuntimeStatus>.Failure(installation.Error!);
            }

            var previous = await snapshotStore.ReadCurrentAsync(
                config.SnapshotRoot,
                cancellationToken);
            if (!previous.IsSuccess)
            {
                return OperationResult<ThemeRuntimeStatus>.Failure(previous.Error!);
            }

            var suspend = await configurationStore.WriteAsync(
                config with { Suspended = true },
                cancellationToken);
            if (!suspend.IsSuccess)
            {
                return OperationResult<ThemeRuntimeStatus>.Failure(suspend.Error!);
            }

            var stop = await processController.SignalStopAsync(cancellationToken);
            if (!stop.IsSuccess)
            {
                var restoreConfig = await configurationStore.WriteAsync(
                    config,
                    CancellationToken.None);
                var restoreAgent = restoreConfig.IsSuccess
                    ? await processController.StartAsync(
                        config.AgentExecutablePath,
                        configurationPath,
                        CancellationToken.None)
                    : OperationResult.Failure(restoreConfig.Error!);
                if (!restoreConfig.IsSuccess || !restoreAgent.IsSuccess)
                {
                    return OperationResult<ThemeRuntimeStatus>.Failure(
                        OperationErrorCode.InvalidResponse,
                        $"{stop.Error!.UserMessage} 旧持久化 Agent 未能完整恢复，请停止重试并检查诊断日志。",
                        "persistence.switch.stop_recovery_failed");
                }

                return OperationResult<ThemeRuntimeStatus>.Failure(stop.Error!);
            }

            var snapshot = await snapshotStore.CreateAsync(
                theme,
                assetStore,
                config.SnapshotRoot,
                createRoot: false,
                cancellationToken);
            if (!snapshot.IsSuccess)
            {
                return await FailSwitchWithRollbackAsync(
                    snapshot.Error!,
                    config,
                    previous.Value!,
                    restoreRuntime: false);
            }

            var activate = await snapshotStore.ActivateAsync(
                snapshot.Value!,
                cancellationToken);
            if (!activate.IsSuccess)
            {
                return await FailSwitchWithRollbackAsync(
                    activate.Error!,
                    config,
                    previous.Value!,
                    restoreRuntime: false);
            }

            var apply = await runtime.SwitchTemporaryAsync(theme, cancellationToken);
            if (!apply.IsSuccess &&
                apply.Error!.Code != OperationErrorCode.CodexNotFound)
            {
                return await FailSwitchWithRollbackAsync(
                    apply.Error!,
                    config,
                    previous.Value!,
                    restoreRuntime: false);
            }

            var current = await repository.SetCurrentPersistentAsync(
                theme.Id,
                cancellationToken);
            if (!current.IsSuccess)
            {
                return await FailSwitchWithRollbackAsync(
                    current.Error!,
                    config,
                    previous.Value!,
                    restoreRuntime: apply.IsSuccess);
            }

            var upgraded = CreateConfiguration(
                config.StorageMode,
                config.SnapshotRoot,
                installation.Value!,
                enabled: true,
                suspended: true) with
            {
                PollIntervalSeconds = config.PollIntervalSeconds,
                VerifyIntervalSeconds = config.VerifyIntervalSeconds,
            };
            var configPrepare = await configurationStore.WriteAsync(
                upgraded,
                cancellationToken);
            if (!configPrepare.IsSuccess)
            {
                return await FailSwitchWithRollbackAsync(
                    configPrepare.Error!,
                    config,
                    previous.Value!,
                    restoreRuntime: apply.IsSuccess);
            }

            var startup = await startupManager.InstallAsync(
                installation.Value!.AgentExecutablePath,
                configurationPath,
                cancellationToken);
            if (!startup.IsSuccess)
            {
                return await FailSwitchWithRollbackAsync(
                    startup.Error!,
                    config,
                    previous.Value!,
                    restoreRuntime: apply.IsSuccess);
            }

            var configCommit = await configurationStore.WriteAsync(
                upgraded with { Suspended = false },
                cancellationToken);
            if (!configCommit.IsSuccess)
            {
                return await FailSwitchWithRollbackAsync(
                    configCommit.Error!,
                    config,
                    previous.Value!,
                    restoreRuntime: apply.IsSuccess);
            }

            var start = await processController.StartAsync(
                installation.Value!.AgentExecutablePath,
                configurationPath,
                cancellationToken);
            if (!start.IsSuccess)
            {
                return await FailSwitchWithRollbackAsync(
                    start.Error!,
                    config,
                    previous.Value!,
                    restoreRuntime: apply.IsSuccess);
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
        if (!runtimeStatus.IsSuccess)
        {
            return OperationResult<ThemeRuntimeStatus>.Failure(runtimeStatus.Error!);
        }

        var runtimeValue = runtimeStatus.Value!;
        if (runtimeValue.State is ThemeRuntimeState.Temporary or ThemeRuntimeState.Partial &&
            runtimeValue.ThemeId is not null)
        {
            return OperationResult<ThemeRuntimeStatus>.Success(runtimeValue with
            {
                IsPersistenceEnabled = true,
                SelectedThemeId = snapshot.Value!.Theme.Id,
                UserMessage = "持久化已启用；当前 Codex 正在使用临时主题。",
            });
        }

        return OperationResult<ThemeRuntimeStatus>.Success(
            ToPersistentStatus(
                runtimeValue,
                snapshot.Value!.Theme.Id,
                "持久化已启用。"));
    }

    public async Task<OperationResult<AgentUpgradeResult>> UpgradeAgentAsync(
        CancellationToken cancellationToken)
    {
        if (!await writeLock.WaitAsync(0, cancellationToken))
        {
            return OperationResult<AgentUpgradeResult>.Failure(
                OperationErrorCode.Conflict,
                "另一个持久化写操作正在执行。",
                "persistence.upgrade.busy");
        }

        try
        {
            var previousResult = await configurationStore.ReadAsync(cancellationToken);
            if (!previousResult.IsSuccess)
            {
                if (previousResult.Error!.Code == OperationErrorCode.NotFound)
                {
                    return OperationResult<AgentUpgradeResult>.Success(
                        new AgentUpgradeResult(false, false, "持久化服务未启用。"));
                }

                return OperationResult<AgentUpgradeResult>.Failure(previousResult.Error);
            }

            var previous = previousResult.Value!;
            if (!previous.Enabled)
            {
                return OperationResult<AgentUpgradeResult>.Success(
                    new AgentUpgradeResult(false, false, "持久化服务未启用。"));
            }

            var installation = await installer.InstallAsync(
                sourceBundleDirectory,
                Path.Combine(stableRoot, "Agent"),
                cancellationToken);
            if (!installation.IsSuccess)
            {
                return OperationResult<AgentUpgradeResult>.Failure(installation.Error!);
            }

            if (string.Equals(
                Path.GetFullPath(previous.AgentExecutablePath),
                Path.GetFullPath(installation.Value!.AgentExecutablePath),
                StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult<AgentUpgradeResult>.Success(
                    new AgentUpgradeResult(false, false, "持久化服务已是当前版本。"));
            }

            var upgraded = CreateConfiguration(
                previous.StorageMode,
                previous.SnapshotRoot,
                installation.Value,
                enabled: true,
                suspended: true) with
            {
                PollIntervalSeconds = previous.PollIntervalSeconds,
                VerifyIntervalSeconds = previous.VerifyIntervalSeconds,
            };

            var stop = await processController.SignalStopAsync(cancellationToken);
            if (!stop.IsSuccess)
            {
                var restore = await processController.StartAsync(
                    previous.AgentExecutablePath,
                    configurationPath,
                    CancellationToken.None);
                if (!restore.IsSuccess)
                {
                    return OperationResult<AgentUpgradeResult>.Failure(
                        OperationErrorCode.InvalidResponse,
                        $"{stop.Error!.UserMessage} 旧持久化 Agent 未能完整恢复，请停止重试并检查诊断日志。",
                        "persistence.upgrade.stop_recovery_failed");
                }

                return OperationResult<AgentUpgradeResult>.Failure(stop.Error!);
            }

            var prepare = await configurationStore.WriteAsync(upgraded, cancellationToken);
            if (!prepare.IsSuccess)
            {
                await processController.StartAsync(
                    previous.AgentExecutablePath,
                    configurationPath,
                    CancellationToken.None);
                return OperationResult<AgentUpgradeResult>.Failure(prepare.Error!);
            }

            var startup = await startupManager.InstallAsync(
                installation.Value.AgentExecutablePath,
                configurationPath,
                cancellationToken);
            var commit = startup.IsSuccess
                ? await configurationStore.WriteAsync(
                    upgraded with { Suspended = false },
                    cancellationToken)
                : OperationResult.Failure(startup.Error!);
            var start = commit.IsSuccess
                ? await processController.StartAsync(
                    installation.Value.AgentExecutablePath,
                    configurationPath,
                    cancellationToken)
                : OperationResult.Failure(commit.Error!);
            if (!startup.IsSuccess || !commit.IsSuccess || !start.IsSuccess)
            {
                await startupManager.InstallAsync(
                    previous.AgentExecutablePath,
                    configurationPath,
                    CancellationToken.None);
                await configurationStore.WriteAsync(previous, CancellationToken.None);
                await processController.StartAsync(
                    previous.AgentExecutablePath,
                    configurationPath,
                    CancellationToken.None);
                return OperationResult<AgentUpgradeResult>.Failure(
                    OperationErrorCode.ExternalToolFailure,
                    "新版持久化服务未能启动；已继续保留并运行旧版。",
                    "persistence.upgrade.rolled_back");
            }

            RemoveUnreferencedAgentVersions(installation.Value.VersionDirectory);
            return OperationResult<AgentUpgradeResult>.Success(
                new AgentUpgradeResult(true, true, "持久化服务已升级。"));
        }
        finally
        {
            writeLock.Release();
        }
    }

    private void RemoveUnreferencedAgentVersions(string activeVersionDirectory)
    {
        var versionsRoot = Path.GetFullPath(Path.Combine(stableRoot, "Agent", "versions"));
        if (!Directory.Exists(versionsRoot) ||
            (File.GetAttributes(versionsRoot) & FileAttributes.ReparsePoint) != 0)
        {
            return;
        }

        var active = Path.GetFullPath(activeVersionDirectory);
        foreach (var directory in Directory.EnumerateDirectories(versionsRoot))
        {
            try
            {
                var candidate = Path.GetFullPath(directory);
                if (string.Equals(candidate, active, StringComparison.OrdinalIgnoreCase) ||
                    (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                Directory.Delete(candidate, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private async Task<OperationResult<ThemeRuntimeStatus>>
        FailSwitchWithRollbackAsync(
            OperationError originalError,
            PersistenceAgentConfiguration previousConfiguration,
            VerifiedPersistenceSnapshot previousSnapshot,
            bool restoreRuntime)
    {
        using var rollbackSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = rollbackSource.Token;
        var rollbackSucceeded = true;
        try
        {
            var snapshot = await snapshotStore.ActivateAsync(
                previousSnapshot.Descriptor,
                token);
            rollbackSucceeded &= snapshot.IsSuccess;

            var current = await repository.SetCurrentPersistentAsync(
                previousSnapshot.Theme.Id,
                token);
            rollbackSucceeded &= current.IsSuccess;

            if (restoreRuntime)
            {
                var runtimeRestore = await runtime.SwitchTemporaryAsync(
                    previousSnapshot.Theme,
                    token);
                rollbackSucceeded &= runtimeRestore.IsSuccess ||
                    runtimeRestore.Error!.Code == OperationErrorCode.CodexNotFound;
            }

            var suspendedPrevious = await configurationStore.WriteAsync(
                previousConfiguration with { Suspended = true },
                token);
            rollbackSucceeded &= suspendedPrevious.IsSuccess;

            var startup = await startupManager.InstallAsync(
                previousConfiguration.AgentExecutablePath,
                configurationPath,
                token);
            rollbackSucceeded &= startup.IsSuccess;

            var config = await configurationStore.WriteAsync(
                previousConfiguration,
                token);
            rollbackSucceeded &= config.IsSuccess;

            var start = await processController.StartAsync(
                previousConfiguration.AgentExecutablePath,
                configurationPath,
                token);
            rollbackSucceeded &= start.IsSuccess;
        }
        catch (OperationCanceledException)
        {
            rollbackSucceeded = false;
        }

        if (!rollbackSucceeded)
        {
            return OperationResult<ThemeRuntimeStatus>.Failure(
                OperationErrorCode.InvalidResponse,
                $"{originalError.UserMessage} 旧持久化状态未能完整恢复，请停止重试并检查诊断日志。",
                "persistence.switch.rollback_failed");
        }

        return OperationResult<ThemeRuntimeStatus>.Failure(originalError);
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
