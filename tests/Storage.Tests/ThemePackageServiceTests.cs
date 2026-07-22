using System.IO.Compression;
using System.Text.Json;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;
using CodexThemeStudio.Storage;
using CodexThemeStudio.ThemeCore;
using SkiaSharp;

namespace CodexThemeStudio.Storage.Tests;

public sealed class ThemePackageServiceTests
{
    [Fact]
    public async Task ExportedPackage_ImportsIntoFreshDataRoot()
    {
        await using var source = await StorageTestEnvironment.CreateAsync();
        var sourceRepository = source.CreateRepository();
        var imagePipeline = new ImagePipeline(source.DataRoot);
        var imageImport = new ThemeImageImportService(
            source.DataRoot,
            imagePipeline,
            sourceRepository);
        var theme = StorageTestEnvironment.CreateTheme("可移植主题包");
        await using var image = new MemoryStream(
            TestImageFactory.Create(SKEncodedImageFormat.Png));
        var created = await imageImport.ImportNewAsync(
            theme,
            image,
            "source.png",
            null,
            CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.DiagnosticCode);

        var package = Path.Combine(source.ContainerRoot, "portable.cttheme");
        var service = new ThemePackageService(
            source.DataRoot,
            sourceRepository,
            imageImport);
        var exported = await service.ExportAsync(
            theme.Id,
            package,
            CancellationToken.None);
        Assert.True(exported.IsSuccess, exported.Error?.DiagnosticCode);

        using (var archive = ZipFile.OpenRead(package))
        {
            Assert.Contains(archive.Entries, entry => entry.FullName == "manifest.json");
            Assert.Contains(archive.Entries, entry => entry.FullName == "theme.json");
            Assert.Contains(archive.Entries, entry => entry.FullName == "background.webp");
        }

        await using var destination = await StorageTestEnvironment.CreateAsync();
        var destinationRepository = destination.CreateRepository();
        var destinationImport = new ThemeImageImportService(
            destination.DataRoot,
            new ImagePipeline(destination.DataRoot),
            destinationRepository);
        var destinationService = new ThemePackageService(
            destination.DataRoot,
            destinationRepository,
            destinationImport);
        var imported = await destinationService.ImportAsync(
            package,
            CancellationToken.None);

        Assert.True(imported.IsSuccess, imported.Error?.DiagnosticCode);
        var importedTheme = await destinationRepository.GetAsync(
            imported.Value!.ThemeId,
            CancellationToken.None);
        Assert.True(importedTheme.IsSuccess);
        Assert.Equal("可移植主题包", importedTheme.Value!.Name);
        Assert.EndsWith(".webp", importedTheme.Value.Art.File, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../escape.js")]
    [InlineData("payload.js")]
    [InlineData("theme.html")]
    public async Task Import_RejectsMaliciousPathAndExecutableContent(string entryName)
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var package = Path.Combine(environment.ContainerRoot, "malicious.cttheme");
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            await WriteEntryAsync(archive, "manifest.json", "{}"u8.ToArray());
            await WriteEntryAsync(archive, "theme.json", "{}"u8.ToArray());
            await WriteEntryAsync(archive, "background.webp", [1]);
            await WriteEntryAsync(archive, entryName, [2]);
        }

        var result = await CreateService(environment).ImportAsync(
            package,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "theme_package.import.entry_rejected",
            result.Error!.DiagnosticCode);
        Assert.False(File.Exists(Path.Combine(environment.ContainerRoot, "escape.js")));
    }

    [Fact]
    public async Task Import_RejectsExpandedPackageBomb()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var package = Path.Combine(environment.ContainerRoot, "bomb.cttheme");
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            await WriteEntryAsync(archive, "manifest.json", "{}"u8.ToArray());
            await WriteEntryAsync(archive, "theme.json", "{}"u8.ToArray());
            var entry = archive.CreateEntry("background.webp", CompressionLevel.SmallestSize);
            await using var output = entry.Open();
            var block = new byte[1024 * 1024];
            for (var index = 0; index < 65; index++)
            {
                await output.WriteAsync(block);
            }
        }

        var result = await CreateService(environment).ImportAsync(
            package,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "theme_package.import.entry_size",
            result.Error!.DiagnosticCode);
    }

    [Fact]
    public async Task Import_RejectsUnknownNewerPackageSchema()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var package = Path.Combine(environment.ContainerRoot, "newer.cttheme");
        var manifest = new ThemePackageManifest(
            ThemePackageService.CurrentPackageSchemaVersion + 1,
            1,
            Guid.NewGuid(),
            []);
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            await WriteEntryAsync(
                archive,
                "manifest.json",
                JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                }));
            await WriteEntryAsync(archive, "theme.json", "{}"u8.ToArray());
            await WriteEntryAsync(archive, "background.webp", [1]);
        }

        var result = await CreateService(environment).ImportAsync(
            package,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.UnsupportedVersion, result.Error!.Code);
    }

    [Fact]
    public async Task Import_RejectsCorruptedHashAndCancellationCleansStaging()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var package = Path.Combine(environment.ContainerRoot, "corrupted.cttheme");
        var theme = StorageTestEnvironment.CreateTheme("损坏校验包");
        var themeBytes = new ThemeDocumentSerializer().Serialize(theme).Value!;
        var background = new byte[] { 1, 2, 3, 4 };
        var manifest = new ThemePackageManifest(
            1,
            1,
            theme.Id,
            [
                new ThemePackageFile(
                    "background.webp",
                    background.Length,
                    new string('0', 64)),
                new ThemePackageFile(
                    "theme.json",
                    themeBytes.Length,
                    Convert.ToHexString(
                            System.Security.Cryptography.SHA256.HashData(themeBytes))
                        .ToLowerInvariant()),
            ]);
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            await WriteEntryAsync(
                archive,
                "manifest.json",
                JsonSerializer.SerializeToUtf8Bytes(manifest, jsonOptions));
            await WriteEntryAsync(archive, "theme.json", themeBytes);
            await WriteEntryAsync(archive, "background.webp", background);
        }

        var service = CreateService(environment);
        var corrupted = await service.ImportAsync(package, CancellationToken.None);
        Assert.False(corrupted.IsSuccess);
        Assert.Equal(
            "theme_package.import.hash_mismatch",
            corrupted.Error!.DiagnosticCode);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await service.ImportAsync(package, cancellation.Token);
        Assert.False(cancelled.IsSuccess);
        Assert.Equal(OperationErrorCode.Cancelled, cancelled.Error!.Code);
        var stagingRoot = Path.Combine(
            environment.DataRoot,
            StorageLayout.RuntimeDirectory,
            "theme-imports");
        Assert.True(
            !Directory.Exists(stagingRoot) ||
            !Directory.EnumerateFileSystemEntries(stagingRoot).Any());
    }

    private static ThemePackageService CreateService(StorageTestEnvironment environment)
    {
        var repository = environment.CreateRepository();
        return new ThemePackageService(
            environment.DataRoot,
            repository,
            new ThemeImageImportService(
                environment.DataRoot,
                new ImagePipeline(environment.DataRoot),
                repository));
    }

    private static async Task WriteEntryAsync(
        ZipArchive archive,
        string name,
        byte[] bytes)
    {
        var entry = archive.CreateEntry(name);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes);
    }
}
