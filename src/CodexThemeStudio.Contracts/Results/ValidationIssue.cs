namespace CodexThemeStudio.Contracts.Results;

public enum ValidationSeverity
{
    Information = 0,
    Warning,
    Error,
}

public sealed record ValidationIssue(
    string Code,
    string UserMessage,
    ValidationSeverity Severity,
    string? FieldPath = null);

