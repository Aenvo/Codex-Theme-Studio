using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;
using Microsoft.Data.Sqlite;
using SkiaSharp;

namespace CodexThemeStudio.Storage.Tests;

public class ThemeImageImportServiceTests
{
    [Fact]
    public async Task ImportNew_StoresManagedPathsAndRepositoryRecord()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var repository = environment.CreateRepository();
        var service = new ThemeImageImportService(
            environment.DataRoot,
            new ImagePipeline(environment.DataRoot),
            repository);
        var theme = StorageTestEnvironment.CreateTheme("图片主题 🌌");

        var result = await service.ImportNewAsync(
            theme,
            new MemoryStream(
                TestImageFactory.Create(SKEncodedImageFormat.Png)),
            "中文 路径.png",
            new ThemeCreateOptions(Tags: ["自制"]),
            CancellationToken.None);
        var stored = await repository.GetAsync(theme.Id, CancellationToken.None);
        var summaries = await repository.ListAsync(CancellationToken.None);
        var summary = Assert.Single(summaries.Value!);

        Assert.True(result.IsSuccess, result.Error?.DiagnosticCode);
        Assert.True(stored.IsSuccess, stored.Error?.DiagnosticCode);
        Assert.Equal(
            result.Value!.Images.ThemeArtFileName,
            stored.Value!.Art.File);
        Assert.Equal(
            result.Value.Images.CardThumbnail.RelativePath,
            summary.ThumbnailRelativePath);
        Assert.Equal(["自制"], summary.Tags);
    }

    [Fact]
    public async Task ImportNew_RepositoryFailureRemovesNewThemeDirectoryAndRecord()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        await using (var connection = new SqliteConnection(
                         $"Data Source={environment.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TRIGGER fail_image_import
                BEFORE INSERT ON themes
                WHEN NEW.name = 'FAIL_IMAGE_IMPORT'
                BEGIN
                    SELECT RAISE(ABORT, 'forced failure');
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var repository = environment.CreateRepository();
        var service = new ThemeImageImportService(
            environment.DataRoot,
            new ImagePipeline(environment.DataRoot),
            repository);
        var theme = StorageTestEnvironment.CreateTheme("FAIL_IMAGE_IMPORT");

        var result = await service.ImportNewAsync(
            theme,
            new MemoryStream(
                TestImageFactory.Create(SKEncodedImageFormat.Jpeg)),
            "failure.jpg",
            null,
            CancellationToken.None);
        var themes = await repository.ListAsync(CancellationToken.None);
        var themeDirectory = Path.Combine(
            environment.DataRoot,
            StorageLayout.GetThemeDirectory(theme.Id).Replace(
                '/',
                Path.DirectorySeparatorChar));

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.StorageUnavailable, result.Error!.Code);
        Assert.Empty(themes.Value!);
        Assert.False(Directory.Exists(themeDirectory));
    }

    [Fact]
    public async Task ImportNew_DoesNotOverwritePreexistingOrphanDirectory()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var repository = environment.CreateRepository();
        var service = new ThemeImageImportService(
            environment.DataRoot,
            new ImagePipeline(environment.DataRoot),
            repository);
        var theme = StorageTestEnvironment.CreateTheme("Existing");
        var themeDirectory = Path.Combine(
            environment.DataRoot,
            StorageLayout.GetThemeDirectory(theme.Id).Replace(
                '/',
                Path.DirectorySeparatorChar));
        Directory.CreateDirectory(themeDirectory);
        var marker = Path.Combine(themeDirectory, "user-file.txt");
        await File.WriteAllTextAsync(marker, "preserve");

        var result = await service.ImportNewAsync(
            theme,
            new MemoryStream(
                TestImageFactory.Create(SKEncodedImageFormat.Webp)),
            "existing.webp",
            null,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.Conflict, result.Error!.Code);
        Assert.Equal("preserve", await File.ReadAllTextAsync(marker));
    }
}
