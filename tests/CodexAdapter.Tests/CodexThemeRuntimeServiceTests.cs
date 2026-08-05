using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.CodexAdapter.Tests;

public sealed class CodexThemeRuntimeServiceTests
{
    [Fact]
    public async Task Apply_WritesSessionThenRecordsRecentUse()
    {
        var fixture = new RuntimeFixture();
        var theme = CreateTheme(Guid.NewGuid());

        var result = await fixture.Service.ApplyTemporaryAsync(
            theme,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ThemeRuntimeState.Temporary, result.Value!.State);
        Assert.Equal(theme.Id, fixture.Session.State.ThemeId);
        Assert.Equal(ThemeApplyResult.Succeeded, fixture.Repository.LastApplyResult);
        Assert.Equal(["renderer.apply", "session.write", "repository.record"], fixture.Events);
        Assert.False(result.Value.IsPersistenceEnabled);
    }

    [Fact]
    public async Task UnknownVersion_FirstApplyCompletesCleanupReapplyAndUnlocksPersistence()
    {
        var fixture = new RuntimeFixture();
        var theme = CreateTheme(Guid.NewGuid());
        fixture.Discovery.InstallationResult =
            OperationResult<CodexInstallationInfo>.Success(
                RuntimeFixture.Installation with
                {
                    Version = "99.0.0.0",
                    ExecutableSha256 = "unknown-build-hash",
                });

        var result = await fixture.Service.ApplyTemporaryAsync(
            theme,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CodexCompatibilityLevel.CompatibleByProbe, result.Value!.CompatibilityLevel);
        Assert.True(result.Value.IsPersistenceEligible);
        Assert.Equal(2, fixture.Renderer.ApplyCount);
        Assert.Equal(1, fixture.Renderer.CleanupCount);
        var qualified = await fixture.Qualification.IsQualifiedAsync(
            "unknown-build-hash",
            CancellationToken.None);
        Assert.True(qualified.IsSuccess && qualified.Value);
    }

    [Fact]
    public async Task VerifiedVersion_NewFingerprintStillCompletesQualificationCycle()
    {
        var fixture = new RuntimeFixture();
        var theme = CreateTheme(Guid.NewGuid());
        const string newFingerprint =
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        fixture.Discovery.InstallationResult =
            OperationResult<CodexInstallationInfo>.Success(
                RuntimeFixture.Installation with
                {
                    ExecutableSha256 = newFingerprint,
                });

        var result = await fixture.Service.ApplyTemporaryAsync(
            theme,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsPersistenceEligible);
        Assert.Equal(2, fixture.Renderer.ApplyCount);
        Assert.Equal(1, fixture.Renderer.CleanupCount);
        var qualified = await fixture.Qualification.IsQualifiedAsync(
            newFingerprint,
            CancellationToken.None);
        Assert.True(qualified.IsSuccess && qualified.Value);
    }

    [Fact]
    public async Task QualificationCycle_UsesDedicatedLongerDeadline()
    {
        var fixture = new RuntimeFixture(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(500));
        fixture.Renderer.ApplyDelay = TimeSpan.FromMilliseconds(45);
        fixture.Renderer.CleanupDelay = TimeSpan.FromMilliseconds(45);
        fixture.Discovery.InstallationResult =
            OperationResult<CodexInstallationInfo>.Success(
                RuntimeFixture.Installation with
                {
                    ExecutableSha256 = "slow-qualification-hash",
                });

        var result = await fixture.Service.ApplyTemporaryAsync(
            CreateTheme(Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsPersistenceEligible);
        Assert.Equal(2, fixture.Renderer.ApplyCount);
        Assert.Equal(1, fixture.Renderer.CleanupCount);
    }

    [Fact]
    public async Task QualificationCycle_DeadlineReturnsTimeoutAndRecovers()
    {
        var fixture = new RuntimeFixture(
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(45));
        fixture.Renderer.ApplyDelay = TimeSpan.FromMilliseconds(20);
        fixture.Renderer.CleanupDelay = TimeSpan.FromMilliseconds(20);
        fixture.Discovery.InstallationResult =
            OperationResult<CodexInstallationInfo>.Success(
                RuntimeFixture.Installation with
                {
                    ExecutableSha256 = "qualification-timeout-hash",
                });

        var result = await fixture.Service.ApplyTemporaryAsync(
            CreateTheme(Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.Timeout, result.Error!.Code);
        Assert.Equal("compatibility.qualification_timeout", result.Error.DiagnosticCode);
        Assert.Null(fixture.Session.State.ThemeId);
        Assert.True(fixture.Renderer.CleanupCount >= 2);
        Assert.Equal(1, fixture.Discovery.CloseCount);
    }

    [Fact]
    public async Task CallerCancellationRemainsCancelled()
    {
        var fixture = new RuntimeFixture();
        fixture.Renderer.ApplyResults.Enqueue(
            OperationResult<RendererRuntimeResult>.Failure(
                OperationErrorCode.Cancelled,
                "操作已取消。",
                "injector_cancelled"));
        var result = await fixture.Service.ApplyTemporaryAsync(
            CreateTheme(Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.Cancelled, result.Error!.Code);
        Assert.Equal("injector_cancelled", result.Error.DiagnosticCode);
    }

    [Fact]
    public async Task Switch_WhenNewApplyFails_ReappliesPreviousTheme()
    {
        var oldTheme = CreateTheme(Guid.NewGuid());
        var newTheme = CreateTheme(Guid.NewGuid());
        var fixture = new RuntimeFixture(oldTheme);
        fixture.Session.State = AppliedSession(oldTheme.Id);
        fixture.Renderer.ApplyResults.Enqueue(
            OperationResult<RendererRuntimeResult>.Failure(
                OperationErrorCode.ProcessExited,
                "Codex 已退出。",
                "process_exited"));
        fixture.Renderer.ApplyResults.Enqueue(
            OperationResult<RendererRuntimeResult>.Success(
                ActiveRenderer(oldTheme.Id, generation: 8)));

        var result = await fixture.Service.SwitchTemporaryAsync(
            newTheme,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.ProcessExited, result.Error!.Code);
        Assert.Equal(oldTheme.Id, fixture.Session.State.ThemeId);
        Assert.Equal(2, fixture.Renderer.ApplyCount);
        Assert.Equal(0, fixture.Renderer.CleanupCount);
    }

    [Fact]
    public async Task Apply_WhenSessionCommitFails_CleansRuntime()
    {
        var fixture = new RuntimeFixture();
        fixture.Session.WriteError = new OperationError(
            OperationErrorCode.AccessDenied,
            "磁盘只读。",
            "runtime.session.write_access_denied");

        var result = await fixture.Service.ApplyTemporaryAsync(
            CreateTheme(Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.AccessDenied, result.Error!.Code);
        Assert.Equal(1, fixture.Renderer.CleanupCount);
        Assert.Null(fixture.Session.State.ThemeId);
        Assert.Null(fixture.Repository.LastApplyResult);
    }

    [Fact]
    public async Task Apply_WhenInspectorTimesOut_DoesNotRecordTheme()
    {
        var fixture = new RuntimeFixture();
        fixture.Renderer.ApplyResults.Enqueue(
            OperationResult<RendererRuntimeResult>.Failure(
                OperationErrorCode.Timeout,
                "Inspector 操作超时。",
                "injector_timeout"));

        var result = await fixture.Service.ApplyTemporaryAsync(
            CreateTheme(Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.Timeout, result.Error!.Code);
        Assert.Null(fixture.Session.State.ThemeId);
        Assert.Null(fixture.Repository.LastApplyResult);
        Assert.Equal(1, fixture.Discovery.CloseCount);
    }

    [Fact]
    public async Task ConcurrentWrite_ReturnsBusyWithoutSecondMutation()
    {
        var fixture = new RuntimeFixture();
        fixture.Renderer.ApplyGate =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = fixture.Service.ApplyTemporaryAsync(
            CreateTheme(Guid.NewGuid()),
            CancellationToken.None);
        await fixture.Renderer.ApplyEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var second = await fixture.Service.RestoreAsync(CancellationToken.None);

        Assert.False(second.IsSuccess);
        Assert.Equal(OperationErrorCode.Conflict, second.Error!.Code);
        Assert.Equal("runtime.operation_busy", second.Error.DiagnosticCode);
        fixture.Renderer.ApplyGate.SetResult();
        Assert.True((await first).IsSuccess);
    }

    [Fact]
    public async Task Restore_IsIdempotentAndDoesNotDeleteThemeLibrary()
    {
        var theme = CreateTheme(Guid.NewGuid());
        var fixture = new RuntimeFixture(theme);
        fixture.Session.State = AppliedSession(theme.Id);

        var first = await fixture.Service.RestoreAsync(CancellationToken.None);
        var second = await fixture.Service.RestoreAsync(CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(ThemeRuntimeState.Default, fixture.Session.State.State);
        Assert.Equal(RuntimeFixture.Process.ProcessId, fixture.Session.State.CodexProcessId);
        Assert.Equal(RuntimeFixture.Process.StartedAtUtc, fixture.Session.State.CodexStartedAtUtc);
        Assert.Equal(2, fixture.Renderer.CleanupCount);
        Assert.Equal(0, fixture.Repository.DeleteCount);
    }

    [Fact]
    public async Task Restore_RemainsAvailableForUnverifiedOfficialVersion()
    {
        var fixture = new RuntimeFixture();
        fixture.Discovery.InstallationResult =
            OperationResult<CodexInstallationInfo>.Success(
                RuntimeFixture.Installation with { Version = "99.0.0.0" });

        var result = await fixture.Service.RestoreAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, fixture.Renderer.CleanupCount);
        Assert.Equal(ThemeRuntimeState.Default, fixture.Session.State.State);
    }

    [Fact]
    public async Task Restore_WhenCodexIsNotRunningClearsOnlySafeSessionState()
    {
        var theme = CreateTheme(Guid.NewGuid());
        var fixture = new RuntimeFixture(theme);
        fixture.Session.State = AppliedSession(theme.Id);
        fixture.Discovery.Processes = [];

        var result = await fixture.Service.RestoreAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ThemeRuntimeState.Default, result.Value!.State);
        Assert.Equal(ThemeRuntimeState.Default, fixture.Session.State.State);
        Assert.Null(fixture.Session.State.ThemeId);
        Assert.Equal(0, fixture.Renderer.CleanupCount);
        Assert.Equal(0, fixture.Repository.DeleteCount);
    }

    [Fact]
    public async Task Status_ReportsPartialMarkersWithoutClaimingVisibleEffect()
    {
        var theme = CreateTheme(Guid.NewGuid());
        var fixture = new RuntimeFixture(theme);
        fixture.Session.State = AppliedSession(theme.Id);
        fixture.Renderer.StatusResult =
            OperationResult<RendererRuntimeResult>.Success(
                ActiveRenderer(theme.Id) with
                {
                    EligibleWindows = 2,
                    AppliedWindows = 1,
                });

        var result = await fixture.Service.GetStatusAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ThemeRuntimeState.Partial, result.Value!.State);
        Assert.Equal(ThemeRuntimeEvidence.RuntimeMarkers, result.Value.Evidence);
        Assert.NotEqual(ThemeRuntimeEvidence.VisibleEffect, result.Value.Evidence);
        Assert.Equal(1, result.Value.PendingWindows);
    }

    [Fact]
    public async Task Status_WarmCacheAndDefaultSession_UsesOneDiscoveryWithoutInspector()
    {
        var fixture = new RuntimeFixture();
        var cached = await fixture.Qualification.CacheCapabilityAsync(
            RuntimeFixture.CompatibleQualification(),
            CancellationToken.None);

        var result = await fixture.Service.GetStatusAsync(CancellationToken.None);

        Assert.True(cached.IsSuccess, cached.Error?.DiagnosticCode);
        Assert.True(result.IsSuccess, result.Error?.DiagnosticCode);
        Assert.Equal(1, fixture.Discovery.DiscoverCount);
        Assert.Equal(0, fixture.Renderer.StatusCount);
        Assert.Equal("compatibility.cache_hit", result.Value!.CompatibilityDiagnosticCode);
    }

    [Fact]
    public async Task Status_ColdCache_UsesOneDiscoveryAndOneCombinedInspection()
    {
        var fixture = new RuntimeFixture();

        var result = await fixture.Service.GetStatusAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.DiagnosticCode);
        Assert.Equal(1, fixture.Discovery.DiscoverCount);
        Assert.Equal(1, fixture.Renderer.StatusCount);
        Assert.Equal(CodexInspectionMode.Full, fixture.Renderer.LastInspectionMode);
    }

    [Fact]
    public async Task Status_WarmCacheWithActiveSession_UsesRendererOnlyInspection()
    {
        var theme = CreateTheme(Guid.NewGuid());
        var fixture = new RuntimeFixture(theme);
        fixture.Session.State = AppliedSession(theme.Id);
        var cached = await fixture.Qualification.CacheCapabilityAsync(
            RuntimeFixture.CompatibleQualification(),
            CancellationToken.None);

        var result = await fixture.Service.GetStatusAsync(CancellationToken.None);

        Assert.True(cached.IsSuccess, cached.Error?.DiagnosticCode);
        Assert.True(result.IsSuccess, result.Error?.DiagnosticCode);
        Assert.Equal(1, fixture.Discovery.DiscoverCount);
        Assert.Equal(1, fixture.Renderer.StatusCount);
        Assert.Equal(CodexInspectionMode.RendererOnly, fixture.Renderer.LastInspectionMode);
    }

    [Fact]
    public async Task Status_ReportsInspectorResidualAlreadyClosedByCombinedInspection()
    {
        var fixture = new RuntimeFixture();
        fixture.Renderer.StatusResult =
            OperationResult<RendererRuntimeResult>.Success(
                InactiveRenderer() with { InspectorWasAlreadyOpen = true });

        var result = await fixture.Service.GetStatusAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ThemeRuntimeState.InspectorResidual, result.Value!.State);
        Assert.True(result.Value.HasInspectorResidual);
        Assert.Equal(0, fixture.Discovery.CloseCount);
    }

    [Fact]
    public async Task Status_DistinguishesNotInstalledNotRunningAndProbeCompatible()
    {
        var notInstalled = new RuntimeFixture();
        notInstalled.Discovery.InstallationResult =
            OperationResult<CodexInstallationInfo>.Failure(
                OperationErrorCode.CodexNotFound,
                "未安装。",
                "codex_not_installed");
        var notInstalledStatus =
            await notInstalled.Service.GetStatusAsync(CancellationToken.None);

        var notRunning = new RuntimeFixture();
        notRunning.Discovery.InstallationResult =
            OperationResult<CodexInstallationInfo>.Success(
                RuntimeFixture.Installation with
                {
                    ExecutableSha256 = new string('c', 64),
                });
        notRunning.Discovery.Processes = [];
        var notRunningStatus =
            await notRunning.Service.GetStatusAsync(CancellationToken.None);

        var unverified = new RuntimeFixture();
        unverified.Discovery.InstallationResult =
            OperationResult<CodexInstallationInfo>.Success(
                RuntimeFixture.Installation with { Version = "99.0.0.0" });
        var unverifiedStatus =
            await unverified.Service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(ThemeRuntimeState.NotInstalled, notInstalledStatus.Value!.State);
        Assert.False(notInstalledStatus.Value.IsPersistenceEligible);
        Assert.Equal(ThemeRuntimeState.NotRunning, notRunningStatus.Value!.State);
        Assert.False(notRunningStatus.Value.IsPersistenceEligible);
        Assert.Equal(ThemeRuntimeState.Ready, unverifiedStatus.Value!.State);
        Assert.Equal(
            CodexCompatibilityLevel.CompatibleByProbe,
            unverifiedStatus.Value.CompatibilityLevel);
        Assert.Equal(1, unverified.Renderer.StatusCount);
    }

    [Fact]
    public async Task Status_BlocksWhenRequiredCapabilityIsMissing()
    {
        var fixture = new RuntimeFixture();
        fixture.Discovery.InstallationResult =
            OperationResult<CodexInstallationInfo>.Success(
                RuntimeFixture.Installation with { Version = "99.0.0.0" });
        fixture.Discovery.ProbeResult = OperationResult<CodexProbeResult>.Success(
            new CodexProbeResult(
                RuntimeFixture.Process.ProcessId,
                RuntimeFixture.Process.StartedAtUtc,
                "150.0.7871.124",
                1,
                ["main"],
                TimeSpan.Zero,
                ExecuteJavaScriptAvailable: false,
                DiagnosticCode: "capability.execute_javascript_missing"));

        var result = await fixture.Service.GetStatusAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ThemeRuntimeState.Unsupported, result.Value!.State);
        Assert.Equal(CodexCompatibilityLevel.Incompatible, result.Value.CompatibilityLevel);
        Assert.Equal("capability.execute_javascript_missing", result.Value.CompatibilityDiagnosticCode);
        Assert.Equal(1, fixture.Renderer.StatusCount);
        Assert.Null(
            (await fixture.Qualification.ReadLatestAsync(CancellationToken.None)).Value);
    }

    [Fact]
    public async Task Status_AfterRendererReloadReportsRunningUninjected()
    {
        var theme = CreateTheme(Guid.NewGuid());
        var fixture = new RuntimeFixture(theme);
        fixture.Session.State = AppliedSession(theme.Id);
        fixture.Renderer.StatusResult =
            OperationResult<RendererRuntimeResult>.Success(InactiveRenderer());

        var result = await fixture.Service.GetStatusAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ThemeRuntimeState.Ready, result.Value!.State);
        Assert.Equal(ThemeRuntimeEvidence.ProcessOnly, result.Value.Evidence);
    }

    [Fact]
    public async Task AtomicSessionStore_RoundTripsAndReplacesState()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cts-session-{Guid.NewGuid():N}");
        try
        {
            var store = new AtomicCurrentSessionStore(root);
            var first = AppliedSession(Guid.NewGuid());
            var second = AppliedSession(Guid.NewGuid());

            Assert.True((await store.WriteAsync(first, CancellationToken.None)).IsSuccess);
            Assert.True((await store.WriteAsync(second, CancellationToken.None)).IsSuccess);
            var read = await store.ReadAsync(CancellationToken.None);

            Assert.True(read.IsSuccess);
            Assert.Equal(second, read.Value);
            Assert.Empty(Directory.EnumerateFiles(
                Path.Combine(root, "runtime"),
                "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ThemePackage CreateTheme(Guid id) =>
        new(
            1,
            id,
            $"Theme {id:N}",
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

    private static RuntimeSessionState AppliedSession(Guid themeId) =>
        new(
            RuntimeSessionState.CurrentSchemaVersion,
            ThemeRuntimeState.Temporary,
            themeId,
            RuntimeFixture.Process.ProcessId,
            RuntimeFixture.Process.StartedAtUtc,
            1,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

    private static RendererRuntimeResult ActiveRenderer(
        Guid themeId,
        int generation = 1) =>
        new(
            1,
            true,
            generation,
            themeId,
            1,
            1,
            1,
            2,
            ["home"],
            0,
            RuntimeFixture.Process.ProcessId);

    private static RendererRuntimeResult InactiveRenderer() =>
        new(1, false, null, null, 0, 0, 1, 0);

    private sealed class RuntimeFixture
    {
        public static readonly CodexInstallationInfo Installation =
            new(
                "OpenAI.Codex_2p2nqsd0c76g0",
                "OpenAI.Codex_26.715.4045.0_x64__2p2nqsd0c76g0",
                "26.715.4045.0",
                @"C:\Program Files\WindowsApps\OpenAI.Codex\ChatGPT.exe",
                ExecutableSha256: new string('a', 64));

        public static CodexCompatibilityQualificationStore.QualificationRecord
            CompatibleQualification() =>
            new(
                Installation.ExecutableSha256,
                Installation.Version,
                Installation.PackageFullName,
                Installation.ExecutablePath,
                Installation.Source,
                Installation.IdentityAssessment,
                CodexCompatibilityLevel.Verified,
                CodexCompatibilityQualificationStore.CurrentProbeContractVersion,
                CodexCompatibilityQualificationStore.CurrentRequiredCapabilitiesVersion,
                DateTimeOffset.Parse("2026-07-24T06:00:00Z"),
                true,
                true,
                false);

        public static readonly CodexProcessInfo Process =
            new(
                1234,
                new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero),
                Installation.ExecutablePath,
                null);

        public RuntimeFixture(params ThemePackage[] themes)
            : this(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(15),
                themes)
        {
        }

        public RuntimeFixture(
            TimeSpan operationTimeout,
            TimeSpan qualificationTimeout,
            params ThemePackage[] themes)
        {
            Renderer = new FakeRenderer(Events, Discovery);
            Repository = new FakeRepository(Events);
            Session = new FakeSessionStore(Events);
            foreach (var theme in themes)
            {
                Repository.Themes[theme.Id] = theme;
            }

            Qualification = CodexCompatibilityQualificationStore.CreateInMemory(
                Installation.ExecutableSha256);
            Service = new CodexThemeRuntimeService(
                Discovery,
                Discovery,
                Renderer,
                Assets,
                Repository,
                Session,
                qualificationStore: Qualification,
                operationTimeout: operationTimeout,
                qualificationTimeout: qualificationTimeout);
        }

        public List<string> Events { get; } = [];

        public FakeDiscovery Discovery { get; } = new();

        public FakeRenderer Renderer { get; }

        public FakeAssets Assets { get; } = new();

        public FakeRepository Repository { get; }

        public FakeSessionStore Session { get; }

        public CodexThemeRuntimeService Service { get; }

        public CodexCompatibilityQualificationStore Qualification { get; }
    }

    private sealed class FakeDiscovery :
        ICodexDiscoveryService,
        ICodexInspectorService
    {
        public OperationResult<CodexInstallationInfo> InstallationResult { get; set; } =
            OperationResult<CodexInstallationInfo>.Success(RuntimeFixture.Installation);

        public IReadOnlyList<CodexProcessInfo> Processes { get; set; } =
            [RuntimeFixture.Process];

        public int CloseCount { get; private set; }

        public OperationResult<CodexProbeResult> ProbeResult { get; set; } =
            OperationResult<CodexProbeResult>.Success(
                new CodexProbeResult(
                    RuntimeFixture.Process.ProcessId,
                    RuntimeFixture.Process.StartedAtUtc,
                    "150.0.7871.124",
                    1,
                    ["main"],
                    TimeSpan.Zero));

        public int DiscoverCount { get; private set; }

        public Task<OperationResult<CodexDiscoverySnapshot>> DiscoverAsync(
            CancellationToken cancellationToken)
        {
            DiscoverCount++;
            return Task.FromResult(
                InstallationResult.IsSuccess
                    ? OperationResult<CodexDiscoverySnapshot>.Success(
                        new CodexDiscoverySnapshot(
                            InstallationResult.Value!,
                            Processes,
                            DateTimeOffset.UtcNow))
                    : OperationResult<CodexDiscoverySnapshot>.Failure(
                        InstallationResult.Error!));
        }

        public Task<OperationResult<CodexProbeResult>> ProbeAsync(
            CodexProcessInfo process,
            CancellationToken cancellationToken) =>
            Task.FromResult(ProbeResult);

        public Task<OperationResult> CloseInspectorAsync(
            CodexProcessInfo process,
            CancellationToken cancellationToken)
        {
            CloseCount++;
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class FakeRenderer(
        List<string> events,
        FakeDiscovery discovery) : IInjectorRendererClient
    {
        public Queue<OperationResult<RendererRuntimeResult>> ApplyResults { get; } = [];

        public OperationResult<RendererRuntimeResult> StatusResult { get; set; } =
            OperationResult<RendererRuntimeResult>.Success(InactiveRenderer());

        public TaskCompletionSource ApplyEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource? ApplyGate { get; set; }

        public TimeSpan ApplyDelay { get; set; }

        public TimeSpan CleanupDelay { get; set; }

        public int ApplyCount { get; private set; }

        public int CleanupCount { get; private set; }

        public int StatusCount { get; private set; }

        public CodexInspectionMode? LastInspectionMode { get; private set; }

        public async Task<OperationResult<RendererRuntimeResult>> ApplyAsync(
            CodexProcessInfo process,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            ApplyCount++;
            events.Add("renderer.apply");
            ApplyEntered.TrySetResult();
            if (ApplyGate is not null)
            {
                await ApplyGate.Task.WaitAsync(cancellationToken);
            }
            if (ApplyDelay > TimeSpan.Zero)
            {
                await Task.Delay(ApplyDelay, cancellationToken);
            }

            return ApplyResults.Count > 0
                ? ApplyResults.Dequeue()
                : OperationResult<RendererRuntimeResult>.Success(
                    ActiveRenderer(ReadThemeId(payload)));
        }

        public Task<OperationResult<RendererRuntimeResult>> GetStatusAsync(
            CodexProcessInfo process,
            CancellationToken cancellationToken)
        {
            StatusCount++;
            return Task.FromResult(StatusResult);
        }

        public async Task<OperationResult<RendererRuntimeResult>> CleanupAsync(
            CodexProcessInfo process,
            CancellationToken cancellationToken)
        {
            CleanupCount++;
            if (CleanupDelay > TimeSpan.Zero)
            {
                await Task.Delay(CleanupDelay, cancellationToken);
            }

            return OperationResult<RendererRuntimeResult>.Success(InactiveRenderer());
        }

        public Task<OperationResult<CodexInspectionResult>> InspectAsync(
            CodexProcessInfo process,
            CodexInspectionMode mode,
            CancellationToken cancellationToken)
        {
            StatusCount++;
            LastInspectionMode = mode;
            if (mode == CodexInspectionMode.Full && !discovery.ProbeResult.IsSuccess)
            {
                return Task.FromResult(
                    OperationResult<CodexInspectionResult>.Failure(
                        discovery.ProbeResult.Error!));
            }

            return Task.FromResult(
                StatusResult.IsSuccess
                    ? OperationResult<CodexInspectionResult>.Success(
                        new CodexInspectionResult(
                            mode == CodexInspectionMode.Full
                                ? discovery.ProbeResult.Value
                                : null,
                            StatusResult.Value!))
                    : OperationResult<CodexInspectionResult>.Failure(
                        StatusResult.Error!));
        }

        private static Guid ReadThemeId(ReadOnlyMemory<byte> payload)
        {
            using var document = System.Text.Json.JsonDocument.Parse(payload);
            return document.RootElement
                .GetProperty("theme")
                .GetProperty("id")
                .GetGuid();
        }
    }

    private sealed class FakeAssets : IThemeAssetStore
    {
        private static readonly byte[] Png =
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

    private sealed class FakeSessionStore(List<string> events) : ICurrentSessionStore
    {
        public RuntimeSessionState State { get; set; } =
            RuntimeSessionState.Default(Guid.Empty, DateTimeOffset.UnixEpoch);

        public OperationError? WriteError { get; set; }

        public Task<OperationResult<RuntimeSessionState>> ReadAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<RuntimeSessionState>.Success(State));

        public Task<OperationResult> WriteAsync(
            RuntimeSessionState state,
            CancellationToken cancellationToken)
        {
            events.Add("session.write");
            if (WriteError is not null)
            {
                var error = WriteError;
                WriteError = null;
                return Task.FromResult(OperationResult.Failure(error));
            }

            State = state;
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class FakeRepository(List<string> events) : IThemeRepository
    {
        public Dictionary<Guid, ThemePackage> Themes { get; } = [];

        public ThemeApplyResult? LastApplyResult { get; private set; }

        public int DeleteCount { get; private set; }

        public Task<OperationResult<ThemePackage>> GetAsync(
            Guid themeId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Themes.TryGetValue(themeId, out var theme)
                    ? OperationResult<ThemePackage>.Success(theme)
                    : OperationResult<ThemePackage>.Failure(
                        OperationErrorCode.NotFound,
                        "主题不存在。",
                        "theme.not_found"));

        public Task<OperationResult> RecordApplyResultAsync(
            Guid themeId,
            ThemeApplyResult result,
            string? userMessage,
            CancellationToken cancellationToken)
        {
            events.Add("repository.record");
            LastApplyResult = result;
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> DeleteAsync(
            Guid themeId,
            CancellationToken cancellationToken)
        {
            DeleteCount++;
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

        public Task<OperationResult> RestoreDeletedAsync(
            Guid themeId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult> PermanentlyDeleteAsync(
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

        public Task<OperationResult> SetCurrentPersistentAsync(
            Guid? themeId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
