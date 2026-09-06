using CodexThemeStudio.Contracts.Models;

namespace CodexThemeStudio.Desktop.MacOS.Services;

public enum MacLibraryState
{
    Initializing = 0,
    Loading,
    Ready,
    Empty,
    Error,
}

public enum MacRuntimeState
{
    Unknown = 0,
    CodexNotRunning,
    ReadyUnqualified,
    Ready,
    IdentityChanged,
    Unavailable,
}

public enum MacOperationStage
{
    Idle = 0,
    Qualifying,
    Applying,
    TemporaryApplied,
    Restoring,
    SafeCleanup,
    Failed,
}

public enum MacProofState
{
    NotRequested = 0,
    Pending,
    Verified,
    Unverified,
}

public sealed record MacThemeLibraryItem(
    Guid ThemeId,
    string DisplayName,
    string Subtitle,
    IReadOnlyList<string> Tags,
    ThemePalette Palette,
    bool IsFavorite,
    DateTimeOffset? LastUsedAtUtc,
    bool HasLocalBackgroundPreview,
    Uri? LocalPreviewAssetUri = null);

public sealed record MacThemeLibrarySnapshot(
    IReadOnlyList<MacThemeLibraryItem> Themes,
    bool PureCssPaletteAvailable,
    string CapabilityMessage);

public sealed record MacRuntimeSnapshot(
    MacRuntimeState State,
    bool PureCssPaletteAvailable,
    string UserMessage);

public sealed record MacThemeOperationOutcome(
    bool IsSuccess,
    MacOperationStage FinalStage,
    MacProofState CleanupProof,
    string? ErrorCode,
    string? RecoveryErrorCode);

public sealed class MacThemeOperationHandle
{
    public MacThemeOperationHandle(
        Guid operationId,
        bool sideEffectsMayHaveStarted,
        Task<MacThemeOperationOutcome> completion)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Operation ID must not be empty.",
                nameof(operationId));
        }

        OperationId = operationId;
        SideEffectsMayHaveStarted = sideEffectsMayHaveStarted;
        Completion = completion ??
            throw new ArgumentNullException(nameof(completion));
    }

    public Guid OperationId { get; }

    public bool SideEffectsMayHaveStarted { get; }

    public Task<MacThemeOperationOutcome> Completion { get; }
}

public interface IMacThemeStudioClient
{
    Task<MacThemeLibrarySnapshot> LoadLibraryAsync(
        CancellationToken cancellationToken);

    Task<MacRuntimeSnapshot> RefreshRuntimeAsync(
        CancellationToken cancellationToken);

    ValueTask<MacThemeOperationHandle> BeginApplyTemporaryAsync(
        Guid themeId,
        CancellationToken admissionCancellationToken);

    ValueTask<MacThemeOperationHandle> BeginRestoreAsync(
        CancellationToken admissionCancellationToken);
}
