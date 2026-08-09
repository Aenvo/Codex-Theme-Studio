namespace CodexThemeStudio.Update;

public sealed record GitHubUpdateServiceOptions
{
    public required string CurrentVersion { get; init; }

    public Uri LatestReleaseApi { get; init; } = new(
        "https://api.github.com/repos/Aenvo/Codex-Theme-Studio/releases/latest");

    public Uri ReleasesFeed { get; init; } = new(
        "https://github.com/Aenvo/Codex-Theme-Studio/releases.atom");

    public string ProductAssetPrefix { get; init; } = "Codex-Theme-Studio";

    public TimeSpan CacheDuration { get; init; } = TimeSpan.FromHours(12);

    public TimeSpan CheckTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public string StagingBaseDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexThemeStudio",
        "Updates",
        "staging");
}
