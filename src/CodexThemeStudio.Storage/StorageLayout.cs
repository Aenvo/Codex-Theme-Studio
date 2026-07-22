namespace CodexThemeStudio.Storage;

public static class StorageLayout
{
    public const string DatabaseDirectory = "database";
    public const string ThemesDirectory = "themes";
    public const string CacheDirectory = "cache";
    public const string ExportsDirectory = "exports";
    public const string LogsDirectory = "logs";
    public const string RecoveryDirectory = "recovery";
    public const string RuntimeDirectory = "runtime";
    public const string ImageImportStagingDirectory = "runtime/image-imports";
    public const string PreviewCacheDirectory = "cache/previews";
    public const string ThumbnailCacheDirectory = "cache/thumbnails";
    public const string DatabaseFileName = "themes.db";
    public const string ThemeFileName = "theme.json";

    public static IReadOnlyList<string> RequiredDirectories { get; } =
    [
        DatabaseDirectory,
        ThemesDirectory,
        CacheDirectory,
        ExportsDirectory,
        LogsDirectory,
        RecoveryDirectory,
        RuntimeDirectory,
    ];

    public static string GetThemeDirectory(Guid themeId) =>
        $"{ThemesDirectory}/{themeId:D}";

    public static string GetThemeDocument(Guid themeId) =>
        $"{GetThemeDirectory(themeId)}/{ThemeFileName}";
}
