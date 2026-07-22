using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;
using CodexThemeStudio.Storage;
using Microsoft.Data.Sqlite;

namespace CodexThemeStudio.Storage.Tests;

public class SqliteThemeRepositoryTests
{
    [Fact]
    public async Task ConcurrentWrites_PreserveEveryThemeAndValidDatabase()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var repository = environment.CreateRepository();
        var themes = Enumerable.Range(0, 20)
            .Select(index => StorageTestEnvironment.CreateTheme($"并发主题 {index:00}"))
            .ToArray();

        var results = await Task.WhenAll(
            themes.Select(theme => repository.SaveAsync(
                theme,
                new ThemeCreateOptions(),
                CancellationToken.None)));
        var listed = await repository.ListAsync(CancellationToken.None);

        Assert.All(results, result => Assert.True(result.IsSuccess, result.Error?.DiagnosticCode));
        Assert.True(listed.IsSuccess, listed.Error?.DiagnosticCode);
        Assert.Equal(themes.Length, listed.Value!.Count);
        Assert.Equal(
            themes.Select(theme => theme.Id).Order(),
            listed.Value.Select(theme => theme.ThemeId).Order());
    }

    [Fact]
    public async Task Save_AllowsDuplicateChineseAndEmojiNamesWithDifferentIds()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var repository = environment.CreateRepository();
        var first = StorageTestEnvironment.CreateTheme("同名主题 🌌");
        var second = StorageTestEnvironment.CreateTheme("同名主题 🌌");

        var firstSave = await repository.SaveAsync(
            first,
            new ThemeCreateOptions(Tags: ["中文", "Emoji✨"]),
            CancellationToken.None);
        var secondSave = await repository.SaveAsync(
            second,
            new ThemeCreateOptions(),
            CancellationToken.None);
        var themes = await repository.ListAsync(CancellationToken.None);

        Assert.True(firstSave.IsSuccess);
        Assert.True(secondSave.IsSuccess);
        Assert.Equal(2, themes.Value!.Count);
        Assert.Equal(2, themes.Value.Select(theme => theme.ThemeId).Distinct().Count());
        Assert.All(
            themes.Value,
            theme => Assert.Equal("同名主题 🌌", theme.DisplayName));
    }

    [Fact]
    public async Task RenameAndCopy_PreserveValidThemeAndCopyExistingArt()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var repository = environment.CreateRepository();
        var source = StorageTestEnvironment.CreateTheme("Original");
        Assert.True(
            (await repository.SaveAsync(
                source,
                new ThemeCreateOptions(),
                CancellationToken.None)).IsSuccess);

        var assetStore = new FileThemeAssetStore(environment.DataRoot);
        await using var art = new MemoryStream([1, 2, 3, 4]);
        Assert.True(
            (await assetStore.SaveAsync(
                source.Id,
                source.Art.File,
                art,
                CancellationToken.None)).IsSuccess);

        var renamed = await repository.RenameAsync(
            source.Id,
            "重命名主题",
            CancellationToken.None);
        var copyId = Guid.NewGuid();
        var copied = await repository.CopyAsync(
            source.Id,
            copyId,
            "重命名主题",
            CancellationToken.None);
        var copyRead = await repository.GetAsync(copyId, CancellationToken.None);

        Assert.True(renamed.IsSuccess);
        Assert.True(copied.IsSuccess);
        Assert.True(copyRead.IsSuccess);
        Assert.Equal("重命名主题", copyRead.Value!.Name);
        Assert.Equal(copyId, copyRead.Value.Id);
        Assert.True(
            File.Exists(
                Path.Combine(
                    environment.DataRoot,
                    StorageLayout.GetThemeDirectory(copyId),
                    "background.webp")));
    }

    [Fact]
    public async Task MetadataMutations_AreTransactionalAndQueryable()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var repository = environment.CreateRepository();
        var first = StorageTestEnvironment.CreateTheme("First");
        var second = StorageTestEnvironment.CreateTheme("Second");
        Assert.True(
            (await repository.SaveAsync(
                first,
                new ThemeCreateOptions(),
                CancellationToken.None)).IsSuccess);
        Assert.True(
            (await repository.SaveAsync(
                second,
                new ThemeCreateOptions(),
                CancellationToken.None)).IsSuccess);

        Assert.True(
            (await repository.SetFavoriteAsync(
                first.Id,
                true,
                CancellationToken.None)).IsSuccess);
        Assert.True(
            (await repository.SetSortOrderAsync(
                first.Id,
                -10,
                CancellationToken.None)).IsSuccess);
        Assert.True(
            (await repository.SetTagsAsync(
                first.Id,
                ["精选", "夜间"],
                CancellationToken.None)).IsSuccess);
        Assert.True(
            (await repository.SetCurrentPersistentAsync(
                first.Id,
                CancellationToken.None)).IsSuccess);
        Assert.True(
            (await repository.SetCurrentPersistentAsync(
                second.Id,
                CancellationToken.None)).IsSuccess);

        var themes = await repository.ListAsync(CancellationToken.None);
        var themeList = themes.Value!;
        var firstSummary = themeList.Single(theme => theme.ThemeId == first.Id);
        var secondSummary = themeList.Single(theme => theme.ThemeId == second.Id);

        Assert.True(firstSummary.IsFavorite);
        Assert.Equal(-10, firstSummary.SortOrder);
        Assert.Equal(["精选", "夜间"], firstSummary.Tags);
        Assert.False(firstSummary.IsCurrentPersistent);
        Assert.True(secondSummary.IsCurrentPersistent);
    }

    [Fact]
    public async Task RecordApplyResult_UpdatesRecentUseOnlyAfterSuccess()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var repository = environment.CreateRepository();
        var theme = StorageTestEnvironment.CreateTheme("Apply metadata");
        Assert.True(
            (await repository.SaveAsync(
                theme,
                new ThemeCreateOptions(),
                CancellationToken.None)).IsSuccess);

        Assert.True(
            (await repository.RecordApplyResultAsync(
                theme.Id,
                ThemeApplyResult.Failed,
                "应用失败。",
                CancellationToken.None)).IsSuccess);
        var failed = (await repository.ListAsync(CancellationToken.None))
            .Value!
            .Single();

        Assert.Null(failed.LastUsedAtUtc);
        Assert.Equal(ThemeApplyResult.Failed, failed.LastApplyResult);

        Assert.True(
            (await repository.RecordApplyResultAsync(
                theme.Id,
                ThemeApplyResult.Succeeded,
                "应用成功。",
                CancellationToken.None)).IsSuccess);
        var succeeded = (await repository.ListAsync(CancellationToken.None))
            .Value!
            .Single();

        Assert.NotNull(succeeded.LastUsedAtUtc);
        Assert.Equal(ThemeApplyResult.Succeeded, succeeded.LastApplyResult);
        Assert.Equal("应用成功。", succeeded.LastApplyMessage);
    }

    [Fact]
    public async Task Delete_IsSoftAndPreservesThemeDirectory()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var repository = environment.CreateRepository();
        var theme = StorageTestEnvironment.CreateTheme("Soft delete");
        Assert.True(
            (await repository.SaveAsync(
                theme,
                new ThemeCreateOptions(),
                CancellationToken.None)).IsSuccess);
        var themeDirectory = Path.Combine(
            environment.DataRoot,
            StorageLayout.GetThemeDirectory(theme.Id));

        var deleted = await repository.DeleteAsync(theme.Id, CancellationToken.None);
        var themes = await repository.ListAsync(CancellationToken.None);
        var get = await repository.GetAsync(theme.Id, CancellationToken.None);

        Assert.True(deleted.IsSuccess);
        Assert.Empty(themes.Value!);
        Assert.False(get.IsSuccess);
        Assert.True(Directory.Exists(themeDirectory));
        Assert.True(File.Exists(Path.Combine(themeDirectory, StorageLayout.ThemeFileName)));
    }

    [Fact]
    public async Task Save_DoesNotOverwriteNewerSchemaFile()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var repository = environment.CreateRepository();
        var theme = StorageTestEnvironment.CreateTheme("Future");
        Assert.True(
            (await repository.SaveAsync(
                theme,
                new ThemeCreateOptions(),
                CancellationToken.None)).IsSuccess);
        var themePath = Path.Combine(
            environment.DataRoot,
            StorageLayout.GetThemeDocument(theme.Id));
        const string futureJson =
            """{"schemaVersion":99,"id":"00000000-0000-0000-0000-000000000000"}""";
        await File.WriteAllTextAsync(themePath, futureJson);

        var save = await repository.SaveAsync(
            theme with { Name = "Must not overwrite" },
            null,
            CancellationToken.None);

        Assert.False(save.IsSuccess);
        Assert.Equal(OperationErrorCode.UnsupportedVersion, save.Error!.Code);
        Assert.Equal(futureJson, await File.ReadAllTextAsync(themePath));
    }

    [Fact]
    public async Task FailedInsert_DoesNotLeavePartialDatabaseRow()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        await using (var connection = new SqliteConnection(
                         $"Data Source={environment.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TRIGGER fail_theme_insert
                BEFORE INSERT ON themes
                WHEN NEW.name = 'FAIL_TX'
                BEGIN
                    SELECT RAISE(ABORT, 'forced failure');
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var repository = environment.CreateRepository();
        var failedTheme = StorageTestEnvironment.CreateTheme("FAIL_TX");
        var save = await repository.SaveAsync(
            failedTheme,
            new ThemeCreateOptions(),
            CancellationToken.None);
        var list = await repository.ListAsync(CancellationToken.None);

        Assert.False(save.IsSuccess);
        Assert.Equal(OperationErrorCode.StorageUnavailable, save.Error!.Code);
        Assert.Empty(list.Value!);

        var consistency = new StorageConsistencyService(environment.DataRoot);
        var report = await consistency.ScanAsync(CancellationToken.None);
        Assert.Contains(
            StorageLayout.GetThemeDirectory(failedTheme.Id),
            report.Value!.OrphanThemeDirectories);
    }

    [Fact]
    public async Task FailedTagTransaction_RollsBackPreviousTags()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var repository = environment.CreateRepository();
        var theme = StorageTestEnvironment.CreateTheme("Tags");
        Assert.True(
            (await repository.SaveAsync(
                theme,
                new ThemeCreateOptions(Tags: ["original"]),
                CancellationToken.None)).IsSuccess);

        await using (var connection = new SqliteConnection(
                         $"Data Source={environment.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TRIGGER fail_tag_insert
                BEFORE INSERT ON theme_tags
                WHEN NEW.tag = 'boom'
                BEGIN
                    SELECT RAISE(ABORT, 'forced failure');
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var update = await repository.SetTagsAsync(
            theme.Id,
            ["replacement", "boom"],
            CancellationToken.None);
        var themes = await repository.ListAsync(CancellationToken.None);

        Assert.False(update.IsSuccess);
        Assert.Equal(["original"], themes.Value!.Single().Tags);
    }
}
