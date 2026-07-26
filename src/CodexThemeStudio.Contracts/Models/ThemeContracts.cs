namespace CodexThemeStudio.Contracts.Models;

public enum ThemeVariant
{
    Auto = 0,
    Light,
    Dark,
}

public enum ThemeSafeArea
{
    Auto = 0,
    Center,
    Top,
    Bottom,
    Left,
    Right,
    None,
}

public enum ThemeArtSize
{
    Cover = 0,
    Contain,
    Crop,
}

public enum ThemeTaskMode
{
    Ambient = 0,
    Hidden,
    Full,
}

public enum ThemeSourceType
{
    LocalCreated = 0,
    Imported,
    RemoteSnapshot,
}

public enum ThemeApplyResult
{
    NeverApplied = 0,
    Succeeded,
    Failed,
}

public sealed record ThemePalette(
    string Background,
    string Panel,
    string Accent,
    string Text,
    string Muted,
    string Border);

public sealed record ThemeArt(
    string File,
    double FocusX,
    double FocusY,
    ThemeSafeArea SafeArea,
    ThemeArtSize Size,
    double HomeOpacity,
    double HomeOverlay,
    ThemeTaskMode TaskMode,
    double TaskOpacity,
    double TaskOverlay,
    double Blur,
    double PanelBlur = 0,
    double CropScale = 1);

public sealed record ThemePackage(
    int SchemaVersion,
    Guid Id,
    string Name,
    ThemeVariant Variant,
    ThemePalette Palette,
    ThemeArt Art);

public sealed record ThemeSummary(
    Guid ThemeId,
    string DisplayName,
    int SchemaVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ModifiedAtUtc,
    DateTimeOffset? LastUsedAtUtc,
    bool IsFavorite,
    int SortOrder,
    IReadOnlyList<string> Tags,
    ThemeSourceType SourceType,
    string? SourceIdentifier,
    bool IsSourceReadOnly,
    string ThemeDirectoryRelativePath,
    string? ThumbnailRelativePath,
    string ContentSha256,
    bool IsCurrentPersistent,
    ThemeApplyResult LastApplyResult,
    string? LastApplyMessage);

public enum ThemeRuntimeState
{
    Default = 0,
    Applying,
    Temporary,
    Persistent,
    Restoring,
    Unavailable,
    Failed,
    NotInstalled,
    NotRunning,
    Ready,
    Partial,
    Mismatch,
    InspectorResidual,
    Unsupported,
}

public enum ThemeRuntimeEvidence
{
    None = 0,
    ProcessOnly,
    RuntimeMarkers,
    VisibleEffect,
}

public sealed record ThemeRuntimeStatus(
    ThemeRuntimeState State,
    Guid? ThemeId,
    bool IsPersistenceEnabled,
    int? CodexProcessId,
    DateTimeOffset ObservedAtUtc,
    string UserMessage,
    Guid? SelectedThemeId = null,
    Guid? OperationId = null,
    ThemeRuntimeEvidence Evidence = ThemeRuntimeEvidence.None,
    bool HasInspectorResidual = false,
    int EligibleWindows = 0,
    int AppliedWindows = 0,
    int PendingWindows = 0,
    string? CodexVersion = null,
    CodexCompatibilityLevel CompatibilityLevel = CodexCompatibilityLevel.Verified,
    CodexIdentityAssessment IdentityAssessment = CodexIdentityAssessment.TrustedStore,
    CodexInstallationSource InstallationSource = CodexInstallationSource.StoreAutomatic,
    string? ExecutableSha256 = null,
    DateTimeOffset? CompatibilityProbedAtUtc = null,
    string? CompatibilityDiagnosticCode = null,
    bool IsPersistenceEligible = true);
