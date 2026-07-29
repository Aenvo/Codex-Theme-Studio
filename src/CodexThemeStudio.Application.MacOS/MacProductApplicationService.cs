using CodexThemeStudio.CodexRuntime;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Application.MacOS;

public sealed class MacProductApplicationService
{
    public const string ToolVersion = "0.1.4";
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

    public async Task<MacInspectorDiagnosticResult> RunInspectorDiagnosticAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty)
        {
            return MacInspectorDiagnosticResult.CreateUnverifiedFailure(
                ToolVersion,
                requestId,
                true,
                "protocol.request_invalid",
                "request");
        }

        bool acquired;
        try
        {
            acquired = await cycleLock.WaitAsync(0, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return MacInspectorDiagnosticResult.CreateUnverifiedFailure(
                ToolVersion,
                requestId,
                true,
                "operation.cancelled",
                "request");
        }

        if (!acquired)
        {
            return MacInspectorDiagnosticResult.CreateUnverifiedFailure(
                ToolVersion,
                requestId,
                true,
                "operation.busy",
                "request");
        }

        try
        {
            var discovery = await coordinator.DiscoverAsync(cancellationToken);
            if (!TryGetEvidence(
                    discovery,
                    out var installation,
                    out var process,
                    out var error))
            {
                return DiagnosticFailure(
                    requestId,
                    error!,
                    "discover",
                    discoverVerified: false,
                    inspectVerified: false,
                    inspectorClosedProofCount: 0);
            }

            var inspection =
                await coordinator.InspectRuntimeDetailedAsync(cancellationToken);
            if (!TryGetMatchingEvidence(
                    inspection.Operation,
                    installation!,
                    process!,
                    out error))
            {
                return DiagnosticFailure(
                    requestId,
                    error!,
                    "inspect",
                    discoverVerified: true,
                    inspectVerified: false,
                    inspectorClosedProofCount: 0,
                    recoveryError: inspection.RecoveryError);
            }

            var cleanup = await coordinator.RestoreDetailedAsync(cancellationToken);
            if (!TryGetMatchingEvidence(
                    cleanup.Operation,
                    installation!,
                    process!,
                    out error))
            {
                return DiagnosticFailure(
                    requestId,
                    error!,
                    "cleanup",
                    discoverVerified: true,
                    inspectVerified: true,
                    inspectorClosedProofCount: 1,
                    recoveryError: cleanup.RecoveryError);
            }

            var value = cleanup.Value!;
            return new MacInspectorDiagnosticResult(
                1,
                ToolVersion,
                requestId,
                "ok",
                true,
                true,
                true,
                true,
                true,
                "stable",
                2,
                value.ResidualCount,
                "verified-zero",
                value.PortListenerCount,
                "verified-zero",
                null,
                null);
        }
        catch (OperationCanceledException)
        {
            return MacInspectorDiagnosticResult.CreateUnverifiedFailure(
                ToolVersion,
                requestId,
                true,
                "operation.cancelled",
                "operation");
        }
        finally
        {
            cycleLock.Release();
        }
    }

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
                    if (progress.Observe(
                            result,
                            "final-cleanup",
                            finalProof: true,
                            recovery: true))
                    {
                        progress.CleanupVerified = true;
                        progress.CleanupRequired = false;
                        progress.InspectorClosedProofCount++;
                    }
                }
                catch (OperationCanceledException)
                {
                    progress.FailRecovery(
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
        if (!progress.Observe(
                secondRestore,
                "second-restore",
                finalProof: true))
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
        MacQualificationCycleResult.CreateUnverifiedFailure(
            ToolVersion,
            requestId,
            true,
            code,
            stage);

    private static MacInspectorDiagnosticResult DiagnosticFailure(
        Guid requestId,
        string code,
        string stage,
        bool discoverVerified,
        bool inspectVerified,
        int inspectorClosedProofCount,
        CodexPlatformFailure? recoveryError = null) =>
        new(
            1,
            ToolVersion,
            requestId,
            "error",
            true,
            discoverVerified,
            inspectVerified,
            false,
            code == "process.identity_changed" ? false : null,
            code == "process.identity_changed" ? "changed" : "unverified",
            inspectorClosedProofCount,
            null,
            "unverified",
            null,
            "unverified",
            new MacQualificationCycleError(code, stage),
            recoveryError is null
                ? null
                : new MacQualificationCycleError(
                    recoveryError.Code,
                    recoveryError.Stage));

    private static bool TryGetEvidence(
        OperationResult<CodexPlatformResponse> result,
        out CodexInstallationIdentityV2? installation,
        out CodexProcessIdentityV2? process,
        out string? error)
    {
        installation = result.Value?.Installation;
        process = result.Value?.Process;
        error = result.Error?.DiagnosticCode;
        if (!result.IsSuccess || result.Value is null ||
            installation is null || process is null)
        {
            error ??= "process.identity_missing";
            return false;
        }
        return true;
    }

    private static bool TryGetMatchingEvidence(
        OperationResult<CodexPlatformResponse> result,
        CodexInstallationIdentityV2 installation,
        CodexProcessIdentityV2 process,
        out string? error)
    {
        if (!TryGetEvidence(
                result,
                out var currentInstallation,
                out var currentProcess,
                out error))
        {
            return false;
        }
        if (currentInstallation != installation || currentProcess != process)
        {
            error = "process.identity_changed";
            return false;
        }
        return true;
    }

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
        public int? FinalResidualCount { get; private set; }
        public string ResidualProof { get; private set; } = "unverified";
        public int? FinalPortListenerCount { get; private set; }
        public string PortProof { get; private set; } = "unverified";
        public bool? ProcessStable { get; private set; }
        public string ProcessProof { get; private set; } = "unverified";
        public bool Succeeded { get; set; }
        public MacQualificationCycleError? Error { get; private set; }
        public MacQualificationCycleError? RecoveryError { get; private set; }

        public bool Observe(
            OperationResult<CodexPlatformResponse> result,
            string stage,
            bool finalProof = false,
            bool recovery = false)
        {
            if (!result.IsSuccess || result.Value is null)
            {
                var code =
                    result.Error?.DiagnosticCode ?? "operation.failed";
                if (code == "process.identity_changed")
                {
                    ProcessStable = false;
                    ProcessProof = "changed";
                }
                RecordFailure(code, stage, recovery);
                return false;
            }

            var value = result.Value;
            if (value.Installation is null || value.Process is null)
            {
                RecordFailure(
                    "process.identity_missing",
                    stage,
                    recovery);
                return false;
            }

            if (installation is null)
            {
                installation = value.Installation;
                process = value.Process;
                if (finalProof)
                {
                    RecordFinalProof(value);
                }
                return true;
            }

            if (!SameInstallation(installation, value.Installation) ||
                !SameProcess(process!, value.Process))
            {
                ProcessStable = false;
                ProcessProof = "changed";
                RecordFailure(
                    "process.identity_changed",
                    stage,
                    recovery);
                return false;
            }

            if (finalProof)
            {
                RecordFinalProof(value);
            }
            return true;
        }

        public void Fail(string code, string stage)
        {
            Error ??= new MacQualificationCycleError(code, stage);
            Succeeded = false;
        }

        public void FailRecovery(string code, string stage)
        {
            RecoveryError ??=
                new MacQualificationCycleError(code, stage);
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
                ProcessProof,
                InspectorClosedProofCount,
                CleanupAttempted,
                CleanupVerified,
                FinalResidualCount,
                ResidualProof,
                FinalPortListenerCount,
                PortProof,
                Error,
                RecoveryError);

        private void RecordFailure(
            string code,
            string stage,
            bool recovery)
        {
            if (recovery)
            {
                FailRecovery(code, stage);
            }
            else
            {
                Fail(code, stage);
            }
        }

        private void RecordFinalProof(CodexPlatformResponse value)
        {
            if (ProcessStable != false)
            {
                ProcessStable = true;
                ProcessProof = "stable";
            }
            FinalResidualCount = value.ResidualCount;
            ResidualProof = value.ResidualCount == 0
                ? "verified-zero"
                : "verified-nonzero";
            FinalPortListenerCount = value.PortListenerCount;
            PortProof = value.PortListenerCount == 0
                ? "verified-zero"
                : "verified-nonzero";
        }

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
