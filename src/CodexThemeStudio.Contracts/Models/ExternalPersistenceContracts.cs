namespace CodexThemeStudio.Contracts.Models;

public enum ExternalPersistenceDisableOutcome
{
    Success = 0,
    Partial,
}

public sealed record ExternalPersistenceStatus(
    string Provider,
    bool IsConfigured,
    bool IsStartupRegistered,
    bool IsAgentRunning,
    bool CanDisableSafely,
    IReadOnlyList<string> Residuals,
    string UserMessage)
{
    public bool RequiresAction =>
        IsConfigured || IsStartupRegistered || IsAgentRunning;
}

public sealed record ExternalPersistenceDisableResult(
    ExternalPersistenceDisableOutcome Outcome,
    ExternalPersistenceStatus Status,
    string UserMessage);
