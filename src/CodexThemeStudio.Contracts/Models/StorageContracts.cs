namespace CodexThemeStudio.Contracts.Models;

public sealed record StorageConsistencyReport(
    IReadOnlyList<Guid> MissingThemeFiles,
    IReadOnlyList<string> OrphanThemeDirectories,
    IReadOnlyList<string> InvalidThemeFiles);

public sealed record ThemeCreateOptions(
    ThemeSourceType SourceType = ThemeSourceType.LocalCreated,
    string? SourceIdentifier = null,
    bool IsSourceReadOnly = false,
    string? ThumbnailRelativePath = null,
    IReadOnlyList<string>? Tags = null);

public sealed record ThemePackageFile(
    string Path,
    long Size,
    string Sha256);

public sealed record ThemePackageManifest(
    int PackageSchemaVersion,
    int ThemeSchemaVersion,
    Guid ThemeId,
    IReadOnlyList<ThemePackageFile> Files);

public sealed record ThemePackageImportResult(
    Guid ThemeId,
    string DisplayName,
    string PackageSha256);

public sealed record StorageMigrationReport(
    string SourceDataRoot,
    string DestinationDataRoot,
    int FileCount,
    long TotalBytes,
    string CombinedSha256,
    bool DatabaseIntegrityVerified,
    bool BootstrapUpdated,
    bool SourceRetained);
