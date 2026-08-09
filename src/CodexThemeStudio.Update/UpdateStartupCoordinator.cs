using System.Text.Json;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Update;

public sealed class UpdateStartupCoordinator
{
    private static readonly TimeSpan CleanupRetryDelay = TimeSpan.FromMilliseconds(125);
    private readonly string updatesRoot;
    private readonly string applicationRoot;

    public UpdateStartupCoordinator(
        string? updatesRoot = null,
        string? applicationRoot = null)
    {
        this.updatesRoot = Path.GetFullPath(updatesRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexThemeStudio",
            "Updates"));
        this.applicationRoot = Path.GetFullPath(applicationRoot ?? AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static string? GetToken(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index + 1 < arguments.Count; index++)
        {
            if (arguments[index] == "--update-token" && IsToken(arguments[index + 1]))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }

    public async Task<UpdateInstallResult?> MarkHealthyAndWaitForResultAsync(
        string token,
        CancellationToken cancellationToken)
    {
        if (!IsToken(token)) return null;
        var healthDirectory = Path.Combine(updatesRoot, "health");
        Directory.CreateDirectory(healthDirectory);
        var healthPath = Path.Combine(healthDirectory, $"{token}.json");
        var temporary = healthPath + ".tmp";
        await File.WriteAllTextAsync(
            temporary,
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                token,
                processId = Environment.ProcessId,
                healthyAtUtc = DateTimeOffset.UtcNow,
            }),
            cancellationToken);
        File.Move(temporary, healthPath, overwrite: true);

        var resultPath = Path.Combine(updatesRoot, "results", $"{token}.json");
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(125);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(resultPath))
            {
                await using var stream = new FileStream(resultPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                return ParseResult(document.RootElement, token);
            }

            await Task.Delay(250, cancellationToken);
        }

        return new UpdateInstallResult(
            UpdateInstallOutcome.Failed,
            "unknown",
            "unknown",
            "更新程序未返回结果，请检查更新目录。",
            BackupDirectory: Path.Combine(updatesRoot, "runner", token));
    }

    public async Task<OperationResult> CleanupTerminalArtifactsAsync(
        UpdateInstallResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Outcome is not (
                UpdateInstallOutcome.Succeeded or
                UpdateInstallOutcome.CleanupCompleted or
                UpdateInstallOutcome.RolledBack))
        {
            return OperationResult.Success();
        }

        if (result.Token is null || !IsToken(result.Token))
        {
            return CleanupFailure("更新清理令牌无效。", "update.artifacts.token_invalid");
        }

        var token = result.Token;
        var requestPath = Path.Combine(updatesRoot, "requests", $"{token}.json");
        try
        {
            if (result.Outcome == UpdateInstallOutcome.RolledBack)
            {
                var failedDirectory = await ReadFailedDirectoryAsync(
                    requestPath,
                    token,
                    applicationRoot,
                    cancellationToken);
                await DeleteDirectoryWithRetryAsync(failedDirectory, cancellationToken);
            }

            await DeleteDirectoryWithRetryAsync(
                Path.Combine(updatesRoot, "runner", token),
                cancellationToken);
            foreach (var path in new[]
            {
                Path.Combine(updatesRoot, "health", $"{token}.json"),
                Path.Combine(updatesRoot, "retry", $"{token}.json"),
                Path.Combine(updatesRoot, "results", $"{token}.json"),
                requestPath,
            })
            {
                await DeleteFileWithRetryAsync(path, cancellationToken);
            }

            return OperationResult.Success();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                JsonException or InvalidDataException)
        {
            return CleanupFailure(
                "更新已处理，但临时文件清理未完成；下次启动前请保留 Updates 目录。",
                $"update.artifacts.{exception.GetType().Name}");
        }
    }

    public async Task<OperationResult<int>> RetryTerminalArtifactCleanupAsync(
        string? excludedToken,
        CancellationToken cancellationToken)
    {
        var resultsRoot = Path.Combine(updatesRoot, "results");
        if (!Directory.Exists(resultsRoot))
        {
            return OperationResult<int>.Success(0);
        }

        try
        {
            var cleaned = 0;
            foreach (var resultPath in Directory.GetFiles(resultsRoot, "*.json"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var token = Path.GetFileNameWithoutExtension(resultPath);
                if (!IsToken(token) || string.Equals(token, excludedToken, StringComparison.Ordinal))
                {
                    continue;
                }

                UpdateInstallResult result;
                await using (var stream = new FileStream(
                    resultPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite))
                using (var document = await JsonDocument.ParseAsync(
                    stream,
                    new JsonDocumentOptions { MaxDepth = 16 },
                    cancellationToken))
                {
                    result = ParseResult(document.RootElement, token);
                }
                if (result.Outcome is not (
                        UpdateInstallOutcome.Succeeded or
                        UpdateInstallOutcome.CleanupCompleted or
                        UpdateInstallOutcome.RolledBack))
                {
                    continue;
                }

                var cleanup = await CleanupTerminalArtifactsAsync(result, cancellationToken);
                if (!cleanup.IsSuccess)
                {
                    return OperationResult<int>.Failure(cleanup.Error!);
                }

                cleaned++;
            }

            return OperationResult<int>.Success(cleaned);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                JsonException or InvalidDataException)
        {
            return OperationResult<int>.Failure(
                OperationErrorCode.AccessDenied,
                "上次更新的临时文件仍未能清理。",
                $"update.artifacts.retry.{exception.GetType().Name}");
        }
    }

    public Task<OperationResult<int>> CleanupExpiredDownloadsAsync(
        TimeSpan maximumAge,
        CancellationToken cancellationToken)
    {
        if (maximumAge < TimeSpan.FromDays(1))
        {
            return Task.FromResult(OperationResult<int>.Failure(
                OperationErrorCode.ValidationFailed,
                "更新缓存保留时间无效。",
                "update.artifacts.retention_invalid"));
        }

        var stagingRoot = Path.Combine(updatesRoot, "staging");
        if (!Directory.Exists(stagingRoot))
        {
            return Task.FromResult(OperationResult<int>.Success(0));
        }

        try
        {
            var threshold = DateTime.UtcNow - maximumAge;
            var deleted = 0;
            foreach (var directory in Directory.EnumerateDirectories(stagingRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(directory);
                if (!IsToken(name) || Directory.GetLastWriteTimeUtc(directory) > threshold)
                {
                    continue;
                }

                EnsureNoReparsePoints(directory);
                Directory.Delete(directory, recursive: true);
                deleted++;
            }

            return Task.FromResult(OperationResult<int>.Success(deleted));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Task.FromResult(OperationResult<int>.Failure(
                OperationErrorCode.AccessDenied,
                "部分过期更新缓存未能清理。",
                $"update.artifacts.expired.{exception.GetType().Name}"));
        }
    }

    private static UpdateInstallResult ParseResult(JsonElement root, string expectedToken)
    {
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            !string.Equals(
                root.GetProperty("token").GetString(),
                expectedToken,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Update result identity is invalid.");
        }

        var outcomeText = root.GetProperty("outcome").GetString();
        if (!Enum.TryParse<UpdateInstallOutcome>(outcomeText, ignoreCase: true, out var outcome))
        {
            outcome = UpdateInstallOutcome.Failed;
        }

        return new UpdateInstallResult(
            outcome,
            root.GetProperty("oldVersion").GetString() ?? "unknown",
            root.GetProperty("newVersion").GetString() ?? "unknown",
            root.GetProperty("userMessage").GetString() ?? "更新结果未知。",
            root.TryGetProperty("preservedDirectory", out var preserved) ? preserved.GetString() : null,
            root.TryGetProperty("backupDirectory", out var backup) ? backup.GetString() : null,
            root.TryGetProperty("token", out var token) ? token.GetString() : null);
    }

    private static async Task<string> ReadFailedDirectoryAsync(
        string requestPath,
        string token,
        string expectedApplicationRoot,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            requestPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        using var document = await JsonDocument.ParseAsync(
            stream,
            new JsonDocumentOptions { MaxDepth = 16 },
            cancellationToken);
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            !string.Equals(root.GetProperty("token").GetString(), token, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Update request identity is invalid.");
        }

        var applicationRoot = Path.GetFullPath(
                root.GetProperty("applicationRoot").GetString() ?? string.Empty)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(
                applicationRoot,
                expectedApplicationRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Update request application root is invalid.");
        }

        var failedDirectory = Path.GetFullPath(
            root.GetProperty("failedDirectory").GetString() ?? string.Empty);
        var expected = Path.Combine(
            Directory.GetParent(applicationRoot)?.FullName ?? string.Empty,
            $".CodexThemeStudio.failed-{token}");
        if (!string.Equals(failedDirectory, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Failed update directory is outside the expected boundary.");
        }

        return failedDirectory;
    }

    private static async Task DeleteDirectoryWithRetryAsync(
        string path,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(path)) return;
            try
            {
                EnsureNoReparsePoints(path);
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 39)
            {
                await Task.Delay(CleanupRetryDelay, cancellationToken);
            }
            catch (UnauthorizedAccessException) when (attempt < 39)
            {
                await Task.Delay(CleanupRetryDelay, cancellationToken);
            }
        }
    }

    private static async Task DeleteFileWithRetryAsync(
        string path,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path)) return;
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("Update metadata cannot be a reparse point.");
                }

                File.Delete(path);
                return;
            }
            catch (IOException) when (attempt < 19)
            {
                await Task.Delay(CleanupRetryDelay, cancellationToken);
            }
            catch (UnauthorizedAccessException) when (attempt < 19)
            {
                await Task.Delay(CleanupRetryDelay, cancellationToken);
            }
        }
    }

    private static void EnsureNoReparsePoints(string root)
    {
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Update cleanup root cannot be a reparse point.");
        }

        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Update cleanup content cannot contain reparse points.");
            }
        }
    }

    private static OperationResult CleanupFailure(string message, string diagnosticCode) =>
        OperationResult.Failure(
            OperationErrorCode.AccessDenied,
            message,
            diagnosticCode);

    private static bool IsToken(string value) =>
        value.Length == 32 && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
