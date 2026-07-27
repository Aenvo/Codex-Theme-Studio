using CodexThemeStudio.CodexRuntime;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.CodexRuntime.Tests;

public sealed class CodexRuntimeCoordinatorTests
{
    [Fact]
    public async Task FirstApplyQualifiesThenSubsequentApplyUsesSingleSessionCommand()
    {
        var bridge = new FakeBridge();
        var coordinator = new CodexRuntimeCoordinator(
            bridge,
            new InMemoryCodexQualificationStore());
        var theme = CreateTheme();

        var first = await coordinator.ApplyTemporaryAsync(theme, CancellationToken.None);
        var second = await coordinator.ApplyTemporaryAsync(theme, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(
            [
                CodexPlatformCommand.Discover,
                CodexPlatformCommand.QualifyAndApply,
                CodexPlatformCommand.Discover,
                CodexPlatformCommand.ApplyTemporary,
            ],
            bridge.Commands);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(11)]
    public async Task ApplyRejectsUnexpectedManagedResourceCountAndDoesNotQualify(
        int residualCount)
    {
        var bridge = new FakeBridge { ApplyResidualCount = residualCount };
        var qualification = new FakeQualificationStore();
        var coordinator = new CodexRuntimeCoordinator(bridge, qualification);

        var result = await coordinator.ApplyTemporaryAsync(
            CreateTheme(),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.InvalidResponse, result.Error!.Code);
        Assert.Equal(0, qualification.WriteCount);
    }

    [Fact]
    public async Task InspectRuntimeRequiresZeroResidueAndClosedInspector()
    {
        var valid = await new CodexRuntimeCoordinator(
            new FakeBridge(),
            new InMemoryCodexQualificationStore())
            .InspectRuntimeAsync(CancellationToken.None);
        var residue = await new CodexRuntimeCoordinator(
            new FakeBridge { InspectResidualCount = 1 },
            new InMemoryCodexQualificationStore())
            .InspectRuntimeAsync(CancellationToken.None);
        var open = await new CodexRuntimeCoordinator(
            new FakeBridge
            {
                InspectInspectorDisposition =
                    CodexInspectorDisposition.Residual,
            },
            new InMemoryCodexQualificationStore())
            .InspectRuntimeAsync(CancellationToken.None);

        Assert.True(valid.IsSuccess);
        Assert.False(residue.IsSuccess);
        Assert.False(open.IsSuccess);
    }

    [Fact]
    public async Task UntrustedDiscoveryNeverStartsStatefulCommand()
    {
        var bridge = new FakeBridge
        {
            PublisherIdentifier = "UNKNOWN",
        };
        var coordinator = new CodexRuntimeCoordinator(
            bridge,
            new InMemoryCodexQualificationStore());

        var result = await coordinator.ApplyTemporaryAsync(
            CreateTheme(),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.CodexIdentityMismatch, result.Error!.Code);
        Assert.Equal([CodexPlatformCommand.Discover], bridge.Commands);
    }

    [Fact]
    public async Task IncompleteCleanupProofFailsClosed()
    {
        var bridge = new FakeBridge
        {
            CleanupResidualCount = 1,
        };
        var coordinator = new CodexRuntimeCoordinator(
            bridge,
            new InMemoryCodexQualificationStore());

        var result = await coordinator.RestoreAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.InvalidResponse, result.Error!.Code);
    }

    [Fact]
    public async Task InspectorResidualFailsCleanupProof()
    {
        var bridge = new FakeBridge
        {
            CleanupInspectorDisposition = CodexInspectorDisposition.Residual,
        };
        var coordinator = new CodexRuntimeCoordinator(
            bridge,
            new InMemoryCodexQualificationStore());

        var result = await coordinator.RestoreAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.InvalidResponse, result.Error!.Code);
    }

    [Fact]
    public async Task PreCancelledOperationIsStructuredAndNeverCallsBridge()
    {
        var bridge = new FakeBridge();
        var coordinator = new CodexRuntimeCoordinator(
            bridge,
            new InMemoryCodexQualificationStore());
        using var source = new CancellationTokenSource();
        source.Cancel();

        var result = await coordinator.ApplyTemporaryAsync(
            CreateTheme(),
            source.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.Cancelled, result.Error!.Code);
        Assert.Empty(bridge.Commands);
    }

    [Fact]
    public void ThemeProjectionExcludesArtAndArbitraryCss()
    {
        var projection = CodexRuntimeCoordinator.ProjectTheme(CreateTheme());
        var json = System.Text.Json.JsonSerializer.Serialize(projection);

        Assert.DoesNotContain("art", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("css", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#111111", json, StringComparison.Ordinal);
    }

    private static ThemePackage CreateTheme() =>
        new(
            1,
            Guid.Parse("5be08c24-d21f-4db0-bdb0-d6d4cc779d7b"),
            "Fixture",
            ThemeVariant.Dark,
            new ThemePalette(
                "#111111",
                "#181818",
                "#2563EB",
                "#F5F5F5",
                "#A3A3A3",
                "#303030"),
            new ThemeArt(
                "art/fixture.png",
                0.5,
                0.5,
                ThemeSafeArea.Auto,
                ThemeArtSize.Cover,
                0.7,
                0.2,
                ThemeTaskMode.Hidden,
                0,
                0,
                0));

    private sealed class FakeBridge : ICodexPlatformBridge
    {
        public List<CodexPlatformCommand> Commands { get; } = [];

        public string PublisherIdentifier { get; init; } = "2DC432GLL2";

        public int CleanupResidualCount { get; init; }
        public int ApplyResidualCount { get; init; } =
            CodexRuntimeCoordinator.ExpectedAppliedManagedResourceCount;
        public int InspectResidualCount { get; init; }

        public CodexInspectorDisposition CleanupInspectorDisposition { get; init; } =
            CodexInspectorDisposition.Closed;
        public CodexInspectorDisposition InspectInspectorDisposition { get; init; } =
            CodexInspectorDisposition.Closed;

        public Task<OperationResult<CodexPlatformResponse>> ExecuteAsync(
            CodexPlatformRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(request.Command);
            var isCleanup = request.Command == CodexPlatformCommand.Cleanup;
            var isInspect =
                request.Command == CodexPlatformCommand.InspectRuntime;
            var response = new CodexPlatformResponse(
                1,
                1,
                "0.1.0",
                request.RequestId,
                true,
                CreateInstallation(PublisherIdentifier),
                CreateProcess(),
                new CodexRuntimeCapabilities(true, false, true, true, true),
                isCleanup
                    ? CodexCleanupDisposition.Verified
                    : CodexCleanupDisposition.NotNeeded,
                request.Command == CodexPlatformCommand.Discover
                    ? CodexInspectorDisposition.NotOpened
                    : isInspect
                        ? InspectInspectorDisposition
                        : isCleanup
                        ? CleanupInspectorDisposition
                        : CodexInspectorDisposition.Closed,
                request.Command == CodexPlatformCommand.Discover ? 0 : 1,
                request.Command is
                    CodexPlatformCommand.ApplyTemporary or
                    CodexPlatformCommand.QualifyAndApply
                    ? 1
                    : 0,
                isCleanup
                    ? CleanupResidualCount
                    : isInspect
                        ? InspectResidualCount
                        : request.Command == CodexPlatformCommand.Discover
                            ? 0
                            : ApplyResidualCount,
                0,
                null);
            return Task.FromResult(OperationResult<CodexPlatformResponse>.Success(response));
        }

        private static CodexInstallationIdentityV2 CreateInstallation(string publisher) =>
            new(
                CodexHostPlatform.MacOS,
                "com.openai.codex",
                publisher,
                "1.0",
                "1",
                "/Applications/ChatGPT.app/Contents/MacOS/ChatGPT",
                new string('A', 64),
                true,
                true,
                true);

        private static CodexProcessIdentityV2 CreateProcess() =>
            new(
                42,
                1,
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                "/Applications/ChatGPT.app/Contents/MacOS/ChatGPT",
                "arm64");
    }

    private sealed class FakeQualificationStore : ICodexQualificationStore
    {
        public int WriteCount { get; private set; }

        public Task<bool> IsQualifiedAsync(
            string installationFingerprint,
            int protocolVersion,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task WriteQualifiedAsync(
            string installationFingerprint,
            int protocolVersion,
            string toolVersion,
            CancellationToken cancellationToken)
        {
            WriteCount++;
            return Task.CompletedTask;
        }
    }
}
