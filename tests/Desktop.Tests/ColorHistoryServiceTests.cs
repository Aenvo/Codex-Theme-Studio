using System.IO;
using CodexThemeStudio.Desktop.Services;

namespace CodexThemeStudio.Desktop.Tests;

public sealed class ColorHistoryServiceTests
{
    [Fact]
    public async Task History_PersistsNewestUniqueColorsAndCapsAtTwenty()
    {
        var root = Path.Combine(Path.GetTempPath(), $"CodexThemeStudio.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var history = new ColorHistoryService(root);
        for (var index = 0; index < 25; index++)
        {
            history.Record($"#{index:X6}");
        }

        history.Record("#000010");
        await history.FlushAsync(CancellationToken.None);

        var restored = new ColorHistoryService(root);
        await restored.InitializeAsync(CancellationToken.None);

        Assert.Equal(20, restored.Colors.Count);
        Assert.Equal("#000010", restored.Colors[0]);
        Assert.DoesNotContain("#000004", restored.Colors);
    }

    [Fact]
    public async Task History_IgnoresCorruptDocuments()
    {
        var root = Path.Combine(Path.GetTempPath(), $"CodexThemeStudio.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "color-history.json"), "not-json");
        var history = new ColorHistoryService(root);

        await history.InitializeAsync(CancellationToken.None);

        Assert.Empty(history.Colors);
    }

    [Fact]
    public async Task History_IgnoresInvalidColorEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), $"CodexThemeStudio.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, "color-history.json"),
            """{"schemaVersion":3,"colors":["not-a-color","#112233"]}""");
        var history = new ColorHistoryService(root);

        await history.InitializeAsync(CancellationToken.None);

        Assert.Equal(new[] { "#112233" }, history.Colors);
    }

    [Fact]
    public async Task History_IgnoresLegacyRealtimeHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"CodexThemeStudio.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, "color-history.json"),
            """{"schemaVersion":2,"colors":["#FFFFFF","#FEFEFE"]}""");
        var history = new ColorHistoryService(root);

        await history.InitializeAsync(CancellationToken.None);

        Assert.Empty(history.Colors);
    }
}
