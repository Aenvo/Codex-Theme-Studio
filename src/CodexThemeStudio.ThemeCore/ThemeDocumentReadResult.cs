using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.ThemeCore;

public enum ThemeDocumentReadStatus
{
    Success = 0,
    Invalid,
    UnsupportedNewerSchema,
}

public sealed record ThemeDocumentReadResult(
    ThemeDocumentReadStatus Status,
    int? SchemaVersion,
    ThemePackage? Theme,
    IReadOnlyList<ValidationIssue> Issues);

