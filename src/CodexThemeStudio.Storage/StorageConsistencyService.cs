using System.Security.Cryptography;
using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;
using CodexThemeStudio.ThemeCore;
using Microsoft.Data.Sqlite;

namespace CodexThemeStudio.Storage;

public sealed class StorageConsistencyService : IStorageConsistencyService
{
    private readonly TrustedPathResolver pathResolver;
    private readonly SqliteConnectionFactory connectionFactory;
    private readonly ThemeDocumentSerializer serializer;

    public StorageConsistencyService(
        string dataRoot,
        ThemeDocumentSerializer? serializer = null)
    {
        pathResolver = new TrustedPathResolver(dataRoot);
        var databasePath = pathResolver.Resolve(
            $"{StorageLayout.DatabaseDirectory}/{StorageLayout.DatabaseFileName}");
        if (!databasePath.IsSuccess)
        {
            throw new ArgumentException(
                databasePath.Error!.UserMessage,
                nameof(dataRoot));
        }

        connectionFactory = new SqliteConnectionFactory(databasePath.Value!);
        this.serializer = serializer ?? new ThemeDocumentSerializer();
    }

    public async Task<OperationResult<StorageConsistencyReport>> ScanAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var indexedThemes = await ReadIndexedThemesAsync(cancellationToken);
            var missingFiles = new List<Guid>();
            var invalidFiles = new List<string>();
            var expectedDirectories = new HashSet<string>(
                indexedThemes.Select(row => row.ThemeDirectoryRelativePath),
                StringComparer.OrdinalIgnoreCase);

            foreach (var row in indexedThemes)
            {
                var documentRelative =
                    $"{row.ThemeDirectoryRelativePath}/{StorageLayout.ThemeFileName}";
                var document = pathResolver.Resolve(documentRelative);
                if (!document.IsSuccess)
                {
                    invalidFiles.Add(documentRelative);
                    continue;
                }

                if (!File.Exists(document.Value))
                {
                    missingFiles.Add(row.ThemeId);
                    continue;
                }

                var bytes = await File.ReadAllBytesAsync(
                    document.Value!,
                    cancellationToken);
                var read = serializer.Read(bytes);
                var actualHash = Convert
                    .ToHexString(SHA256.HashData(bytes))
                    .ToLowerInvariant();

                if (read.Status is not ThemeDocumentReadStatus.Success ||
                    !string.Equals(
                        actualHash,
                        row.ContentSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    invalidFiles.Add(documentRelative);
                }
            }

            var orphanDirectories = FindOrphanDirectories(expectedDirectories);
            return OperationResult<StorageConsistencyReport>.Success(
                new StorageConsistencyReport(
                    missingFiles,
                    orphanDirectories,
                    invalidFiles));
        }
        catch (OperationCanceledException)
        {
            return OperationResult<StorageConsistencyReport>.Failure(
                OperationErrorCode.Cancelled,
                "一致性扫描已取消。",
                "storage.consistency.cancelled");
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResult<StorageConsistencyReport>.Failure(
                OperationErrorCode.AccessDenied,
                "没有权限扫描主题存储。",
                "storage.consistency.access_denied");
        }
        catch (SqliteException)
        {
            return OperationResult<StorageConsistencyReport>.Failure(
                OperationErrorCode.StorageUnavailable,
                "无法读取 SQLite 主题索引。",
                "storage.consistency.sqlite_failure");
        }
        catch (IOException)
        {
            return OperationResult<StorageConsistencyReport>.Failure(
                OperationErrorCode.StorageUnavailable,
                "无法扫描主题文件。",
                "storage.consistency.io_failure");
        }
    }

    private async Task<IReadOnlyList<IndexedTheme>> ReadIndexedThemesAsync(
        CancellationToken cancellationToken)
    {
        var themes = new List<IndexedTheme>();
        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, theme_directory_relative_path, content_sha256
            FROM themes;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            themes.Add(
                new IndexedTheme(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetString(2)));
        }

        return themes;
    }

    private IReadOnlyList<string> FindOrphanDirectories(
        IReadOnlySet<string> expectedDirectories)
    {
        var themesDirectory = pathResolver.Resolve(StorageLayout.ThemesDirectory);
        if (!themesDirectory.IsSuccess ||
            !Directory.Exists(themesDirectory.Value))
        {
            return Array.Empty<string>();
        }

        var orphanDirectories = new List<string>();
        foreach (var directory in Directory.EnumerateDirectories(
                     themesDirectory.Value!,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            var relative = Path
                .GetRelativePath(pathResolver.DataRoot, directory)
                .Replace(Path.DirectorySeparatorChar, '/');

            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 ||
                !expectedDirectories.Contains(relative))
            {
                orphanDirectories.Add(relative);
            }
        }

        orphanDirectories.Sort(StringComparer.OrdinalIgnoreCase);
        return orphanDirectories;
    }

    private sealed record IndexedTheme(
        Guid ThemeId,
        string ThemeDirectoryRelativePath,
        string ContentSha256);
}

