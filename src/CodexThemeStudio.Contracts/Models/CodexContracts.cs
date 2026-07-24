namespace CodexThemeStudio.Contracts.Models;

public enum CodexInstallationSource
{
    StoreAutomatic = 0,
    ManualExecutable,
}

public enum CodexIdentityAssessment
{
    TrustedStore = 0,
    UnverifiedSource,
}

public enum CodexCompatibilityLevel
{
    Verified = 0,
    CompatibleByProbe,
    Incompatible,
}

public sealed record CodexInstallationInfo(
    string PackageFamilyName,
    string PackageFullName,
    string Version,
    string ExecutablePath,
    string PublisherId = "2p2nqsd0c76g0",
    bool IsStoreSigned = true,
    CodexInstallationSource Source = CodexInstallationSource.StoreAutomatic,
    CodexIdentityAssessment IdentityAssessment = CodexIdentityAssessment.TrustedStore,
    string ExecutableSha256 = "",
    bool SourceAcknowledged = true);

public sealed record CodexProcessInfo(
    int ProcessId,
    DateTimeOffset StartedAtUtc,
    string ExecutablePath,
    string? BrowserId);

public sealed record CodexDiscoverySnapshot(
    CodexInstallationInfo Installation,
    IReadOnlyList<CodexProcessInfo> Processes,
    DateTimeOffset ObservedAtUtc);

public sealed record CodexProbeResult(
    int ProcessId,
    DateTimeOffset ProcessStartedAtUtc,
    string ElectronVersion,
    int WindowCount,
    IReadOnlyList<string> RouteTypes,
    TimeSpan InspectorOpenDuration,
    bool ElectronAvailable = true,
    bool BrowserWindowAvailable = true,
    bool ExecuteJavaScriptAvailable = true,
    int EligibleWindowCount = 1,
    bool CanaryApplied = true,
    bool CanaryCleaned = true,
    string? DiagnosticCode = null);

public sealed record CodexTargetStatus(
    bool HasManualSelection,
    string? ExecutablePath,
    string? ExecutableSha256,
    bool SourceAcknowledged,
    string UserMessage);

public enum CodexStatusRefreshMode
{
    PreferCache = 0,
    ForceProbe,
}

public sealed record CodexCachedCompatibilityStatus(
    string CodexVersion,
    string PackageFullName,
    string ExecutablePath,
    string ExecutableSha256,
    CodexCompatibilityLevel CompatibilityLevel,
    CodexIdentityAssessment IdentityAssessment,
    CodexInstallationSource InstallationSource,
    DateTimeOffset ProbedAtUtc,
    bool IsPersistenceEligible);
