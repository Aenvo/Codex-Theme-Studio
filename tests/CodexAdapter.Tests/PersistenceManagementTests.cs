using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.CodexAdapter.Tests;

public sealed class PersistenceManagementTests
{
    [Fact]
    public void StartupCommand_QuotesPathsWithSpacesAndChinese()
    {
        var command = WindowsRunStartupManager.BuildCommand(
            @"C:\用户 数据\Codex Theme Studio\CodexThemeStudio.Agent.exe",
            @"C:\用户 数据\Codex Theme Studio\config.json");

        Assert.Equal(
            "\"C:\\用户 数据\\Codex Theme Studio\\CodexThemeStudio.Agent.exe\" " +
            "run --config \"C:\\用户 数据\\Codex Theme Studio\\config.json\"",
            command);
    }

    [Fact]
    public async Task Installer_CopiesBundleToContentAddressedStableDirectory()
    {
        var parent = CreateTemporaryDirectory();
        var source = Path.Combine(parent, "便携 GUI 源");
        var stable = Path.Combine(parent, "稳定 Agent");
        try
        {
            await CreateAgentBundleAsync(source);
            await File.WriteAllTextAsync(
                Path.Combine(source, "CodexThemeStudio.Desktop.dll"),
                "desktop-only");

            var result = await new ManagedAgentInstaller().InstallAsync(
                source,
                stable,
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.StartsWith(
                Path.Combine(stable, "versions"),
                result.Value!.VersionDirectory,
                StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(result.Value.AgentExecutablePath));
            Assert.True(File.Exists(result.Value.NodeExecutablePath));
            Assert.True(File.Exists(result.Value.InjectorScriptPath));
            Assert.False(File.Exists(Path.Combine(
                result.Value.VersionDirectory,
                "CodexThemeStudio.Desktop.dll")));
            Assert.Equal(64, result.Value.ContentFingerprint.Length);

            Directory.Delete(source, recursive: true);
            Assert.True(File.Exists(result.Value.AgentExecutablePath));
            Assert.True(File.Exists(result.Value.NodeExecutablePath));
            Assert.True(File.Exists(result.Value.InjectorScriptPath));
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public async Task Installer_RejectsUnknownManifestSchema()
    {
        var parent = CreateTemporaryDirectory();
        try
        {
            var source = Path.Combine(parent, "source");
            await CreateAgentBundleAsync(source, schemaVersion: 2);

            var result = await new ManagedAgentInstaller().InstallAsync(
                source,
                Path.Combine(parent, "stable"),
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(
                "persistence.install.manifest_invalid",
                result.Error!.DiagnosticCode);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public async Task Installer_RejectsPathEscapeAndDuplicateDestination()
    {
        var parent = CreateTemporaryDirectory();
        try
        {
            var source = Path.Combine(parent, "source");
            var entries = await CreateAgentBundleAsync(source);
            entries.Add(entries[0] with { Source = "../outside.exe" });
            await WriteAgentManifestAsync(source, 1, entries);

            var escaped = await new ManagedAgentInstaller().InstallAsync(
                source,
                Path.Combine(parent, "stable-escape"),
                CancellationToken.None);

            Assert.False(escaped.IsSuccess);
            Assert.Equal(
                "persistence.install.manifest_invalid",
                escaped.Error!.DiagnosticCode);

            entries = await CreateAgentBundleAsync(source);
            entries.Add(entries[0] with { Source = entries[1].Source });
            await WriteAgentManifestAsync(source, 1, entries);

            var duplicate = await new ManagedAgentInstaller().InstallAsync(
                source,
                Path.Combine(parent, "stable-duplicate"),
                CancellationToken.None);

            Assert.False(duplicate.IsSuccess);
            Assert.Equal(
                "persistence.install.manifest_invalid",
                duplicate.Error!.DiagnosticCode);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public async Task Installer_RejectsHashMismatchAndCorruptExistingVersion()
    {
        var parent = CreateTemporaryDirectory();
        try
        {
            var source = Path.Combine(parent, "source");
            await CreateAgentBundleAsync(source);
            var agentSource = Path.Combine(
                source,
                "agent",
                "CodexThemeStudio.Agent.exe");
            await File.AppendAllTextAsync(agentSource, "tampered");

            var mismatch = await new ManagedAgentInstaller().InstallAsync(
                source,
                Path.Combine(parent, "stable-mismatch"),
                CancellationToken.None);

            Assert.False(mismatch.IsSuccess);
            Assert.Equal(
                "persistence.install.bundle_mismatch",
                mismatch.Error!.DiagnosticCode);

            await CreateAgentBundleAsync(source);
            var stable = Path.Combine(parent, "stable");
            var first = await new ManagedAgentInstaller().InstallAsync(
                source,
                stable,
                CancellationToken.None);
            Assert.True(first.IsSuccess);

            await File.AppendAllTextAsync(
                first.Value!.AgentExecutablePath,
                "corrupt");
            var second = await new ManagedAgentInstaller().InstallAsync(
                source,
                stable,
                CancellationToken.None);

            Assert.False(second.IsSuccess);
            Assert.Equal(
                "persistence.install.destination_invalid",
                second.Error!.DiagnosticCode);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public async Task Installer_ReusesValidContentFingerprint()
    {
        var parent = CreateTemporaryDirectory();
        try
        {
            var source = Path.Combine(parent, "source");
            var stable = Path.Combine(parent, "stable");
            await CreateAgentBundleAsync(source);

            var first = await new ManagedAgentInstaller().InstallAsync(
                source,
                stable,
                CancellationToken.None);
            var second = await new ManagedAgentInstaller().InstallAsync(
                source,
                stable,
                CancellationToken.None);

            Assert.True(first.IsSuccess);
            Assert.True(second.IsSuccess);
            Assert.Equal(
                first.Value!.ContentFingerprint,
                second.Value!.ContentFingerprint);
            Assert.Equal(
                first.Value.VersionDirectory,
                second.Value.VersionDirectory);
            Assert.Single(Directory.GetDirectories(
                Path.Combine(stable, "versions")));
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public async Task Installer_CancellationDoesNotLeaveStagingDirectory()
    {
        var parent = CreateTemporaryDirectory();
        try
        {
            var source = Path.Combine(parent, "source");
            var stable = Path.Combine(parent, "stable");
            await CreateAgentBundleAsync(source);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var result = await new ManagedAgentInstaller().InstallAsync(
                source,
                stable,
                cancellation.Token);

            Assert.False(result.IsSuccess);
            Assert.Equal(
                "persistence.install.cancelled",
                result.Error!.DiagnosticCode);
            Assert.False(Directory.Exists(Path.Combine(stable, "versions")));
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public async Task Installer_RejectsReparsePointSourceAndStableRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var parent = CreateTemporaryDirectory();
        try
        {
            var source = Path.Combine(parent, "source");
            var junction = Path.Combine(parent, "source-junction");
            await CreateAgentBundleAsync(source);
            await CreateDirectoryJunctionAsync(junction, source);

            var result = await new ManagedAgentInstaller().InstallAsync(
                junction,
                Path.Combine(parent, "stable"),
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(
                "persistence.install.bundle_incomplete",
                result.Error!.DiagnosticCode);

            var stableTarget = Path.Combine(parent, "stable-target");
            var stableJunction = Path.Combine(parent, "stable-junction");
            Directory.CreateDirectory(stableTarget);
            await CreateDirectoryJunctionAsync(stableJunction, stableTarget);

            var stableResult = await new ManagedAgentInstaller().InstallAsync(
                source,
                stableJunction,
                CancellationToken.None);

            Assert.False(stableResult.IsSuccess);
            Assert.Equal(
                "persistence.install.path_invalid",
                stableResult.Error!.DiagnosticCode);
        }
        finally
        {
            foreach (var junctionName in new[]
                     {
                         "source-junction",
                         "stable-junction",
                     })
            {
                var junction = Path.Combine(parent, junctionName);
                if (Directory.Exists(junction))
                {
                    Directory.Delete(junction);
                }
            }

            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public async Task Enable_WhenCodexNotRunningStillInstallsPersistentAgent()
    {
        var fixture = new ServiceFixture();
        fixture.Runtime.ApplyResult =
            OperationResult<ThemeRuntimeStatus>.Failure(
                OperationErrorCode.CodexNotFound,
                "Codex 未运行。",
                "runtime.codex_not_running");

        var result = await fixture.Service.EnableAsync(
            fixture.Theme,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsPersistenceEnabled);
        Assert.True(fixture.Startup.Installed);
        Assert.Equal(1, fixture.Controller.StartCount);
        Assert.Equal(fixture.Theme.Id, fixture.Repository.CurrentPersistent);
        Assert.Equal(fixture.Theme.Id, fixture.Snapshot.Active!.ThemeId);
    }

    [Fact]
    public async Task Switch_WhenCodexNotRunningStillChangesActiveSnapshot()
    {
        var fixture = new ServiceFixture();
        Assert.True(
            (await fixture.Service.EnableAsync(
                fixture.Theme,
                CancellationToken.None)).IsSuccess);
        var next = CreateTheme(Guid.NewGuid());
        fixture.Runtime.SwitchResult =
            OperationResult<ThemeRuntimeStatus>.Failure(
                OperationErrorCode.CodexNotFound,
                "Codex 未运行。",
                "runtime.codex_not_running");

        var result = await fixture.Service.SwitchAsync(
            next,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(next.Id, fixture.Repository.CurrentPersistent);
        Assert.Equal(next.Id, fixture.Snapshot.Active!.ThemeId);
        Assert.Equal(2, fixture.Controller.StartCount);
    }

    [Fact]
    public async Task Switch_WhenRuntimeFailsRollsBackSnapshotPointer()
    {
        var fixture = new ServiceFixture();
        Assert.True(
            (await fixture.Service.EnableAsync(
                fixture.Theme,
                CancellationToken.None)).IsSuccess);
        var oldDescriptor = fixture.Snapshot.Active;
        fixture.Runtime.SwitchResult =
            OperationResult<ThemeRuntimeStatus>.Failure(
                OperationErrorCode.Timeout,
                "Inspector 超时。",
                "injector_timeout");

        var result = await fixture.Service.SwitchAsync(
            CreateTheme(Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(oldDescriptor, fixture.Snapshot.Active);
        Assert.Equal(fixture.Theme.Id, fixture.Repository.CurrentPersistent);
    }

    [Fact]
    public async Task Disable_WhenStartupRemovalFailsDoesNotPretendSuccess()
    {
        var fixture = new ServiceFixture();
        Assert.True(
            (await fixture.Service.EnableAsync(
                fixture.Theme,
                CancellationToken.None)).IsSuccess);
        fixture.Startup.RemoveError = new OperationError(
            OperationErrorCode.AccessDenied,
            "启动项移除失败。",
            "persistence.startup.remove_failed");

        var result = await fixture.Service.DisableAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.AccessDenied, result.Error!.Code);
        Assert.Equal(0, fixture.Runtime.RestoreCount);
        Assert.Equal(0, fixture.Controller.StopCount);
        Assert.True(fixture.Startup.Installed);
    }

    [Fact]
    public async Task StrictMode_MissingDataRootFailsWithoutSnapshotCreation()
    {
        var fixture = new ServiceFixture();
        var missing = Path.Combine(fixture.Root, "offline");

        var result = await fixture.Service.EnableAsync(
            fixture.Theme,
            new PersistenceOptions(
                PersistenceStorageMode.StrictDataRoot,
                missing),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.StorageUnavailable, result.Error!.Code);
        Assert.Equal(0, fixture.Snapshot.CreateCount);
        Assert.False(Directory.Exists(missing));
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cts-management-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task CreateDirectoryJunctionAsync(
        string junction,
        string target)
    {
        var commandInterpreter = Environment.GetEnvironmentVariable("ComSpec");
        Assert.False(string.IsNullOrWhiteSpace(commandInterpreter));
        var startInfo = new ProcessStartInfo(commandInterpreter!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(junction);
        startInfo.ArgumentList.Add(target);

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(
            process.ExitCode == 0,
            $"Could not create test junction: {await output} {await error}");
    }

    private static async Task<List<AgentManifestEntry>> CreateAgentBundleAsync(
        string source,
        int schemaVersion = 1)
    {
        var files = new Dictionary<string, string>
        {
            ["agent/CodexThemeStudio.Agent.exe"] = "agent",
            ["shared-runtime.dll"] = "shared",
            ["runtime/node/node.exe"] = "node",
            ["runtime/injector/index.mjs"] = "injector",
        };
        foreach (var (relativePath, content) in files)
        {
            var path = Path.Combine(source, relativePath.Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content);
        }

        var entries = new List<AgentManifestEntry>();
        foreach (var relativePath in files.Keys)
        {
            var sourcePath = Path.Combine(source, relativePath.Replace('/', '\\'));
            var destination = relativePath.StartsWith(
                "agent/",
                StringComparison.Ordinal)
                ? relativePath["agent/".Length..]
                : relativePath;
            entries.Add(new AgentManifestEntry(
                relativePath,
                destination,
                new FileInfo(sourcePath).Length,
                await Sha256Async(sourcePath)));
        }

        await WriteAgentManifestAsync(source, schemaVersion, entries);
        return entries;
    }

    private static async Task WriteAgentManifestAsync(
        string source,
        int schemaVersion,
        IReadOnlyList<AgentManifestEntry> entries)
    {
        var path = Path.Combine(source, "agent", "agent-bundle-manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(new
            {
                schemaVersion,
                files = entries.Select(entry => new
                {
                    source = entry.Source,
                    destination = entry.Destination,
                    bytes = entry.Bytes,
                    sha256 = entry.Sha256,
                }),
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static async Task<string> Sha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(
                await SHA256.HashDataAsync(stream))
            .ToLowerInvariant();
    }

    private sealed record AgentManifestEntry(
        string Source,
        string Destination,
        long Bytes,
        string Sha256);

    private static ThemePackage CreateTheme(Guid id) =>
        new(
            1,
            id,
            "Persistence Management",
            ThemeVariant.Auto,
            new ThemePalette(
                "#101010",
                "#202020",
                "#3366FF",
                "#FFFFFF",
                "#AAAAAA",
                "#444444"),
            new ThemeArt(
                "background.png",
                0.5,
                0.5,
                ThemeSafeArea.Auto,
                ThemeArtSize.Cover,
                0.8,
                0.2,
                ThemeTaskMode.Ambient,
                0.25,
                0.5,
                0));

    private sealed class ServiceFixture : IDisposable
    {
        public ServiceFixture()
        {
            Root = CreateTemporaryDirectory();
            Theme = CreateTheme(Guid.NewGuid());
            Service = new PersistenceService(
                Runtime,
                new FakeAssets(),
                Repository,
                Snapshot,
                new FakeInstaller(),
                Startup,
                Controller,
                Root,
                Root);
        }

        public string Root { get; }

        public ThemePackage Theme { get; }

        public FakeRuntime Runtime { get; } = new();

        public FakeRepository Repository { get; } = new();

        public FakeSnapshot Snapshot { get; } = new();

        public FakeStartup Startup { get; } = new();

        public FakeController Controller { get; } = new();

        public PersistenceService Service { get; }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class FakeRuntime : ICodexThemeRuntime
    {
        public OperationResult<ThemeRuntimeStatus>? ApplyResult { get; set; }

        public OperationResult<ThemeRuntimeStatus>? SwitchResult { get; set; }

        public int RestoreCount { get; private set; }

        public Task<OperationResult<ThemeRuntimeStatus>> ApplyTemporaryAsync(
            ThemePackage theme,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                ApplyResult ?? Success(theme.Id, ThemeRuntimeState.Temporary));

        public Task<OperationResult<ThemeRuntimeStatus>> SwitchTemporaryAsync(
            ThemePackage theme,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                SwitchResult ?? Success(theme.Id, ThemeRuntimeState.Temporary));

        public Task<OperationResult<ThemeRuntimeStatus>> RestoreAsync(
            CancellationToken cancellationToken)
        {
            RestoreCount++;
            return Task.FromResult(Success(null, ThemeRuntimeState.Default));
        }

        public Task<OperationResult<ThemeRuntimeStatus>> GetStatusAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(Success(null, ThemeRuntimeState.Ready));

        public Task<OperationResult<ThemeRuntimeStatus>> GetStatusAsync(
            CodexStatusRefreshMode refreshMode,
            CancellationToken cancellationToken) =>
            GetStatusAsync(cancellationToken);

        public Task<OperationResult<CodexCachedCompatibilityStatus?>>
            GetCachedCompatibilityAsync(CancellationToken cancellationToken) =>
            Task.FromResult(
                OperationResult<CodexCachedCompatibilityStatus?>.SuccessOptional(null));

        private static OperationResult<ThemeRuntimeStatus> Success(
            Guid? themeId,
            ThemeRuntimeState state) =>
            OperationResult<ThemeRuntimeStatus>.Success(
                new ThemeRuntimeStatus(
                    state,
                    themeId,
                    false,
                    1234,
                    DateTimeOffset.UtcNow,
                    "ok"));
    }

    private sealed class FakeSnapshot : IPersistenceSnapshotStore
    {
        public PersistenceSnapshotDescriptor? Active { get; private set; }

        public int CreateCount { get; private set; }

        public Task<OperationResult<PersistenceSnapshotDescriptor>> CreateAsync(
            ThemePackage theme,
            IThemeAssetStore assetStore,
            string snapshotRoot,
            bool createRoot,
            CancellationToken cancellationToken)
        {
            CreateCount++;
            return Task.FromResult(
                OperationResult<PersistenceSnapshotDescriptor>.Success(
                    new PersistenceSnapshotDescriptor(
                        Guid.NewGuid(),
                        theme.Id,
                        new string((char)('a' + Math.Min(CreateCount, 20)), 64),
                        snapshotRoot)));
        }

        public Task<OperationResult<PersistenceSnapshotDescriptor>>
            GetCurrentDescriptorAsync(
                string snapshotRoot,
                CancellationToken cancellationToken) =>
            Task.FromResult(
                Active is null
                    ? OperationResult<PersistenceSnapshotDescriptor>.Failure(
                        OperationErrorCode.NotFound,
                        "未创建。",
                        "persistence.pointer.missing")
                    : OperationResult<PersistenceSnapshotDescriptor>.Success(Active));

        public Task<OperationResult> ActivateAsync(
            PersistenceSnapshotDescriptor descriptor,
            CancellationToken cancellationToken)
        {
            Active = descriptor;
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult<VerifiedPersistenceSnapshot>> ReadCurrentAsync(
            string snapshotRoot,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeInstaller : IManagedAgentInstaller
    {
        public Task<OperationResult<ManagedAgentInstallation>> InstallAsync(
            string sourceDirectory,
            string stableAgentRoot,
            CancellationToken cancellationToken)
        {
            var version = Path.Combine(stableAgentRoot, "versions", "test");
            return Task.FromResult(
                OperationResult<ManagedAgentInstallation>.Success(
                    new ManagedAgentInstallation(
                        version,
                        Path.Combine(version, "CodexThemeStudio.Agent.exe"),
                        Path.Combine(version, "runtime", "node", "node.exe"),
                        Path.Combine(version, "runtime", "injector", "index.mjs"),
                        new string('a', 64))));
        }
    }

    private sealed class FakeStartup : IAgentStartupManager
    {
        public bool Installed { get; private set; }

        public OperationError? RemoveError { get; set; }

        public Task<OperationResult> InstallAsync(
            string agentExecutablePath,
            string configurationPath,
            CancellationToken cancellationToken)
        {
            Installed = true;
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> RemoveAsync(
            CancellationToken cancellationToken)
        {
            if (RemoveError is not null)
            {
                return Task.FromResult(OperationResult.Failure(RemoveError));
            }

            Installed = false;
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult<bool>> IsInstalledAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<bool>.Success(Installed));
    }

    private sealed class FakeController : IAgentProcessController
    {
        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public Task<OperationResult> StartAsync(
            string agentExecutablePath,
            string configurationPath,
            CancellationToken cancellationToken)
        {
            StartCount++;
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> SignalStopAsync(
            CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class FakeAssets : IThemeAssetStore
    {
        public Task<OperationResult<string>> SaveAsync(
            Guid themeId,
            string fileName,
            Stream content,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult<Stream>> OpenReadAsync(
            string relativePath,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                OperationResult<Stream>.Success(new MemoryStream(
                    [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00])));
    }

    private sealed class FakeRepository : IThemeRepository
    {
        public Guid? CurrentPersistent { get; private set; }

        public Task<OperationResult> SetCurrentPersistentAsync(
            Guid? themeId,
            CancellationToken cancellationToken)
        {
            CurrentPersistent = themeId;
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult<IReadOnlyList<ThemeSummary>>> ListAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult<IReadOnlyList<ThemeSummary>>> ListDeletedAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(
                OperationResult<IReadOnlyList<ThemeSummary>>.Success(
                    Array.Empty<ThemeSummary>()));

        public Task<OperationResult<ThemePackage>> GetAsync(
            Guid themeId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult> SaveAsync(
            ThemePackage theme,
            ThemeCreateOptions? createOptions,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult<ThemePackage>> CopyAsync(
            Guid sourceThemeId,
            Guid newThemeId,
            string newName,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult> RenameAsync(
            Guid themeId,
            string newName,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult> SetFavoriteAsync(
            Guid themeId,
            bool isFavorite,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult> SetSortOrderAsync(
            Guid themeId,
            int sortOrder,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult> SetTagsAsync(
            Guid themeId,
            IReadOnlyList<string> tags,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult> RecordApplyResultAsync(
            Guid themeId,
            ThemeApplyResult result,
            string? userMessage,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult> DeleteAsync(
            Guid themeId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult> RestoreDeletedAsync(
            Guid themeId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult> PermanentlyDeleteAsync(
            Guid themeId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
