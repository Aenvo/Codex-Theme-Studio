using System.Text.Json;
using CodexThemeStudio.Application.MacOS;
using CodexThemeStudio.CodexRuntime;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;
using CodexThemeStudio.MacOS.AcceptanceHarness;

namespace CodexThemeStudio.Application.MacOS.Tests;

public sealed class MacProductApplicationServiceTests
{
    [Fact]
    public async Task QualificationCycleUsesCoordinatorAndReusesInProcessQualification()
    {
        var bridge = new FakeBridge();
        var service = CreateService(bridge);

        var result = await service.RunQualificationCycleAsync(
            Guid.NewGuid(),
            CreateTheme(),
            CancellationToken.None);

        Assert.Equal("ok", result.Status);
        Assert.True(result.QualificationEstablishedWithinRun);
        Assert.True(result.FirstQualificationApplied);
        Assert.True(result.FirstRestoreVerified);
        Assert.True(result.SecondTemporaryApplyVerified);
        Assert.True(result.SecondRestoreVerified);
        Assert.True(result.ProcessStable);
        Assert.Equal("stable", result.ProcessProof);
        Assert.True(result.CleanupVerified);
        Assert.Equal(0, result.FinalResidualCount);
        Assert.Equal("verified-zero", result.ResidualProof);
        Assert.Equal(0, result.FinalPortListenerCount);
        Assert.Equal("verified-zero", result.PortProof);
        Assert.Null(result.Error);
        Assert.Null(result.RecoveryError);
        Assert.Equal(
            [
                CodexPlatformCommand.Discover,
                CodexPlatformCommand.Discover,
                CodexPlatformCommand.QualifyAndApply,
                CodexPlatformCommand.Cleanup,
                CodexPlatformCommand.Discover,
                CodexPlatformCommand.ApplyTemporary,
                CodexPlatformCommand.Cleanup,
            ],
            bridge.Commands);
    }

    [Fact]
    public async Task FailedApplyUsesOneIndependentFinalCleanup()
    {
        var bridge = new FakeBridge
        {
            FailCommand = CodexPlatformCommand.QualifyAndApply,
        };
        var service = CreateService(bridge);
        using var cancelledCaller = new CancellationTokenSource();

        var result = await service.RunQualificationCycleAsync(
            Guid.NewGuid(),
            CreateTheme(),
            cancelledCaller.Token);

        Assert.Equal("error", result.Status);
        Assert.True(result.CleanupAttempted);
        Assert.True(result.CleanupVerified);
        Assert.True(result.ProcessStable);
        Assert.Equal("stable", result.ProcessProof);
        Assert.Equal(0, result.FinalResidualCount);
        Assert.Equal("verified-zero", result.ResidualProof);
        Assert.Equal(0, result.FinalPortListenerCount);
        Assert.Equal("verified-zero", result.PortProof);
        Assert.Equal("helper.failed", result.Error!.Code);
        Assert.Null(result.RecoveryError);
        Assert.Equal(1, bridge.Commands.Count(
            command => command == CodexPlatformCommand.QualifyAndApply));
        Assert.DoesNotContain(
            CodexPlatformCommand.ApplyTemporary,
            bridge.Commands);
        Assert.Equal(1, bridge.Commands.Count(
            command => command == CodexPlatformCommand.Cleanup));
    }

    [Fact]
    public async Task CancellationAfterStatefulRequestUsesIndependentCleanup()
    {
        var bridge = new FakeBridge
        {
            CancelCommand = CodexPlatformCommand.QualifyAndApply,
        };

        var result = await CreateService(bridge).RunQualificationCycleAsync(
            Guid.NewGuid(),
            CreateTheme(),
            CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.True(result.CleanupAttempted);
        Assert.True(result.CleanupVerified);
        Assert.Equal("operation.cancelled", result.Error!.Code);
        Assert.Null(result.RecoveryError);
        Assert.True(result.ProcessStable);
        Assert.Equal("stable", result.ProcessProof);
        Assert.Equal(1, bridge.Commands.Count(
            command => command == CodexPlatformCommand.Cleanup));
    }

    [Fact]
    public async Task FailedRestoreGetsOnlyOneFinalCleanupAndRemainsUnverified()
    {
        var bridge = new FakeBridge { FailCleanup = true };

        var result = await CreateService(bridge).RunQualificationCycleAsync(
            Guid.NewGuid(),
            CreateTheme(),
            CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.True(result.CleanupAttempted);
        Assert.False(result.CleanupVerified);
        Assert.Null(result.ProcessStable);
        Assert.Equal("unverified", result.ProcessProof);
        Assert.Null(result.FinalResidualCount);
        Assert.Equal("unverified", result.ResidualProof);
        Assert.Null(result.FinalPortListenerCount);
        Assert.Equal("unverified", result.PortProof);
        Assert.Equal("renderer.cleanup_failed", result.Error!.Code);
        Assert.Equal(
            "renderer.cleanup_failed",
            result.RecoveryError!.Code);
        Assert.Equal(2, bridge.Commands.Count(
            command => command == CodexPlatformCommand.Cleanup));
    }

    [Fact]
    public async Task FailedApplyAndRecoveryKeepBothErrorsAndUnknownProofs()
    {
        var bridge = new FakeBridge
        {
            FailCommand = CodexPlatformCommand.QualifyAndApply,
            FailCleanup = true,
        };

        var result = await CreateService(bridge).RunQualificationCycleAsync(
            Guid.NewGuid(),
            CreateTheme(),
            CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.Equal("helper.failed", result.Error!.Code);
        Assert.Equal("first-apply", result.Error.Stage);
        Assert.Equal(
            "renderer.cleanup_failed",
            result.RecoveryError!.Code);
        Assert.Equal("final-cleanup", result.RecoveryError.Stage);
        Assert.True(result.CleanupAttempted);
        Assert.False(result.CleanupVerified);
        Assert.Null(result.ProcessStable);
        Assert.Equal("unverified", result.ProcessProof);
        Assert.Null(result.FinalResidualCount);
        Assert.Equal("unverified", result.ResidualProof);
        Assert.Null(result.FinalPortListenerCount);
        Assert.Equal("unverified", result.PortProof);
        Assert.DoesNotContain(
            CodexPlatformCommand.ApplyTemporary,
            bridge.Commands);
    }

    [Fact]
    public async Task IdentityChangeFailsClosedAndCleansUp()
    {
        var bridge = new FakeBridge
        {
            ChangeProcessOnCall = 3,
        };
        var result = await CreateService(bridge).RunQualificationCycleAsync(
            Guid.NewGuid(),
            CreateTheme(),
            CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.False(result.ProcessStable);
        Assert.Equal("changed", result.ProcessProof);
        Assert.True(result.CleanupAttempted);
        Assert.True(result.CleanupVerified);
        Assert.Equal("process.identity_changed", result.Error!.Code);
        Assert.Null(result.RecoveryError);
    }

    [Fact]
    public async Task ConcurrentCycleReturnsBusyWithoutStartingSecondOperation()
    {
        var bridge = new FakeBridge { PauseFirstCall = true };
        var service = CreateService(bridge);
        var first = service.RunQualificationCycleAsync(
            Guid.NewGuid(),
            CreateTheme(),
            CancellationToken.None);
        await bridge.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var second = await service.RunQualificationCycleAsync(
            Guid.NewGuid(),
            CreateTheme(),
            CancellationToken.None);
        bridge.ReleaseFirstCall.SetResult();
        _ = await first;

        Assert.Equal("error", second.Status);
        Assert.Equal("operation.busy", second.Error!.Code);
    }

    [Fact]
    public async Task InvalidThemeNeverCallsBridge()
    {
        var bridge = new FakeBridge();
        var invalid = CreateTheme() with
        {
            Palette = CreateTheme().Palette with { Accent = "red" },
        };

        var result = await CreateService(bridge).RunQualificationCycleAsync(
            Guid.NewGuid(),
            invalid,
            CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.Empty(bridge.Commands);
    }

    [Fact]
    public void ProtocolRejectsUnknownAndDangerousFields()
    {
        var valid = CreateRequestJson();
        var unknown = valid.Replace(
            "\"theme\":",
            "\"css\":\"body{}\",\"theme\":",
            StringComparison.Ordinal);

        var failure = Assert.Throws<AcceptanceProtocolException>(
            () => AcceptanceProtocol.Decode(
                System.Text.Encoding.UTF8.GetBytes(unknown),
                DateTimeOffset.Parse("2026-07-27T00:00:00Z")));

        Assert.Equal("protocol.request_invalid", failure.Code);
    }

    [Fact]
    public void ProtocolRejectsExpiredAuthorization()
    {
        var failure = Assert.Throws<AcceptanceProtocolException>(
            () => AcceptanceProtocol.Decode(
                System.Text.Encoding.UTF8.GetBytes(CreateRequestJson()),
                DateTimeOffset.Parse("2026-07-27T00:10:00Z")));

        Assert.Equal("authorization.live_required", failure.Code);
    }

    [Fact]
    public void ProtocolRejectsNonDeclarativePaletteValue()
    {
        var invalid = CreateRequestJson().Replace(
            "\"#2563EB\"",
            "\"var(--accent)\"",
            StringComparison.Ordinal);

        var failure = Assert.Throws<AcceptanceProtocolException>(
            () => AcceptanceProtocol.Decode(
                System.Text.Encoding.UTF8.GetBytes(invalid),
                DateTimeOffset.Parse("2026-07-27T00:00:00Z")));

        Assert.Equal("request.theme_invalid", failure.Code);
    }

    [Fact]
    public async Task SourceCompositionFailsClosedBeforeCreatingRuntime()
    {
        var result = await MacProductCompositionRoot.CreateAsync(
            AppContext.BaseDirectory,
            new string('a', 64),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Service);
    }

    [Theory]
    [InlineData(
        "protocol.request_invalid",
        "request",
        false)]
    [InlineData(
        "runtime.identity_verification_failed",
        "identity",
        false)]
    [InlineData(
        "operation.unexpected",
        "harness",
        false)]
    public void EntryFailureUsesOnlyUnverifiedFinalEvidence(
        string code,
        string stage,
        bool runtimeIdentityVerified)
    {
        var result = MacQualificationCycleResult.CreateUnverifiedFailure(
            MacProductApplicationService.ToolVersion,
            Guid.NewGuid(),
            runtimeIdentityVerified,
            code,
            stage);

        Assert.Equal("0.1.2", result.ToolVersion);
        Assert.Equal("error", result.Status);
        Assert.Equal(runtimeIdentityVerified, result.RuntimeIdentityVerified);
        Assert.Null(result.ProcessStable);
        Assert.Equal("unverified", result.ProcessProof);
        Assert.Null(result.FinalResidualCount);
        Assert.Equal("unverified", result.ResidualProof);
        Assert.Null(result.FinalPortListenerCount);
        Assert.Equal("unverified", result.PortProof);
        Assert.Equal(code, result.Error!.Code);
        Assert.Equal(stage, result.Error.Stage);
        Assert.Null(result.RecoveryError);

        var json = JsonSerializer.Serialize(
            result,
            AcceptanceProtocol.JsonOptions);
        Assert.Contains("\"processStable\":null", json);
        Assert.Contains("\"finalResidualCount\":null", json);
        Assert.Contains("\"finalPortListenerCount\":null", json);
        Assert.DoesNotContain(
            "\"finalResidualCount\":-1",
            json,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\"finalPortListenerCount\":-1",
            json,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task StructuredResultContainsNoPrivateRuntimeFields()
    {
        var result = await CreateService(new FakeBridge())
            .RunQualificationCycleAsync(
                Guid.NewGuid(),
                CreateTheme(),
                CancellationToken.None);
        var json = JsonSerializer.Serialize(result);
        foreach (var forbidden in new[]
                 {
                     "\"pid\"",
                     "targetId",
                     "webSocket",
                     "\"url\"",
                     "\"dom\"",
                     "\"css\"",
                     "commandLine",
                     "environment",
                     "credential",
                     "conversation",
                 })
        {
            Assert.DoesNotContain(
                forbidden,
                json,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private static MacProductApplicationService CreateService(
        ICodexPlatformBridge bridge) =>
        new(new CodexRuntimeCoordinator(
            bridge,
            new InMemoryCodexQualificationStore()));

    private static MacThemeInput CreateTheme() =>
        new(
            1,
            Guid.Parse("5be08c24-d21f-4db0-bdb0-d6d4cc779d7b"),
            "dark",
            new MacPaletteInput(
                "#111111",
                "#181818",
                "#F5F5F5",
                "#A3A3A3",
                "#2563EB",
                "#303030"));

    private static string CreateRequestJson() =>
        """
        {
          "schemaVersion": 1,
          "requestId": "5be08c24-d21f-4db0-bdb0-d6d4cc779d7b",
          "operation": "qualification-cycle",
          "deadlineMilliseconds": 120000,
          "authorization": {
            "purpose": "stage-7b.2b",
            "acknowledgeStatefulInspector": true,
            "acknowledgeTemporaryTheme": true,
            "expiresAtUtc": "2026-07-27T00:04:00Z",
            "stagingAssemblyId": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
          },
          "theme": {
            "schemaVersion": 1,
            "themeId": "5be08c24-d21f-4db0-bdb0-d6d4cc779d7b",
            "variant": "dark",
            "palette": {
              "background": "#111111",
              "surface": "#181818",
              "foreground": "#F5F5F5",
              "muted": "#A3A3A3",
              "accent": "#2563EB",
              "border": "#303030"
            }
          }
        }
        """;

    private sealed class FakeBridge : ICodexPlatformBridge
    {
        private int callCount;

        public List<CodexPlatformCommand> Commands { get; } = [];
        public CodexPlatformCommand? FailCommand { get; init; }
        public CodexPlatformCommand? CancelCommand { get; init; }
        public bool FailCleanup { get; init; }
        public int ChangeProcessOnCall { get; init; }
        public bool PauseFirstCall { get; init; }
        public TaskCompletionSource FirstCallStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstCall { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<OperationResult<CodexPlatformResponse>> ExecuteAsync(
            CodexPlatformRequest request,
            CancellationToken cancellationToken)
        {
            callCount++;
            Commands.Add(request.Command);
            if (PauseFirstCall && callCount == 1)
            {
                FirstCallStarted.SetResult();
                await ReleaseFirstCall.Task.WaitAsync(cancellationToken);
            }
            if (request.Command == FailCommand)
            {
                return OperationResult<CodexPlatformResponse>.Failure(
                    OperationErrorCode.ExternalToolFailure,
                    "fixture",
                    "helper.failed");
            }
            if (request.Command == CancelCommand)
            {
                throw new OperationCanceledException();
            }
            if (FailCleanup &&
                request.Command == CodexPlatformCommand.Cleanup)
            {
                return OperationResult<CodexPlatformResponse>.Failure(
                    OperationErrorCode.InvalidResponse,
                    "fixture",
                    "renderer.cleanup_failed");
            }

            var cleanup = request.Command == CodexPlatformCommand.Cleanup;
            var discovery = request.Command == CodexPlatformCommand.Discover;
            var changed = callCount == ChangeProcessOnCall;
            return OperationResult<CodexPlatformResponse>.Success(
                new CodexPlatformResponse(
                    1,
                    1,
                    "0.2.0",
                    request.RequestId,
                    true,
                    Installation(),
                    Process(changed),
                    new CodexRuntimeCapabilities(
                        true,
                        false,
                        true,
                        true,
                        true),
                    cleanup
                        ? CodexCleanupDisposition.Verified
                        : CodexCleanupDisposition.NotNeeded,
                    discovery
                        ? CodexInspectorDisposition.NotOpened
                        : CodexInspectorDisposition.Closed,
                    discovery ? 0 : 1,
                    request.Command is
                        CodexPlatformCommand.ApplyTemporary or
                        CodexPlatformCommand.QualifyAndApply
                            ? 1
                            : 0,
                    0,
                    0,
                    null));
        }

        private static CodexInstallationIdentityV2 Installation() =>
            new(
                CodexHostPlatform.MacOS,
                "com.openai.codex",
                "2DC432GLL2",
                "1",
                "1",
                "/Applications/ChatGPT.app/Contents/MacOS/ChatGPT",
                new string('a', 64),
                true,
                true,
                true);

        private static CodexProcessIdentityV2 Process(bool changed) =>
            new(
                42,
                1,
                changed
                    ? DateTimeOffset.Parse("2026-01-02T00:00:00Z")
                    : DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                "/Applications/ChatGPT.app/Contents/MacOS/ChatGPT",
                "arm64");
    }
}
