using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.CodexAdapter.Tests;

public sealed class PersistenceAgentTests
{
    [Fact]
    public async Task Snapshot_RoundTripsAndRejectsCorruptedImage()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var theme = CreateTheme(Guid.NewGuid());
            var store = new PersistenceSnapshotStore();
            var created = await store.CreateAsync(
                theme,
                new FakeAssetStore(),
                root,
                createRoot: false,
                CancellationToken.None);
            Assert.True(created.IsSuccess);
            Assert.True(
                (await store.ActivateAsync(
                    created.Value!,
                    CancellationToken.None)).IsSuccess);

            var verified = await store.ReadCurrentAsync(root, CancellationToken.None);
            Assert.True(verified.IsSuccess);
            Assert.Equal(theme, verified.Value!.Theme);
            Assert.NotEmpty(verified.Value.Image);

            var snapshotDirectory = Path.Combine(
                root,
                "snapshots",
                created.Value!.SnapshotId.ToString("D"));
            var image = Directory.EnumerateFiles(snapshotDirectory, "background.*").Single();
            await File.AppendAllTextAsync(image, "tampered");

            var corrupted = await store.ReadCurrentAsync(root, CancellationToken.None);
            Assert.False(corrupted.IsSuccess);
            Assert.Equal(OperationErrorCode.InvalidResponse, corrupted.Error!.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Snapshot_StrictUnavailableRootFailsWithoutCreatingIt()
    {
        var parent = CreateTemporaryDirectory();
        var unavailable = Path.Combine(parent, "offline-drive-data");
        try
        {
            var result = await new PersistenceSnapshotStore().CreateAsync(
                CreateTheme(Guid.NewGuid()),
                new FakeAssetStore(),
                unavailable,
                createRoot: false,
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(OperationErrorCode.StorageUnavailable, result.Error!.Code);
            Assert.False(Directory.Exists(unavailable));
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public async Task Agent_NewPidAppliesOnceAndAgentRestartDoesNotRepeat()
    {
        var fixture = new AgentFixture();

        var first = await fixture.Engine.RunCycleAsync(
            fixture.Configuration,
            CancellationToken.None);
        var restartedEngine = fixture.CreateEngine();
        var second = await restartedEngine.RunCycleAsync(
            fixture.Configuration,
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(1, fixture.Renderer.ApplyCount);
        Assert.Equal(ThemeRuntimeState.Persistent, second.Value!.State);
        Assert.Equal(ThemeRuntimeEvidence.ProcessOnly, second.Value.Evidence);
    }

    [Fact]
    public async Task Agent_UnqualifiedVerifiedFingerprintPausesWithoutInspector()
    {
        var fixture = new AgentFixture(qualified: false);

        var result = await fixture.Engine.RunCycleAsync(
            fixture.Configuration,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ThemeRuntimeState.Unsupported, result.Value!.State);
        Assert.Equal(0, fixture.Renderer.StatusCount);
        Assert.Equal(0, fixture.Renderer.ApplyCount);
    }

    [Fact]
    public async Task Agent_SamePidWithDifferentStartTimeIsNotPidReuse()
    {
        var fixture = new AgentFixture();
        Assert.True(
            (await fixture.Engine.RunCycleAsync(
                fixture.Configuration,
                CancellationToken.None)).IsSuccess);
        fixture.Discovery.Process = fixture.Discovery.Process with
        {
            StartedAtUtc = fixture.Discovery.Process.StartedAtUtc.AddMinutes(1),
        };

        var second = await fixture.Engine.RunCycleAsync(
            fixture.Configuration,
            CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(2, fixture.Renderer.ApplyCount);
        Assert.Equal(
            fixture.Discovery.Process.StartedAtUtc,
            fixture.State.State.AppliedProcessStartedAtUtc);
    }

    [Fact]
    public async Task Agent_ThemeSwitchFingerprintForcesOneNewApply()
    {
        var fixture = new AgentFixture();
        Assert.True(
            (await fixture.Engine.RunCycleAsync(
                fixture.Configuration,
                CancellationToken.None)).IsSuccess);
        fixture.Snapshot.Current = CreateSnapshot(Guid.NewGuid(), "b".PadLeft(64, 'b'));

        var switched = await fixture.Engine.RunCycleAsync(
            fixture.Configuration,
            CancellationToken.None);
        var repeated = await fixture.Engine.RunCycleAsync(
            fixture.Configuration,
            CancellationToken.None);

        Assert.True(switched.IsSuccess);
        Assert.True(repeated.IsSuccess);
        Assert.Equal(2, fixture.Renderer.ApplyCount);
    }

    [Fact]
    public async Task Agent_DamagedSnapshotFailsClosedWithoutInspector()
    {
        var fixture = new AgentFixture();
        fixture.Snapshot.Error = new OperationError(
            OperationErrorCode.InvalidResponse,
            "快照损坏。",
            "persistence.snapshot.hash_mismatch");

        var result = await fixture.Engine.RunCycleAsync(
            fixture.Configuration,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, fixture.Renderer.ApplyCount);
        Assert.Equal(0, fixture.Discovery.FindInstallationCount);
    }

    [Fact]
    public async Task Agent_AfterVerifyIntervalReappliesWhenRendererLost()
    {
        var time = new MutableTimeProvider(
            new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero));
        var fixture = new AgentFixture(time);
        Assert.True(
            (await fixture.Engine.RunCycleAsync(
                fixture.Configuration,
                CancellationToken.None)).IsSuccess);
        time.UtcNow = time.UtcNow.AddMinutes(2);
        fixture.Renderer.StatusResult =
            OperationResult<RendererRuntimeResult>.Success(
                new RendererRuntimeResult(1, false, null, null, 0, 0, 1, 0));

        var result = await fixture.Engine.RunCycleAsync(
            fixture.Configuration,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, fixture.Renderer.StatusCount);
        Assert.Equal(2, fixture.Renderer.ApplyCount);
    }

    [Fact]
    public async Task Agent_FirstCycleAdoptsMatchingGuiRuntimeWithoutReapply()
    {
        var fixture = new AgentFixture();
        fixture.Renderer.StatusResult =
            OperationResult<RendererRuntimeResult>.Success(
                new RendererRuntimeResult(
                    1,
                    true,
                    1,
                    fixture.Snapshot.Current.Theme.Id,
                    1,
                    1,
                    1,
                    2));

        var result = await fixture.Engine.RunCycleAsync(
            fixture.Configuration,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, fixture.Renderer.ApplyCount);
        Assert.Equal(1, fixture.Renderer.StatusCount);
        Assert.Equal(
            fixture.Snapshot.Current.Theme.Id,
            fixture.State.State.ThemeId);
    }

    [Fact]
    public void ConfigurationPolicy_RejectsArbitraryExecutablePaths()
    {
        var fixture = new AgentFixture();

        var result = PersistenceAgentConfigurationPolicy.Validate(
            fixture.Configuration,
            @"C:\Agent\config.json");

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.ValidationFailed, result.Error!.Code);
        Assert.Equal(
            "persistence.config.runtime_identity_invalid",
            result.Error.DiagnosticCode);
    }

    [Fact]
    public async Task Runner_WhenMutexAlreadyOwnedExitsAsSecondInstance()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var suffix = Guid.NewGuid().ToString("N");
        var mutexName = $@"Local\CodexThemeStudio.Tests.Agent.{suffix}";
        var stopEventName = $@"Local\CodexThemeStudio.Tests.Agent.Stop.{suffix}";
        using var mutex = new Mutex(
            initiallyOwned: true,
            mutexName,
            out var createdNew);
        Assert.True(createdNew);

        var exitCode = await new PersistenceAgentRunner(
                @"C:\does-not-need-to-exist.json",
                "test",
                mutexName,
                stopEventName)
            .RunAsync(CancellationToken.None);

        Assert.Equal(3, exitCode);
    }

    [Fact]
    public async Task Runner_WithIsolatedNamesStartsAsFirstInstance()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var suffix = Guid.NewGuid().ToString("N");
        var runner = new PersistenceAgentRunner(
            @"C:\does-not-need-to-exist.json",
            "test",
            $@"Local\CodexThemeStudio.Tests.Agent.{suffix}",
            $@"Local\CodexThemeStudio.Tests.Agent.Stop.{suffix}");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exitCode = await runner.RunAsync(cancellation.Token);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Runner_PublicConstructorUsesProductionWaitHandleNames()
    {
        var runner = new PersistenceAgentRunner(
            @"C:\does-not-need-to-exist.json");

        Assert.Equal(PersistenceAgentRunner.MutexName, runner.InstanceMutexName);
        Assert.Equal(PersistenceAgentRunner.StopEventName, runner.ShutdownEventName);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cts-agent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static ThemePackage CreateTheme(Guid id) =>
        new(
            1,
            id,
            "Persistence Test",
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

    private static VerifiedPersistenceSnapshot CreateSnapshot(
        Guid themeId,
        string fingerprint) =>
        new(
            new PersistenceSnapshotDescriptor(
                Guid.NewGuid(),
                themeId,
                fingerprint,
                @"C:\snapshot"),
            CreateTheme(themeId),
            FakeAssetStore.Png);

    private sealed class AgentFixture
    {
        public AgentFixture(
            TimeProvider? timeProvider = null,
            bool qualified = true)
        {
            Snapshot.Current = CreateSnapshot(Guid.NewGuid(), new string('a', 64));
            TimeProvider = timeProvider ?? TimeProvider.System;
            Qualification = qualified
                ? CodexCompatibilityQualificationStore.CreateInMemory(
                    string.Empty)
                : CodexCompatibilityQualificationStore.CreateInMemory();
            Engine = CreateEngine();
        }

        public FakeSnapshotStore Snapshot { get; } = new();

        public FakeDiscovery Discovery { get; } = new();

        public FakeRenderer Renderer { get; } = new();

        public MemoryStateStore State { get; } = new();

        public TimeProvider TimeProvider { get; }

        public CodexCompatibilityQualificationStore Qualification { get; }

        public PersistenceAgentEngine Engine { get; }

        public PersistenceAgentConfiguration Configuration { get; } =
            new(
                PersistenceAgentConfiguration.CurrentSchemaVersion,
                true,
                false,
                PersistenceStorageMode.StableLocal,
                @"C:\snapshot",
                @"C:\Agent\CodexThemeStudio.Agent.exe",
                @"C:\Agent\runtime\node\node.exe",
                @"C:\Agent\runtime\injector\index.mjs",
                @"C:\Agent\state.json",
                @"C:\Agent\agent.jsonl",
                5,
                60);

        public PersistenceAgentEngine CreateEngine() =>
            new(
                Snapshot,
                Discovery,
                Discovery,
                Renderer,
                State,
                qualificationStore: Qualification,
                timeProvider: TimeProvider);
    }

    private sealed class FakeSnapshotStore : IPersistenceSnapshotStore
    {
        public VerifiedPersistenceSnapshot Current { get; set; } = null!;

        public OperationError? Error { get; set; }

        public Task<OperationResult<VerifiedPersistenceSnapshot>> ReadCurrentAsync(
            string snapshotRoot,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Error is null
                    ? OperationResult<VerifiedPersistenceSnapshot>.Success(Current)
                    : OperationResult<VerifiedPersistenceSnapshot>.Failure(Error));

        public Task<OperationResult<PersistenceSnapshotDescriptor>> CreateAsync(
            ThemePackage theme,
            IThemeAssetStore assetStore,
            string snapshotRoot,
            bool createRoot,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult<PersistenceSnapshotDescriptor>>
            GetCurrentDescriptorAsync(
                string snapshotRoot,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult> ActivateAsync(
            PersistenceSnapshotDescriptor descriptor,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeDiscovery :
        ICodexDiscoveryService,
        ICodexInspectorService
    {
        public CodexProcessInfo Process { get; set; } =
            new(
                4242,
                new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero),
                @"C:\Program Files\WindowsApps\OpenAI.Codex\ChatGPT.exe",
                null);

        public int FindInstallationCount { get; private set; }

        public Task<OperationResult<CodexDiscoverySnapshot>> DiscoverAsync(
            CancellationToken cancellationToken)
        {
            FindInstallationCount++;
            var installation = new CodexInstallationInfo(
                "OpenAI.Codex_2p2nqsd0c76g0",
                "OpenAI.Codex_26.715.4045.0_x64__2p2nqsd0c76g0",
                "26.715.4045.0",
                Process.ExecutablePath);
            return Task.FromResult(
                OperationResult<CodexDiscoverySnapshot>.Success(
                    new CodexDiscoverySnapshot(
                        installation,
                        [Process],
                        DateTimeOffset.UtcNow)));
        }

        public Task<OperationResult<CodexProbeResult>> ProbeAsync(
            CodexProcessInfo process,
            CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<CodexProbeResult>.Success(
                new CodexProbeResult(
                    process.ProcessId,
                    process.StartedAtUtc,
                    "150.0.7871.124",
                    1,
                    ["main"],
                    TimeSpan.Zero)));

        public Task<OperationResult> CloseInspectorAsync(
            CodexProcessInfo process,
            CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Success());
    }

    private sealed class FakeRenderer : IInjectorRendererClient
    {
        public int ApplyCount { get; private set; }

        public int StatusCount { get; private set; }

        public OperationResult<RendererRuntimeResult> StatusResult { get; set; } =
            OperationResult<RendererRuntimeResult>.Success(
                new RendererRuntimeResult(1, false, null, null, 0, 0, 1, 0));

        public Task<OperationResult<RendererRuntimeResult>> ApplyAsync(
            CodexProcessInfo process,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            ApplyCount++;
            using var document = System.Text.Json.JsonDocument.Parse(payload);
            var themeId = document.RootElement
                .GetProperty("theme")
                .GetProperty("id")
                .GetGuid();
            return Task.FromResult(
                OperationResult<RendererRuntimeResult>.Success(
                    new RendererRuntimeResult(
                        1,
                        true,
                        ApplyCount,
                        themeId,
                        1,
                        1,
                        1,
                        2)));
        }

        public Task<OperationResult<RendererRuntimeResult>> GetStatusAsync(
            CodexProcessInfo process,
            CancellationToken cancellationToken)
        {
            StatusCount++;
            return Task.FromResult(StatusResult);
        }

        public Task<OperationResult<RendererRuntimeResult>> CleanupAsync(
            CodexProcessInfo process,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult<CodexInspectionResult>> InspectAsync(
            CodexProcessInfo process,
            CodexInspectionMode mode,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                StatusResult.IsSuccess
                    ? OperationResult<CodexInspectionResult>.Success(
                        new CodexInspectionResult(null, StatusResult.Value!))
                    : OperationResult<CodexInspectionResult>.Failure(
                        StatusResult.Error!));
    }

    private sealed class MemoryStateStore : IPersistenceAgentStateStore
    {
        public PersistenceAgentState State { get; private set; } =
            PersistenceAgentState.Empty;

        public Task<OperationResult<PersistenceAgentState>> ReadAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<PersistenceAgentState>.Success(State));

        public Task<OperationResult> WriteAsync(
            PersistenceAgentState state,
            CancellationToken cancellationToken)
        {
            State = state;
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class FakeAssetStore : IThemeAssetStore
    {
        public static readonly byte[] Png =
        [
            0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00,
        ];

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
                OperationResult<Stream>.Success(new MemoryStream(Png)));
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
