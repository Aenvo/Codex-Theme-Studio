using CodexThemeStudio.CodexAdapter;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Storage;

namespace CodexThemeStudio.Integration.Tests;

public sealed class PersistenceLiveTests
{
    [LiveFact("enable")]
    public async Task LiveEnable()
    {
        var bundle = RequireEnvironmentPath("CTS_LIVE_BUNDLE");
        var dataRoot = RequireEnvironmentPath("CTS_LIVE_DATA_ROOT");
        var theme = CreateTheme();
        var (repository, assets) = await PrepareDataAsync(dataRoot, theme);
        var injector = new InjectorCommandClient(
            Path.Combine(bundle, "runtime", "node", "node.exe"),
            Path.Combine(bundle, "runtime", "injector", "index.mjs"));
        var runtime = new CodexThemeRuntimeService(
            injector,
            injector,
            injector,
            assets,
            repository,
            new AtomicCurrentSessionStore(dataRoot));
        var persistence = new PersistenceService(
            runtime,
            assets,
            repository,
            new PersistenceSnapshotStore(),
            new ManagedAgentInstaller(),
            new WindowsRunStartupManager(),
            new AgentProcessController(),
            bundle);

        var result = await persistence.EnableAsync(theme, CancellationToken.None);

        Assert.True(
            result.IsSuccess,
            result.Error?.DiagnosticCode ?? result.Error?.UserMessage);
        Assert.True(result.Value!.IsPersistenceEnabled);
        Assert.Equal(ThemeRuntimeState.Persistent, result.Value.State);
    }

    [LiveFact("disable")]
    public async Task LiveDisable()
    {
        var bundle = RequireEnvironmentPath("CTS_LIVE_BUNDLE");
        var dataRoot = RequireEnvironmentPath("CTS_LIVE_DATA_ROOT");
        var theme = CreateTheme();
        var (repository, assets) = await PrepareDataAsync(dataRoot, theme);
        var injector = new InjectorCommandClient(
            Path.Combine(bundle, "runtime", "node", "node.exe"),
            Path.Combine(bundle, "runtime", "injector", "index.mjs"));
        var runtime = new CodexThemeRuntimeService(
            injector,
            injector,
            injector,
            assets,
            repository,
            new AtomicCurrentSessionStore(dataRoot));
        var persistence = new PersistenceService(
            runtime,
            assets,
            repository,
            new PersistenceSnapshotStore(),
            new ManagedAgentInstaller(),
            new WindowsRunStartupManager(),
            new AgentProcessController(),
            bundle);

        var result = await persistence.DisableAsync(CancellationToken.None);

        Assert.True(
            result.IsSuccess,
            result.Error?.DiagnosticCode ?? result.Error?.UserMessage);
        Assert.False(result.Value!.IsPersistenceEnabled);
        Assert.Equal(ThemeRuntimeState.Default, result.Value.State);
    }

    private static async Task<(SqliteThemeRepository, FileThemeAssetStore)>
        PrepareDataAsync(string dataRoot, ThemePackage theme)
    {
        Directory.CreateDirectory(dataRoot);
        foreach (var relative in StorageLayout.RequiredDirectories)
        {
            Directory.CreateDirectory(Path.Combine(dataRoot, relative));
        }

        var initializer = new SqliteDatabaseInitializer(dataRoot);
        Assert.True((await initializer.InitializeAsync(CancellationToken.None)).IsSuccess);
        var repository = new SqliteThemeRepository(dataRoot);
        Assert.True(
            (await repository.SaveAsync(
                theme,
                new ThemeCreateOptions(),
                CancellationToken.None)).IsSuccess);
        var assets = new FileThemeAssetStore(dataRoot);
        await using var image = new MemoryStream(Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAQAAAAECAYAAACp8Z5+AAAAAXNSR0IArs4c6Q" +
            "AAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAASSURBVBhX" +
            "Y4iRb/iPjBlIFwAAIXAfoXwjkNQAAAAASUVORK5CYII="));
        Assert.True(
            (await assets.SaveAsync(
                theme.Id,
                theme.Art.File,
                image,
                CancellationToken.None)).IsSuccess);
        return (repository, assets);
    }

    private static ThemePackage CreateTheme() =>
        new(
            1,
            Guid.Parse("c9ae861a-9ace-4ad9-9fef-5779841531ac"),
            "Task 8 live persistence verification",
            ThemeVariant.Dark,
            new ThemePalette(
                "#15101D",
                "#241B30E8",
                "#D06CF2",
                "#FAF5FC",
                "#BDAFC4",
                "#4B3A57"),
            new ThemeArt(
                "verification.png",
                0.5,
                0.5,
                ThemeSafeArea.Center,
                ThemeArtSize.Cover,
                0.82,
                0.22,
                ThemeTaskMode.Ambient,
                0.3,
                0.68,
                0));

    private static string RequireEnvironmentPath(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(value), $"{name} is required.");
        Assert.True(Path.IsPathFullyQualified(value), $"{name} must be absolute.");
        return Path.GetFullPath(value);
    }
}
