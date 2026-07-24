using CodexThemeStudio.CodexAdapter;

namespace CodexThemeStudio.CodexAdapter.Tests;

public sealed class CodexTargetSelectionServiceTests
{
    [Fact]
    public async Task Resolve_WithoutSavedSelectionReturnsAutomaticMode()
    {
        var configuration = Path.Combine(
            Path.GetTempPath(),
            "cts-target-" + Guid.NewGuid().ToString("N"),
            "codex-target.json");
        var service = new CodexTargetSelectionService(configuration);

        var result = await service.ResolveAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Select_RemembersAcknowledgementUntilExecutableChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), "cts-target-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var executable = Path.Combine(root, "Codex.exe");
            var configuration = Path.Combine(root, "codex-target.json");
            await File.WriteAllBytesAsync(executable, [1, 2, 3]);
            var service = new CodexTargetSelectionService(configuration);

            var selected = await service.SelectAsync(
                executable,
                acknowledgeUnverifiedSource: true,
                CancellationToken.None);
            Assert.True(selected.IsSuccess);
            Assert.True(selected.Value!.SourceAcknowledged);

            await File.WriteAllBytesAsync(executable, [4, 5, 6]);
            var changed = await service.GetStatusAsync(CancellationToken.None);

            Assert.True(changed.IsSuccess);
            Assert.False(changed.Value!.SourceAcknowledged);
            Assert.NotEqual(
                selected.Value.ExecutableSha256,
                changed.Value.ExecutableSha256);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Reset_MovesSelectionAsideAndRestoresAutomaticMode()
    {
        var root = Path.Combine(Path.GetTempPath(), "cts-target-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var executable = Path.Combine(root, "Codex.exe");
            var configuration = Path.Combine(root, "codex-target.json");
            await File.WriteAllBytesAsync(executable, [1]);
            var service = new CodexTargetSelectionService(configuration);
            await service.SelectAsync(executable, true, CancellationToken.None);

            var reset = await service.ResetAsync(CancellationToken.None);

            Assert.True(reset.IsSuccess);
            Assert.False(reset.Value!.HasManualSelection);
            Assert.False(File.Exists(configuration));
            Assert.Single(Directory.GetFiles(root, "codex-target.json.automatic-*"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
