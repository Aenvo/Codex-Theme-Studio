namespace CodexThemeStudio.Contracts.Models;

public enum CodexHostPlatform
{
    Windows = 0,
    MacOS,
}

public enum CodexPlatformCommand
{
    Discover = 0,
    InspectRuntime,
    ApplyTemporary,
    QualifyAndApply,
    Cleanup,
}

public enum CodexCleanupDisposition
{
    NotNeeded = 0,
    Verified,
    Unverified,
}

public enum CodexInspectorDisposition
{
    NotOpened = 0,
    Closed,
    Residual,
    Unknown,
}

public sealed record CodexInstallationIdentityV2(
    CodexHostPlatform Platform,
    string ProductIdentifier,
    string PublisherIdentifier,
    string Version,
    string BuildVersion,
    string ExecutablePath,
    string ExecutableSha256,
    bool SignatureValid,
    bool HardenedRuntime,
    bool GatekeeperAccepted);

public sealed record CodexProcessIdentityV2(
    int ProcessId,
    int ParentProcessId,
    DateTimeOffset StartedAtUtc,
    string ExecutablePath,
    string Architecture);

public sealed record CodexRuntimeCapabilities(
    bool PureCssPalette,
    bool BackgroundImage,
    bool TemporaryApply,
    bool Cleanup,
    bool Requalification);

public sealed record CodexThemePaletteProjection(
    string Background,
    string Panel,
    string Accent,
    string Text,
    string Muted,
    string Border);

public sealed record CodexThemeProjection(
    int SchemaVersion,
    Guid ThemeId,
    ThemeVariant Variant,
    CodexThemePaletteProjection Palette);

public sealed record CodexPlatformRequest(
    int SchemaVersion,
    int ProtocolVersion,
    Guid RequestId,
    CodexPlatformCommand Command,
    int DeadlineMilliseconds,
    string BundlePath,
    CodexThemeProjection? Theme);

public sealed record CodexPlatformResponse(
    int SchemaVersion,
    int ProtocolVersion,
    string ToolVersion,
    Guid RequestId,
    bool IsSuccess,
    CodexInstallationIdentityV2? Installation,
    CodexProcessIdentityV2? Process,
    CodexRuntimeCapabilities? Capabilities,
    CodexCleanupDisposition CleanupDisposition,
    CodexInspectorDisposition InspectorDisposition,
    int EligibleWindowCount,
    int AppliedWindowCount,
    int ResidualCount,
    int PortListenerCount,
    string? ErrorCode);
