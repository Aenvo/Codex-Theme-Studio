using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Contracts.Models;

public enum DiagnosticSource
{
    Desktop,
    Agent,
}

public enum DiagnosticLevel
{
    Information,
    Warning,
    Error,
}

public enum DiagnosticOutcome
{
    Started,
    Succeeded,
    Failed,
    State,
    Recovered,
}

public enum DiagnosticHealth
{
    Empty,
    Normal,
    NeedsAttention,
    Unavailable,
}

public sealed record DiagnosticExceptionInfo(
    string Type,
    int HResult,
    IReadOnlyList<string> MethodFrames,
    string StackFingerprint);

public sealed record DiagnosticEvent(
    int SchemaVersion,
    DateTimeOffset TimestampUtc,
    DiagnosticSource Source,
    DiagnosticLevel Level,
    string EventName,
    string? Operation,
    DiagnosticOutcome Outcome,
    Guid SessionId,
    Guid? CorrelationId,
    OperationErrorCode? ErrorCode,
    string? DiagnosticCode,
    string AppVersion,
    string? CodexVersion,
    string? CodexExecutableSha256,
    DiagnosticExceptionInfo? Exception)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record DiagnosticEventGroup(
    DiagnosticEvent Event,
    int RepeatCount,
    DateTimeOffset FirstTimestampUtc,
    DateTimeOffset LastTimestampUtc);

public sealed record DiagnosticSnapshot(
    DiagnosticHealth Health,
    string StatusText,
    DateTimeOffset? LastUpdatedAtUtc,
    DiagnosticEvent? LatestError,
    DiagnosticEvent? LatestRecovery,
    IReadOnlyList<DiagnosticEventGroup> RecentEvents,
    string LogDirectory,
    bool LogDirectoryAvailable,
    int InvalidLineCount,
    string? WriteFailureCode);

public sealed record DiagnosticIssueContext(
    string AppVersion,
    string? CodexVersion,
    string? CodexExecutableSha256,
    string RuntimeStatus);

public sealed record DiagnosticIssueDraft(
    Guid ReportId,
    string Markdown,
    DiagnosticSnapshot Snapshot);

public sealed record DiagnosticBundleResult(
    Guid ReportId,
    string DestinationPath,
    int FileCount,
    long TotalBytes,
    string Sha256);
