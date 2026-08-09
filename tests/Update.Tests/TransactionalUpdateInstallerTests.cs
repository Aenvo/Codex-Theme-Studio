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
        var updatesRoot = Path.Combine(root, "local", "Updates");
        var downloadRoot = Path.Combine(
            updatesRoot,
            "staging",
            "0123456789abcdef0123456789abcdef");
        var stagedRoot = Path.Combine(downloadRoot, "app");
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
        var zip = Path.Combine(downloadRoot, "package.zip");
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
        Assert.False(Directory.Exists(downloadRoot));
    }

    [Fact]
    public async Task StartAsync_RejectsAndPreservesUnownedDownloadDirectory()
    {
        var node = FindNode();
        var appRoot = Path.Combine(root, "app");
        var downloadRoot = Path.Combine(root, "outside-staging");
        var stagedRoot = Path.Combine(downloadRoot, "app");
        var updatesRoot = Path.Combine(root, "local", "Updates");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(stagedRoot);
        await File.WriteAllTextAsync(Path.Combine(appRoot, "CodexThemeManager.exe"), "old");
        await File.WriteAllTextAsync(Path.Combine(stagedRoot, "CodexThemeManager.exe"), "new");
        await File.WriteAllTextAsync(Path.Combine(stagedRoot, "app-install-manifest.json"), "{}");
        var zip = Path.Combine(downloadRoot, "package.zip");
        await File.WriteAllTextAsync(zip, "zip");
        var script = Path.Combine(root, "stub.mjs");
        await File.WriteAllTextAsync(script, string.Empty);
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
            new StagedUpdate(
                FakeRelease(),
                stagedRoot,
                zip,
                new string('a', 64),
                3,
                Path.Combine(stagedRoot, "app-install-manifest.json")),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(Directory.Exists(downloadRoot));
        Assert.True(File.Exists(zip));
    }

    [Fact]
    public async Task StartupCoordinator_WritesHealthAndReadsStructuredResult()
    {
        const string token = "abcdefabcdefabcdefabcdefabcdefab";
        var updatesRoot = Path.Combine(root, "Updates");
        Directory.CreateDirectory(Path.Combine(updatesRoot, "results"));
        await File.WriteAllTextAsync(
            Path.Combine(updatesRoot, "results", $"{token}.json"),
            $"{{\"schemaVersion\":1,\"token\":\"{token}\",\"outcome\":\"Succeeded\",\"oldVersion\":\"1.2.2\",\"newVersion\":\"1.3.0\",\"userMessage\":\"ok\"}}");

        var result = await new UpdateStartupCoordinator(updatesRoot)
            .MarkHealthyAndWaitForResultAsync(token, CancellationToken.None);

        Assert.Equal(UpdateInstallOutcome.Succeeded, result!.Outcome);
        Assert.True(File.Exists(Path.Combine(updatesRoot, "health", $"{token}.json")));
        Assert.Equal(token, UpdateStartupCoordinator.GetToken(["--update-token", token]));
    }

    [Fact]
    public async Task StartupCoordinator_CleansTerminalArtifactsAndRolledBackVersion()
    {
        const string token = "abcdefabcdefabcdefabcdefabcdefab";
        var updatesRoot = Path.Combine(root, "Updates");
        var appRoot = Path.Combine(root, "Codex Theme Studio");
        var failed = Path.Combine(root, $".CodexThemeStudio.failed-{token}");
        var preserved = Path.Combine(updatesRoot, "Preserved", "keep");
        Directory.CreateDirectory(Path.Combine(updatesRoot, "runner", token));
        Directory.CreateDirectory(Path.Combine(updatesRoot, "requests"));
        Directory.CreateDirectory(Path.Combine(updatesRoot, "results"));
        Directory.CreateDirectory(Path.Combine(updatesRoot, "health"));
        Directory.CreateDirectory(Path.Combine(updatesRoot, "retry"));
        Directory.CreateDirectory(failed);
        Directory.CreateDirectory(preserved);
        await File.WriteAllTextAsync(Path.Combine(failed, "new.txt"), "failed new version");
        await File.WriteAllTextAsync(Path.Combine(preserved, "user.txt"), "keep");
        await File.WriteAllTextAsync(
            Path.Combine(updatesRoot, "requests", $"{token}.json"),
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                token,
                applicationRoot = appRoot,
                failedDirectory = failed,
            }));
        foreach (var directory in new[] { "results", "health", "retry" })
        {
            await File.WriteAllTextAsync(
                Path.Combine(updatesRoot, directory, $"{token}.json"),
                "{}");
        }

        var result = await new UpdateStartupCoordinator(updatesRoot, appRoot)
            .CleanupTerminalArtifactsAsync(
                new UpdateInstallResult(
                    UpdateInstallOutcome.RolledBack,
                    "1.2.2",
                    "1.3.0",
                    "rolled back",
                    Token: token),
                CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.DiagnosticCode);
        Assert.False(Directory.Exists(failed));
        Assert.False(Directory.Exists(Path.Combine(updatesRoot, "runner", token)));
        Assert.False(File.Exists(Path.Combine(updatesRoot, "requests", $"{token}.json")));
        Assert.False(File.Exists(Path.Combine(updatesRoot, "results", $"{token}.json")));
        Assert.False(File.Exists(Path.Combine(updatesRoot, "health", $"{token}.json")));
        Assert.False(File.Exists(Path.Combine(updatesRoot, "retry", $"{token}.json")));
        Assert.True(File.Exists(Path.Combine(preserved, "user.txt")));
    }

    [Fact]
    public async Task StartupCoordinator_RetriesOldTerminalCleanupButSkipsCurrentTransaction()
    {
        const string currentToken = "0123456789abcdef0123456789abcdef";
        const string oldToken = "abcdefabcdefabcdefabcdefabcdefab";
        var updatesRoot = Path.Combine(root, "Updates");
        var resultsRoot = Path.Combine(updatesRoot, "results");
        Directory.CreateDirectory(resultsRoot);
        foreach (var token in new[] { currentToken, oldToken })
        {
            Directory.CreateDirectory(Path.Combine(updatesRoot, "runner", token));
            Directory.CreateDirectory(Path.Combine(updatesRoot, "requests"));
            await File.WriteAllTextAsync(
                Path.Combine(updatesRoot, "requests", $"{token}.json"),
                "{}");
            await File.WriteAllTextAsync(
                Path.Combine(resultsRoot, $"{token}.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    token,
                    outcome = "Succeeded",
                    oldVersion = "1.2.2",
                    newVersion = "1.3.0",
                    userMessage = "ok",
                }));
        }

        var result = await new UpdateStartupCoordinator(updatesRoot, root)
            .RetryTerminalArtifactCleanupAsync(currentToken, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.DiagnosticCode);
        Assert.Equal(1, result.Value);
        Assert.True(File.Exists(Path.Combine(resultsRoot, $"{currentToken}.json")));
        Assert.False(File.Exists(Path.Combine(resultsRoot, $"{oldToken}.json")));
        Assert.True(Directory.Exists(Path.Combine(updatesRoot, "runner", currentToken)));
        Assert.False(Directory.Exists(Path.Combine(updatesRoot, "runner", oldToken)));
    }

    [Fact]
    public async Task StartupCoordinator_RejectsRolledBackCleanupForAnotherApplicationRoot()
    {
        const string token = "abcdefabcdefabcdefabcdefabcdefab";
        var updatesRoot = Path.Combine(root, "Updates");
        var expectedAppRoot = Path.Combine(root, "expected", "Codex Theme Studio");
        var otherAppRoot = Path.Combine(root, "other", "Codex Theme Studio");
        var failed = Path.Combine(root, "other", $".CodexThemeStudio.failed-{token}");
        Directory.CreateDirectory(Path.Combine(updatesRoot, "requests"));
        Directory.CreateDirectory(failed);
        await File.WriteAllTextAsync(Path.Combine(failed, "do-not-delete.txt"), "keep");
        await File.WriteAllTextAsync(
            Path.Combine(updatesRoot, "requests", $"{token}.json"),
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                token,
                applicationRoot = otherAppRoot,
                failedDirectory = failed,
            }));

        var result = await new UpdateStartupCoordinator(updatesRoot, expectedAppRoot)
            .CleanupTerminalArtifactsAsync(
                new UpdateInstallResult(
                    UpdateInstallOutcome.RolledBack,
                    "1.2.2",
                    "1.3.0",
                    "rolled back",
                    Token: token),
                CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(File.Exists(Path.Combine(failed, "do-not-delete.txt")));
    }

    [Fact]
    public async Task StartupCoordinator_DeletesOnlyExpiredOwnedDownloads()
    {
        var stagingRoot = Path.Combine(root, "Updates", "staging");
        var expired = Path.Combine(stagingRoot, "0123456789abcdef0123456789abcdef");
        var recent = Path.Combine(stagingRoot, "abcdefabcdefabcdefabcdefabcdefab");
        var unknown = Path.Combine(stagingRoot, "user-folder");
        Directory.CreateDirectory(expired);
        Directory.CreateDirectory(recent);
        Directory.CreateDirectory(unknown);
        Directory.SetLastWriteTimeUtc(expired, DateTime.UtcNow - TimeSpan.FromDays(8));
        Directory.SetLastWriteTimeUtc(recent, DateTime.UtcNow - TimeSpan.FromHours(1));
        Directory.SetLastWriteTimeUtc(unknown, DateTime.UtcNow - TimeSpan.FromDays(30));

        var result = await new UpdateStartupCoordinator(Path.Combine(root, "Updates"))
            .CleanupExpiredDownloadsAsync(TimeSpan.FromDays(7), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.DiagnosticCode);
        Assert.Equal(1, result.Value);
        Assert.False(Directory.Exists(expired));
        Assert.True(Directory.Exists(recent));
        Assert.True(Directory.Exists(unknown));
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
