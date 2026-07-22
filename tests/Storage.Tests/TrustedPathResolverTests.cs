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
}

