namespace CodexThemeStudio.Contracts.Models;

public sealed record ExternalThemeDescriptor(
    string Provider,
    string SourceIdentifier,
    ThemePackage Theme,
    string ImageFileName,
    string ImageSha256,
    bool IsPersistenceConfigured,
    bool IsAppliedToCurrentProcess,
    int? AppliedProcessId,
    string UserMessage);
