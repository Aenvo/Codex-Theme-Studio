using System.Diagnostics;
using System.Text.Json;
using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Update;

public sealed class TransactionalUpdateInstaller(
    TransactionalUpdateInstallerOptions options) : IUpdateInstaller
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<OperationResult<UpdateInstallResult>> StartAsync(
        StagedUpdate stagedUpdate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stagedUpdate);
        var zipBytes = new FileInfo(stagedUpdate.ZipPath).Length;
        var preflight = UpdatePreflightValidator.Check(
            options.ApplicationRoot,
            options.UpdatesRoot,
            zipBytes,
            stagedUpdate.ExpandedBytes);
        if (!preflight.IsSuccess)
        {
            return OperationResult<UpdateInstallResult>.Failure(preflight.Error!);
        }

        var token = Guid.NewGuid().ToString("N");
        var appRoot = Path.GetFullPath(options.ApplicationRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Directory.GetParent(appRoot)!.FullName;
        var sameVolumeStaging = Path.Combine(parent, $".CodexThemeStudio.update-{token}");
        var backup = Path.Combine(parent, $".CodexThemeStudio.backup-{token}");
        var failed = Path.Combine(parent, $".CodexThemeStudio.failed-{token}");
        var runner = Path.Combine(options.UpdatesRoot, "runner", token);
        var requests = Path.Combine(options.UpdatesRoot, "requests");
        var results = Path.Combine(options.UpdatesRoot, "results");
        var health = Path.Combine(options.UpdatesRoot, "health");
        var preserved = Path.Combine(options.UpdatesRoot, "Preserved");

        try
        {
            if (Directory.Exists(sameVolumeStaging) || Directory.Exists(backup))
            {
                throw new IOException("Updater target already exists.");
            }

            await CopyDirectoryAsync(
                stagedUpdate.StagingRoot,
                sameVolumeStaging,
                cancellationToken);
            Directory.CreateDirectory(runner);
            Directory.CreateDirectory(requests);
            Directory.CreateDirectory(results);
            Directory.CreateDirectory(health);
            Directory.CreateDirectory(preserved);
            File.Copy(options.NodeExecutablePath, Path.Combine(runner, "node.exe"), overwrite: false);
            File.Copy(options.UpdaterScriptPath, Path.Combine(runner, "apply-update.mjs"), overwrite: false);

            var requestPath = Path.Combine(requests, $"{token}.json");
            var request = new
            {
                schemaVersion = 1,
                token,
                currentVersion = options.CurrentVersion,
                targetVersion = stagedUpdate.Release.Version,
                applicationRoot = appRoot,
                stagingRoot = sameVolumeStaging,
                backupDirectory = backup,
                failedDirectory = failed,
                executableRelativePath = options.ExecutableRelativePath,
                zipSha256 = stagedUpdate.ZipSha256,
                currentProcessId = Environment.ProcessId,
                resultPath = Path.Combine(results, $"{token}.json"),
                healthPath = Path.Combine(health, $"{token}.json"),
                preservedRoot = preserved,
            };
            await File.WriteAllTextAsync(
                requestPath,
                JsonSerializer.Serialize(request, JsonOptions),
                cancellationToken);

            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(runner, "node.exe"),
                WorkingDirectory = runner,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add(Path.Combine(runner, "apply-update.mjs"));
            startInfo.ArgumentList.Add(token);
            _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Updater did not start.");

            return OperationResult<UpdateInstallResult>.Success(
                new UpdateInstallResult(
                    UpdateInstallOutcome.Started,
                    options.CurrentVersion,
                    stagedUpdate.Release.Version,
                    "下载完成，正在重启更新…"));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or JsonException)
        {
            TryDeleteDirectory(sameVolumeStaging);
            TryDeleteDirectory(runner);
            return OperationResult<UpdateInstallResult>.Failure(
                OperationErrorCode.ExternalToolFailure,
                "无法启动外部更新程序；未修改任何程序文件。",
                $"update.installer.{exception.GetType().Name}");
        }
    }

    public async Task<OperationResult<UpdateInstallResult>> RetryCleanupAsync(
        string token,
        CancellationToken cancellationToken)
    {
        if (token.Length != 32 || token.Any(static character =>
            character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            return OperationResult<UpdateInstallResult>.Failure(
                OperationErrorCode.ValidationFailed,
                "更新清理令牌无效。",
                "update.cleanup.token_invalid");
        }

        var runner = Path.Combine(options.UpdatesRoot, "runner", token);
        var node = Path.Combine(runner, "node.exe");
        var script = Path.Combine(runner, "apply-update.mjs");
        if (!File.Exists(node) || !File.Exists(script))
        {
            return OperationResult<UpdateInstallResult>.Failure(
                OperationErrorCode.NotFound,
                "更新清理程序已不可用，请保留旧目录并前往 GitHub。",
                "update.cleanup.runner_missing");
        }

        try
        {
            var retryDirectory = Path.Combine(options.UpdatesRoot, "retry");
            Directory.CreateDirectory(retryDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(retryDirectory, $"{token}.json"),
                "{\"schemaVersion\":1}",
                cancellationToken);
            var resultPath = Path.Combine(options.UpdatesRoot, "results", $"{token}.json");
            if (File.Exists(resultPath)) File.Delete(resultPath);
            StartRunner(node, script, runner, token);
            var result = await new UpdateStartupCoordinator(options.UpdatesRoot)
                .MarkHealthyAndWaitForResultAsync(token, cancellationToken);
            return result is null
                ? OperationResult<UpdateInstallResult>.Failure(
                    OperationErrorCode.InvalidResponse,
                    "更新清理程序未返回结果。",
                    "update.cleanup.no_result")
                : OperationResult<UpdateInstallResult>.Success(result);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return OperationResult<UpdateInstallResult>.Failure(
                OperationErrorCode.ExternalToolFailure,
                "重试清理失败；旧目录仍保留。",
                $"update.cleanup.{exception.GetType().Name}");
        }
    }

    private static async Task CopyDirectoryAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        var sourceRoot = Path.GetFullPath(source);
        if (!Directory.Exists(sourceRoot) || IsReparsePoint(sourceRoot))
        {
            throw new IOException("Staging root is invalid.");
        }

        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsReparsePoint(directory)) throw new IOException("Staging contains a reparse point.");
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(sourceRoot, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsReparsePoint(file)) throw new IOException("Staging contains a reparse point.");
            var target = Path.Combine(destination, Path.GetRelativePath(sourceRoot, file));
            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, cancellationToken);
        }
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void StartRunner(
        string node,
        string script,
        string workingDirectory,
        string token)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = node,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add(token);
        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Updater did not start.");
    }
}
