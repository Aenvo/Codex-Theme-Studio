namespace CodexThemeStudio.Application.MacOS;

public sealed record MacInspectorDiagnosticResult(
    int SchemaVersion,
    string ToolVersion,
    Guid RequestId,
    string Status,
    bool RuntimeIdentityVerified,
    bool DiscoverVerified,
    bool InspectVerified,
    bool CleanupVerified,
    bool? ProcessStable,
    string ProcessProof,
    int InspectorClosedProofCount,
    int? FinalResidualCount,
    string ResidualProof,
    int? FinalPortListenerCount,
    string PortProof,
    MacQualificationCycleError? Error)
{
    public static MacInspectorDiagnosticResult CreateUnverifiedFailure(
        string toolVersion,
        Guid requestId,
        bool runtimeIdentityVerified,
        string code,
        string stage) =>
        new(
            1,
            toolVersion,
            requestId,
            "error",
            runtimeIdentityVerified,
            false,
            false,
            false,
            null,
            "unverified",
            0,
            null,
            "unverified",
            null,
            "unverified",
            new MacQualificationCycleError(code, stage));
}
