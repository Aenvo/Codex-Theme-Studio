namespace CodexThemeStudio.Contracts.Models;

public sealed record UpdateAssetInfo(
    string Name,
    Uri DownloadUri,
    long Size,
    string Sha256Digest);

public sealed record UpdateReleaseInfo(
    string Version,
    string TagName,
    Uri ReleaseUri,
    DateTimeOffset PublishedAt,
    string ReleaseNotes,
    IReadOnlyList<UpdateAssetInfo> Assets);

public sealed record UpdateCheckResult(
    string CurrentVersion,
    bool IsUpdateAvailable,
    UpdateReleaseInfo? Release,
    bool WasCached);

public sealed record UpdateDownloadProgress(
    long BytesReceived,
    long TotalBytes,
    int Percentage,
    string Stage);

public sealed record StagedUpdate(
    UpdateReleaseInfo Release,
    string StagingRoot,
    string ZipPath,
    string ZipSha256,
    long ExpandedBytes,
    string InstallManifestPath);

public sealed record UpdateInstallRequest(
    string Token,
    string CurrentVersion,
    string TargetVersion,
    string ApplicationRoot,
    string StagingRoot,
    string ExecutableRelativePath,
    string ZipSha256,
    int CurrentProcessId);

public enum UpdateInstallOutcome
{
    Started,
    Succeeded,
    RolledBack,
    RollbackIncomplete,
    CleanupIncomplete,
    Failed,
}

public sealed record UpdateInstallResult(
    UpdateInstallOutcome Outcome,
    string OldVersion,
    string NewVersion,
    string UserMessage,
    string? PreservedDirectory = null,
    string? BackupDirectory = null);

public sealed record AgentUpgradeResult(
    bool WasRequired,
    bool Upgraded,
    string UserMessage);
