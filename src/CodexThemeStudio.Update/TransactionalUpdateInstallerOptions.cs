namespace CodexThemeStudio.Update;

public sealed record TransactionalUpdateInstallerOptions
{
    public required string CurrentVersion { get; init; }

    public required string ApplicationRoot { get; init; }

    public required string NodeExecutablePath { get; init; }

    public required string UpdaterScriptPath { get; init; }

    public string ExecutableRelativePath { get; init; } = "CodexThemeManager.exe";

    public string UpdatesRoot { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexThemeStudio",
        "Updates");
}
