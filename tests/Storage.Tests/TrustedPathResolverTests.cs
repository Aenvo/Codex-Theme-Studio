using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;
using CodexThemeStudio.Storage;

namespace CodexThemeStudio.Storage.Tests;

public class TrustedPathResolverTests
{
    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("C:\\outside.txt")]
    [InlineData("themes/../outside.txt")]
    [InlineData("themes/CON/file.txt")]
    public async Task AssetStore_RejectsUnsafePath(string fileName)
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var store = new FileThemeAssetStore(environment.DataRoot);
        await using var content = new MemoryStream([1, 2, 3]);

        var result = await store.SaveAsync(
            Guid.NewGuid(),
            fileName,
            content,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.InvalidPath, result.Error!.Code);
    }

    [Fact]
    public async Task Resolver_RejectsSymbolicLinkOrJunctionWhenSupported()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var outside = Path.Combine(environment.ContainerRoot, "outside");
        Directory.CreateDirectory(outside);
        var link = Path.Combine(
            environment.DataRoot,
            StorageLayout.ThemesDirectory,
            "linked");

        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        catch (IOException)
        {
            return;
        }

        var resolver = new TrustedPathResolver(environment.DataRoot);
        var result = resolver.Resolve("themes/linked/file.txt");

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.InvalidPath, result.Error!.Code);
        Assert.Equal("path.reparse_point", result.Error.DiagnosticCode);
    }

    [Fact]
    public async Task AssetStore_DeleteGeneratedAssets_PermanentlyRemovesNamedFilesAndEmptyDraftDirectory()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var store = new FileThemeAssetStore(environment.DataRoot);
        var themeId = Guid.NewGuid();
        await using var first = new MemoryStream([1, 2, 3]);
        await using var second = new MemoryStream([4, 5, 6]);
        Assert.True((await store.SaveAsync(
            themeId,
            "background-first.webp",
            first,
            CancellationToken.None)).IsSuccess);
        Assert.True((await store.SaveAsync(
            themeId,
            "background-second.webp",
            second,
            CancellationToken.None)).IsSuccess);
        var themeDirectory = Path.Combine(
            environment.DataRoot,
            StorageLayout.GetThemeDirectory(themeId));

        var deleted = await store.DeleteGeneratedAssetsAsync(
            themeId,
            ["background-first.webp", "background-second.webp"],
            deleteEmptyThemeDirectory: true,
            CancellationToken.None);

        Assert.True(deleted.IsSuccess, deleted.Error?.DiagnosticCode);
        Assert.False(Directory.Exists(themeDirectory));
    }

    [Fact]
    public async Task AssetStore_DeleteGeneratedAssets_RefusesCurrentThemeBackground()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var store = new FileThemeAssetStore(environment.DataRoot);
        var repository = environment.CreateRepository();
        var theme = StorageTestEnvironment.CreateTheme("Protected");
        Assert.True((await repository.SaveAsync(
            theme,
            new ThemeCreateOptions(),
            CancellationToken.None)).IsSuccess);
        await using var content = new MemoryStream([1, 2, 3]);
        Assert.True((await store.SaveAsync(
            theme.Id,
            theme.Art.File,
            content,
            CancellationToken.None)).IsSuccess);
        var assetPath = Path.Combine(
            environment.DataRoot,
            StorageLayout.GetThemeDirectory(theme.Id),
            theme.Art.File);

        var deleted = await store.DeleteGeneratedAssetsAsync(
            theme.Id,
            [theme.Art.File],
            deleteEmptyThemeDirectory: false,
            CancellationToken.None);

        Assert.False(deleted.IsSuccess);
        Assert.Equal(
            "asset.delete_generated.asset_still_referenced",
            deleted.Error!.DiagnosticCode);
        Assert.True(File.Exists(assetPath));
    }

    [Fact]
    public async Task AssetStore_DeleteGeneratedAssets_PreservesUnlistedFilesAndNonemptyDirectory()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var store = new FileThemeAssetStore(environment.DataRoot);
        var themeId = Guid.NewGuid();
        await using var generated = new MemoryStream([1, 2, 3]);
        Assert.True((await store.SaveAsync(
            themeId,
            "background-generated.webp",
            generated,
            CancellationToken.None)).IsSuccess);
        var themeDirectory = Path.Combine(
            environment.DataRoot,
            StorageLayout.GetThemeDirectory(themeId));
        var unknownPath = Path.Combine(themeDirectory, "user-note.txt");
        await File.WriteAllTextAsync(unknownPath, "preserve");

        var deleted = await store.DeleteGeneratedAssetsAsync(
            themeId,
            ["background-generated.webp"],
            deleteEmptyThemeDirectory: true,
            CancellationToken.None);

        Assert.True(deleted.IsSuccess, deleted.Error?.DiagnosticCode);
        Assert.True(Directory.Exists(themeDirectory));
        Assert.True(File.Exists(unknownPath));
        Assert.False(File.Exists(Path.Combine(
            themeDirectory,
            "background-generated.webp")));
    }
}
