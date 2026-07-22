using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Storage;

namespace CodexThemeStudio.Storage.Tests;

public class StorageConsistencyServiceTests
{
    [Fact]
    public async Task Scan_ReportsMissingInvalidAndOrphanWithoutDeletingThem()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var repository = environment.CreateRepository();
        var missingTheme = StorageTestEnvironment.CreateTheme("Missing");
        var invalidTheme = StorageTestEnvironment.CreateTheme("Invalid");
        Assert.True(
            (await repository.SaveAsync(
                missingTheme,
                new ThemeCreateOptions(),
                CancellationToken.None)).IsSuccess);
        Assert.True(
            (await repository.SaveAsync(
                invalidTheme,
                new ThemeCreateOptions(),
                CancellationToken.None)).IsSuccess);

        var missingPath = Path.Combine(
            environment.DataRoot,
            StorageLayout.GetThemeDocument(missingTheme.Id));
        var recoveryPath = Path.Combine(
            environment.DataRoot,
            StorageLayout.RecoveryDirectory,
            $"{missingTheme.Id:D}.theme.json");
        File.Move(missingPath, recoveryPath);

        var invalidPath = Path.Combine(
            environment.DataRoot,
            StorageLayout.GetThemeDocument(invalidTheme.Id));
        await File.WriteAllTextAsync(invalidPath, """{"schemaVersion":1}""");

        var orphanRelative = "themes/orphan-directory";
        var orphanPath = Path.Combine(
            environment.DataRoot,
            orphanRelative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(orphanPath);
        await File.WriteAllTextAsync(
            Path.Combine(orphanPath, StorageLayout.ThemeFileName),
            "{}");

        var service = new StorageConsistencyService(environment.DataRoot);
        var scan = await service.ScanAsync(CancellationToken.None);

        Assert.True(scan.IsSuccess);
        Assert.Contains(missingTheme.Id, scan.Value!.MissingThemeFiles);
        Assert.Contains(
            StorageLayout.GetThemeDocument(invalidTheme.Id),
            scan.Value.InvalidThemeFiles);
        Assert.Contains(orphanRelative, scan.Value.OrphanThemeDirectories);
        Assert.True(Directory.Exists(orphanPath));
        Assert.True(File.Exists(Path.Combine(orphanPath, StorageLayout.ThemeFileName)));
    }
}

