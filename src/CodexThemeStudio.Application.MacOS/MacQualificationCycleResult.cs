namespace CodexThemeStudio.Application.MacOS;

public sealed record MacQualificationCycleError(
    string Code,
    string Stage);

public sealed record MacQualificationCycleResult(
    int SchemaVersion,
    string ToolVersion,
    Guid RequestId,
    string Status,
    bool RuntimeIdentityVerified,
    bool QualificationEstablishedWithinRun,
    bool FirstQualificationApplied,
    bool FirstRestoreVerified,
    bool SecondTemporaryApplyVerified,
    bool SecondRestoreVerified,
    bool ProcessStable,
    int InspectorClosedProofCount,
    bool CleanupAttempted,
    bool CleanupVerified,
    int FinalResidualCount,
    int FinalPortListenerCount,
    MacQualificationCycleError? Error);
