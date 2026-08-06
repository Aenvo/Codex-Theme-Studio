using System.Diagnostics;
using System.Text.Json;
using CodexThemeStudio.Contracts.Models;

namespace CodexThemeStudio.Update.Tests;

public sealed class TransactionalUpdateInstallerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "cts-installer-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StartAsync_UsesTokenOnlyAndPreparesSameVolumeStaging()
    {
        var node = FindNode();
        var appRoot = Path.Combine(root, "app");
        var stagedRoot = Path.Combine(root, "download", "app");
        var updatesRoot = Path.Combine(root, "local", "Updates");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(stagedRoot);
        await File.WriteAllTextAsync(Path.Combine(appRoot, "CodexThemeManager.exe"), "old");
        await File.WriteAllTextAsync(Path.Combine(stagedRoot, "CodexThemeManager.exe"), "new");
        await File.WriteAllTextAsync(Path.Combine(stagedRoot, "app-install-manifest.json"), "{}");
        var script = Path.Combine(root, "stub.mjs");
        await File.WriteAllTextAsync(
            script,
            "import{writeFileSync}from'node:fs';writeFileSync('args.json',JSON.stringify(process.argv.slice(2)))");
        var release = FakeRelease();
        var zip = Path.Combine(root, "download", "package.zip");
        await File.WriteAllTextAsync(zip, "zip");
        var installer = new TransactionalUpdateInstaller(
            new TransactionalUpdateInstallerOptions
            {
                CurrentVersion = "1.2.2",
                ApplicationRoot = appRoot,
                NodeExecutablePath = node,
                UpdaterScriptPath = script,
                UpdatesRoot = updatesRoot,
            });

        var result = await installer.StartAsync(
            new StagedUpdate(release, stagedRoot, zip, new string('a', 64), 3, Path.Combine(stagedRoot, "app-install-manifest.json")),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.DiagnosticCode);
        Assert.Equal(UpdateInstallOutcome.Started, result.Value!.Outcome);
        var runner = Assert.Single(Directory.GetDirectories(Path.Combine(updatesRoot, "runner")));
        await WaitForFileAsync(Path.Combine(runner, "args.json"));
        var arguments = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(Path.Combine(runner, "args.json")));
        Assert.Single(arguments!);
        Assert.Matches("^[a-f0-9]{32}$", arguments![0]);
        Assert.True(Directory.GetDirectories(root, ".CodexThemeStudio.update-*").Length == 1);
    }

    [Fact]
    public async Task StartupCoordinator_WritesHealthAndReadsStructuredResult()
    {
        const string token = "abcdefabcdefabcdefabcdefabcdefab";
        var updatesRoot = Path.Combine(root, "Updates");
        Directory.CreateDirectory(Path.Combine(updatesRoot, "results"));
        await File.WriteAllTextAsync(
            Path.Combine(updatesRoot, "results", $"{token}.json"),
            "{\"outcome\":\"Succeeded\",\"oldVersion\":\"1.2.2\",\"newVersion\":\"1.3.0\",\"userMessage\":\"ok\"}");

        var result = await new UpdateStartupCoordinator(updatesRoot)
            .MarkHealthyAndWaitForResultAsync(token, CancellationToken.None);

        Assert.Equal(UpdateInstallOutcome.Succeeded, result!.Outcome);
        Assert.True(File.Exists(Path.Combine(updatesRoot, "health", $"{token}.json")));
        Assert.Equal(token, UpdateStartupCoordinator.GetToken(["--update-token", token]));
    }

    public void Dispose()
    {
        for (var attempt = 0; attempt < 20 && Directory.Exists(root); attempt++)
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { Thread.Sleep(100); }
            catch (UnauthorizedAccessException) { Thread.Sleep(100); }
        }
    }

    private static UpdateReleaseInfo FakeRelease() =>
        new(
            "1.3.0",
            "v1.3.0",
            new Uri("https://github.com/Aenvo/Codex-Theme-Studio/releases/tag/v1.3.0"),
            DateTimeOffset.UtcNow,
            "notes",
            []);

    private static string FindNode()
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "where.exe",
            Arguments = "node.exe",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        var path = process.StandardOutput.ReadLine();
        process.WaitForExit();
        return File.Exists(path) ? path : throw new InvalidOperationException("Node.js is unavailable.");
    }

    private static async Task WaitForFileAsync(string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!File.Exists(path))
        {
            await Task.Delay(25, timeout.Token);
        }
    }
}
