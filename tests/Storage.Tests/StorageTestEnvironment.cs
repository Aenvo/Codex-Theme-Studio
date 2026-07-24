using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Storage;
using CodexThemeStudio.ThemeCore;

namespace CodexThemeStudio.Storage.Tests;

internal sealed class StorageTestEnvironment : IAsyncDisposable
{
    private StorageTestEnvironment(
        string containerRoot,
        string dataRoot,
        string bootstrapPath)
    {
        ContainerRoot = containerRoot;
        DataRoot = dataRoot;
        BootstrapPath = bootstrapPath;
    }

    public string ContainerRoot { get; }

    public string DataRoot { get; }

    public string BootstrapPath { get; }

    public string DatabasePath =>
        Path.Combine(
            DataRoot,
            StorageLayout.DatabaseDirectory,
            StorageLayout.DatabaseFileName);

    public static async Task<StorageTestEnvironment> CreateAsync()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "CodexThemeStudio.Storage.Tests");
        var containerRoot = Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
        var dataRoot = Path.Combine(containerRoot, "data");
        var bootstrapPath = Path.Combine(containerRoot, "bootstrap", "bootstrap.json");

        var environment = new StorageTestEnvironment(
            containerRoot,
            dataRoot,
            bootstrapPath);
        var location = new StorageLocationService(bootstrapPath);
        var initialized = await location.InitializeAsync(
            dataRoot,
            CancellationToken.None);
        Assert.True(
            initialized.IsSuccess,
            initialized.Error?.DiagnosticCode);

        var database = new SqliteDatabaseInitializer(dataRoot);
        var migrated = await database.InitializeAsync(CancellationToken.None);
        Assert.True(migrated.IsSuccess, migrated.Error?.DiagnosticCode);

        return environment;
    }

    public SqliteThemeRepository CreateRepository(
        IThemeDirectoryRecycleService? directoryRecycler = null) =>
        new(
            DataRoot,
            new ThemeDocumentSerializer(),
            directoryRecycler: directoryRecycler);

    public static ThemePackage CreateTheme(
        string name,
        Guid? id = null) =>
        new(
            ThemePackageContractValidator.CurrentSchemaVersion,
            id ?? Guid.NewGuid(),
            name,
            ThemeVariant.Auto,
            new ThemePalette(
                "#100D14",
                "#18131D",
                "#B98BD2",
                "#EEE8F2",
                "#A99EAE",
                "#302735"),
            new ThemeArt(
                "background.webp",
                0.5,
                0.5,
                ThemeSafeArea.Auto,
                ThemeArtSize.Cover,
                0.8,
                0.25,
                ThemeTaskMode.Ambient,
                0.3,
                0.65,
                0));

    public ValueTask DisposeAsync()
    {
        var expectedParent = Path.GetFullPath(
            Path.Combine(
                Path.GetTempPath(),
                "CodexThemeStudio.Storage.Tests")) + Path.DirectorySeparatorChar;
        var fullContainer = Path.GetFullPath(ContainerRoot);
        if (fullContainer.StartsWith(
                expectedParent,
                StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(fullContainer))
        {
            Directory.Delete(fullContainer, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
