namespace CodexThemeStudio.Contracts.Models;

public sealed record CodexInstallationInfo(
    string PackageFamilyName,
    string PackageFullName,
    string Version,
    string ExecutablePath,
    string PublisherId = "2p2nqsd0c76g0",
    bool IsStoreSigned = true);

public sealed record CodexProcessInfo(
    int ProcessId,
    DateTimeOffset StartedAtUtc,
    string ExecutablePath,
    string? BrowserId);

public sealed record CodexProbeResult(
    int ProcessId,
    DateTimeOffset ProcessStartedAtUtc,
    string ElectronVersion,
    int WindowCount,
    IReadOnlyList<string> RouteTypes,
    TimeSpan InspectorOpenDuration);
