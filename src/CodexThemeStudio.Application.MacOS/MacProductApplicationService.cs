using CodexThemeStudio.CodexRuntime;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Application.MacOS;

public sealed class MacProductApplicationService
{
    public const string ToolVersion = "0.1.0";
    public static readonly TimeSpan CleanupGrace = TimeSpan.FromSeconds(25);

    private readonly CodexRuntimeCoordinator coordinator;
    private readonly SemaphoreSlim cycleLock = new(1, 1);

    internal MacProductApplicationService(CodexRuntimeCoordinator coordinator)
    {
        this.coordinator =
            coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public Task<OperationResult<CodexPlatformResponse>> DiscoverAsync(
        CancellationToken cancellationToken) =>
        coordinator.DiscoverAsync(cancellationToken);

    public Task<OperationResult<CodexPlatformResponse>> ApplyTemporaryAsync(
        ThemePackage theme,
        CancellationToken cancellationToken) =>
        coordinator.ApplyTemporaryAsync(theme, cancellationToken);

    public Task<OperationResult<CodexPlatformResponse>> RestoreAsync(
        CancellationToken cancellationToken) =>
        coordinator.RestoreAsync(cancellationToken);

    public async Task<MacQualificationCycleResult> RunQualificationCycleAsync(
        Guid requestId,
        MacThemeInput theme,
        CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty ||
            theme is null ||
            !theme.TryCreatePackage(out var package))
        {
            return Failed(
                requestId,
                "request.theme_invalid",
                "request");
        }

        bool acquired;
        try
        {
            acquired = await cycleLock.WaitAsync(0, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Failed(requestId, "operation.cancelled", "request");
        }

        if (!acquired)
        {
            return Failed(requestId, "operation.busy", "request");
        }

        var progress = new CycleProgress(requestId);
        try
        {
            await ExecuteCycleCoreAsync(
                progress,
                package!,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            progress.Fail("operation.cancelled", "operation");
        }
        finally
        {
            if (progress.CleanupRequired)
            {
                progress.CleanupAttempted = true;
                using var cleanup = new CancellationTokenSource(CleanupGrace);
                try
                {
                    var result = await coordinator.RestoreAsync(cleanup.Token);
                    if (progress.Observe(result, "final-cleanup"))
                    {
                        progress.CleanupVerified = true;
                        progress.CleanupRequired = false;
                        progress.InspectorClosedProofCount++;
                    }
                }
                catch (OperationCanceledException)
                {
                    progress.Fail(
                        "operation.cleanup_timeout",
                        "final-cleanup");
                }
            }

            cycleLock.Release();
        }

        return progress.ToResult();
    }

    private async Task ExecuteCycleCoreAsync(
        CycleProgress progress,
        ThemePackage package,
        CancellationToken cancellationToken)
    {
        var discovery = await coordinator.DiscoverAsync(cancellationToken);
        if (!progress.Observe(discovery, "discover"))
        {
            return;
        }

        progress.CleanupRequired = true;
        progress.CleanupVerified = false;
        var firstApply = await coordinator.ApplyTemporaryAsync(
            package,
            cancellationToken);
        if (!progress.Observe(firstApply, "first-apply"))
        {
            return;
        }
        progress.FirstQualificationApplied = true;
        progress.QualificationEstablishedWithinRun = true;
        progress.InspectorClosedProofCount++;

        var firstRestore = await coordinator.RestoreAsync(cancellationToken);
        if (!progress.Observe(firstRestore, "first-restore"))
        {
            return;
        }
        progress.FirstRestoreVerified = true;
        progress.CleanupRequired = false;
        progress.CleanupVerified = true;
        progress.InspectorClosedProofCount++;

        progress.CleanupRequired = true;
        progress.CleanupVerified = false;
        var secondApply = await coordinator.ApplyTemporaryAsync(
            package,
            cancellationToken);
        if (!progress.Observe(secondApply, "second-apply"))
        {
            return;
        }
        progress.SecondTemporaryApplyVerified = true;
        progress.InspectorClosedProofCount++;

        var secondRestore = await coordinator.RestoreAsync(cancellationToken);
        if (!progress.Observe(secondRestore, "second-restore"))
        {
            return;
        }
        progress.SecondRestoreVerified = true;
        progress.CleanupRequired = false;
        progress.CleanupVerified = true;
        progress.InspectorClosedProofCount++;
        progress.Succeeded = true;
    }

    private static MacQualificationCycleResult Failed(
        Guid requestId,
        string code,
        string stage) =>
        new(
            1,
            ToolVersion,
            requestId,
            "error",
            true,
            false,
            false,
            false,
            false,
            false,
            false,
            0,
            false,
            false,
            -1,
            -1,
            new MacQualificationCycleError(code, stage));

    private sealed class CycleProgress(Guid requestId)
    {
        private CodexInstallationIdentityV2? installation;
        private CodexProcessIdentityV2? process;

        public bool QualificationEstablishedWithinRun { get; set; }
        public bool FirstQualificationApplied { get; set; }
        public bool FirstRestoreVerified { get; set; }
        public bool SecondTemporaryApplyVerified { get; set; }
        public bool SecondRestoreVerified { get; set; }
        public bool CleanupRequired { get; set; }
        public bool CleanupAttempted { get; set; }
        public bool CleanupVerified { get; set; }
        public int InspectorClosedProofCount { get; set; }
        public int FinalResidualCount { get; private set; } = -1;
        public int FinalPortListenerCount { get; private set; } = -1;
        public bool ProcessStable { get; private set; } = true;
        public bool Succeeded { get; set; }
        public MacQualificationCycleError? Error { get; private set; }

        public bool Observe(
            OperationResult<CodexPlatformResponse> result,
            string stage)
        {
            if (!result.IsSuccess || result.Value is null)
            {
                Fail(
                    result.Error?.DiagnosticCode ?? "operation.failed",
                    stage);
                return false;
            }

            var value = result.Value;
            FinalResidualCount = value.ResidualCount;
            FinalPortListenerCount = value.PortListenerCount;
            if (value.Installation is null || value.Process is null)
            {
                Fail("process.identity_missing", stage);
                return false;
            }

            if (installation is null)
            {
                installation = value.Installation;
                process = value.Process;
                return true;
            }

            if (!SameInstallation(installation, value.Installation) ||
                !SameProcess(process!, value.Process))
            {
                ProcessStable = false;
                Fail("process.identity_changed", stage);
                return false;
            }

            return true;
        }

        public void Fail(string code, string stage)
        {
            Error ??= new MacQualificationCycleError(code, stage);
            Succeeded = false;
        }

        public MacQualificationCycleResult ToResult() =>
            new(
                1,
                ToolVersion,
                requestId,
                Succeeded ? "ok" : "error",
                true,
                QualificationEstablishedWithinRun,
                FirstQualificationApplied,
                FirstRestoreVerified,
                SecondTemporaryApplyVerified,
                SecondRestoreVerified,
                ProcessStable,
                InspectorClosedProofCount,
                CleanupAttempted,
                CleanupVerified,
                FinalResidualCount,
                FinalPortListenerCount,
                Error);

        private static bool SameInstallation(
            CodexInstallationIdentityV2 left,
            CodexInstallationIdentityV2 right) =>
            left == right;

        private static bool SameProcess(
            CodexProcessIdentityV2 left,
            CodexProcessIdentityV2 right) =>
            left == right;
    }
}
