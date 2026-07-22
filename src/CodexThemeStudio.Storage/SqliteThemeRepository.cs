using System.Globalization;
using System.Security.Cryptography;
using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;
using CodexThemeStudio.ThemeCore;
using Microsoft.Data.Sqlite;

namespace CodexThemeStudio.Storage;

public sealed class SqliteThemeRepository : IThemeRepository
{
    private const int MaximumThemeDocumentBytes = 512 * 1024;
    private const int MaximumTagCount = 32;
    private const int MaximumTagLength = 40;

    private readonly TrustedPathResolver pathResolver;
    private readonly SqliteConnectionFactory connectionFactory;
    private readonly ThemeDocumentSerializer serializer;
    private readonly TimeProvider timeProvider;

    public SqliteThemeRepository(
        string dataRoot,
        ThemeDocumentSerializer? serializer = null,
        TimeProvider? timeProvider = null)
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
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<OperationResult<IReadOnlyList<ThemeSummary>>> ListAsync(
        CancellationToken cancellationToken) =>
        RunAsync(
            async () =>
            {
                await using var connection =
                    await connectionFactory.OpenAsync(cancellationToken);
                var rows = new List<ThemeRow>();

                await using (var command = connection.CreateCommand())
                {
                    command.CommandText = $"""
                        SELECT {ThemeColumns}
                        FROM themes
                        WHERE deleted_utc IS NULL
                        ORDER BY sort_order, name COLLATE NOCASE, id;
                        """;

                    await using var reader =
                        await command.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        rows.Add(ReadThemeRow(reader));
                    }
                }

                var summaries = new List<ThemeSummary>(rows.Count);
                foreach (var row in rows)
                {
                    var tags = await ReadTagsAsync(
                        connection,
                        row.ThemeId,
                        cancellationToken);
                    summaries.Add(row.ToSummary(tags));
                }

                return OperationResult<IReadOnlyList<ThemeSummary>>.Success(summaries);
            },
            "theme.list",
            cancellationToken);

    public Task<OperationResult<ThemePackage>> GetAsync(
        Guid themeId,
        CancellationToken cancellationToken) =>
        RunAsync(
            async () =>
            {
                if (themeId == Guid.Empty)
                {
                    return OperationResult<ThemePackage>.Failure(
                        OperationErrorCode.ValidationFailed,
                        "主题 ID 不能为空。",
                        "theme.get.id_empty");
                }

                await using var connection =
                    await connectionFactory.OpenAsync(cancellationToken);
                var row = await ReadThemeRowAsync(
                    connection,
                    themeId,
                    cancellationToken);
                if (row is null)
                {
                    return OperationResult<ThemePackage>.Failure(
                        OperationErrorCode.NotFound,
                        "主题不存在。",
                        "theme.get.not_found");
                }

                return await ReadThemeDocumentAsync(
                    row.ThemeDirectoryRelativePath,
                    cancellationToken);
            },
            "theme.get",
            cancellationToken);

    public Task<OperationResult> SaveAsync(
        ThemePackage theme,
        ThemeCreateOptions? createOptions,
        CancellationToken cancellationToken) =>
        RunAsync(
            async () =>
            {
                ArgumentNullException.ThrowIfNull(theme);

                var issues = ThemePackageContractValidator.Validate(theme);
                if (issues.Count > 0)
                {
                    return OperationResult.Failure(
                        OperationErrorCode.ValidationFailed,
                        issues[0].UserMessage,
                        issues[0].Code);
                }

                var tagsResult = ValidateTags(createOptions?.Tags ?? Array.Empty<string>());
                if (!tagsResult.IsSuccess)
                {
                    return OperationResult.Failure(tagsResult.Error!);
                }

                var thumbnailResult = ValidateOptionalDataRootPath(
                    createOptions?.ThumbnailRelativePath);
                if (!thumbnailResult.IsSuccess)
                {
                    return OperationResult.Failure(thumbnailResult.Error!);
                }

                if (createOptions?.SourceIdentifier is { Length: > 512 } ||
                    createOptions?.SourceIdentifier?.Any(char.IsControl) is true)
                {
                    return OperationResult.Failure(
                        OperationErrorCode.ValidationFailed,
                        "来源标识无效或超过 512 个字符。",
                        "theme.source_identifier.invalid");
                }

                var serialized = serializer.Serialize(theme);
                if (!serialized.IsSuccess)
                {
                    return OperationResult.Failure(serialized.Error!);
                }

                var themeDirectoryRelative = StorageLayout.GetThemeDirectory(theme.Id);
                var documentRelative = StorageLayout.GetThemeDocument(theme.Id);
                var directory = pathResolver.Resolve(themeDirectoryRelative);
                var document = pathResolver.Resolve(documentRelative);
                if (!directory.IsSuccess)
                {
                    return OperationResult.Failure(directory.Error!);
                }

                if (!document.IsSuccess)
                {
                    return OperationResult.Failure(document.Error!);
                }

                Directory.CreateDirectory(directory.Value!);

                var trustedDocument = pathResolver.Resolve(documentRelative);
                if (!trustedDocument.IsSuccess)
                {
                    return OperationResult.Failure(trustedDocument.Error!);
                }

                if (File.Exists(trustedDocument.Value))
                {
                    var existing = await ReadThemeFileAsync(
                        trustedDocument.Value!,
                        cancellationToken);
                    if (existing.Status is ThemeDocumentReadStatus.UnsupportedNewerSchema)
                    {
                        return OperationResult.Failure(
                            OperationErrorCode.UnsupportedVersion,
                            "现有主题使用更高版本 Schema，当前版本不会覆盖。",
                            "theme.save.newer_schema");
                    }
                }

                var write = await AtomicFileWriter.WriteAsync(
                    trustedDocument.Value!,
                    serialized.Value!,
                    cancellationToken);
                if (!write.IsSuccess)
                {
                    return write;
                }

                var contentSha256 = Convert
                    .ToHexString(SHA256.HashData(serialized.Value!))
                    .ToLowerInvariant();
                var now = timeProvider.GetUtcNow();

                await using var connection =
                    await connectionFactory.OpenAsync(cancellationToken);
                await using var transaction =
                    (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

                var existingRow = await ReadThemeRowAsync(
                    connection,
                    theme.Id,
                    cancellationToken,
                    transaction,
                    includeDeleted: true);

                if (existingRow?.DeletedUtc is not null)
                {
                    return OperationResult.Failure(
                        OperationErrorCode.Conflict,
                        "该主题已软删除，不能直接覆盖。",
                        "theme.save.soft_deleted");
                }

                if (existingRow is null)
                {
                    await InsertThemeAsync(
                        connection,
                        transaction,
                        theme,
                        createOptions ?? new ThemeCreateOptions(),
                        themeDirectoryRelative,
                        string.IsNullOrEmpty(thumbnailResult.Value)
                            ? null
                            : thumbnailResult.Value,
                        contentSha256,
                        now,
                        cancellationToken);

                    await ReplaceTagsAsync(
                        connection,
                        transaction,
                        theme.Id,
                        tagsResult.Value!,
                        cancellationToken);
                }
                else
                {
                    await using var update = connection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandText = """
                        UPDATE themes
                        SET name = $name,
                            schema_version = $schemaVersion,
                            modified_utc = $modifiedUtc,
                            theme_directory_relative_path = $themeDirectory,
                            thumbnail_relative_path = COALESCE($thumbnailPath, thumbnail_relative_path),
                            content_sha256 = $contentSha256
                        WHERE id = $id AND deleted_utc IS NULL;
                        """;
                    update.Parameters.AddWithValue("$name", theme.Name);
                    update.Parameters.AddWithValue("$schemaVersion", theme.SchemaVersion);
                    update.Parameters.AddWithValue("$modifiedUtc", now.ToString("O"));
                    update.Parameters.AddWithValue(
                        "$themeDirectory",
                        themeDirectoryRelative);
                    update.Parameters.AddWithValue(
                        "$thumbnailPath",
                        string.IsNullOrEmpty(thumbnailResult.Value)
                            ? DBNull.Value
                            : thumbnailResult.Value);
                    update.Parameters.AddWithValue("$contentSha256", contentSha256);
                    update.Parameters.AddWithValue("$id", theme.Id.ToString("D"));
                    await update.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return OperationResult.Success();
            },
            "theme.save",
            cancellationToken);

    public Task<OperationResult<ThemePackage>> CopyAsync(
        Guid sourceThemeId,
        Guid newThemeId,
        string newName,
        CancellationToken cancellationToken) =>
        RunAsync(
            async () =>
            {
                if (newThemeId == Guid.Empty || newThemeId == sourceThemeId)
                {
                    return OperationResult<ThemePackage>.Failure(
                        OperationErrorCode.ValidationFailed,
                        "复制主题必须使用新的非空 UUID。",
                        "theme.copy.id_invalid");
                }

                var source = await GetAsync(sourceThemeId, cancellationToken);
                if (!source.IsSuccess)
                {
                    return OperationResult<ThemePackage>.Failure(source.Error!);
                }

                var copy = source.Value! with
                {
                    Id = newThemeId,
                    Name = newName,
                };
                var issues = ThemePackageContractValidator.Validate(copy);
                if (issues.Count > 0)
                {
                    return OperationResult<ThemePackage>.Failure(
                        OperationErrorCode.ValidationFailed,
                        issues[0].UserMessage,
                        issues[0].Code);
                }

                var assetCopy = await CopyArtIfPresentAsync(
                    source.Value!,
                    copy,
                    cancellationToken);
                if (!assetCopy.IsSuccess)
                {
                    return OperationResult<ThemePackage>.Failure(assetCopy.Error!);
                }

                var save = await SaveAsync(
                    copy,
                    new ThemeCreateOptions(),
                    cancellationToken);
                return save.IsSuccess
                    ? OperationResult<ThemePackage>.Success(copy)
                    : OperationResult<ThemePackage>.Failure(save.Error!);
            },
            "theme.copy",
            cancellationToken);

    public Task<OperationResult> RenameAsync(
        Guid themeId,
        string newName,
        CancellationToken cancellationToken) =>
        RunAsync(
            async () =>
            {
                var existing = await GetAsync(themeId, cancellationToken);
                if (!existing.IsSuccess)
                {
                    return OperationResult.Failure(existing.Error!);
                }

                return await SaveAsync(
                    existing.Value! with { Name = newName },
                    null,
                    cancellationToken);
            },
            "theme.rename",
            cancellationToken);

    public Task<OperationResult> SetFavoriteAsync(
        Guid themeId,
        bool isFavorite,
        CancellationToken cancellationToken) =>
        UpdateSingleAsync(
            themeId,
            "is_favorite",
            isFavorite ? 1 : 0,
            "theme.favorite",
            cancellationToken);

    public Task<OperationResult> SetSortOrderAsync(
        Guid themeId,
        int sortOrder,
        CancellationToken cancellationToken) =>
        UpdateSingleAsync(
            themeId,
            "sort_order",
            sortOrder,
            "theme.sort_order",
            cancellationToken);

    public Task<OperationResult> SetTagsAsync(
        Guid themeId,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken) =>
        RunAsync(
            async () =>
            {
                var validated = ValidateTags(tags);
                if (!validated.IsSuccess)
                {
                    return OperationResult.Failure(validated.Error!);
                }

                await using var connection =
                    await connectionFactory.OpenAsync(cancellationToken);
                await using var transaction =
                    (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

                if (!await ThemeExistsAsync(
                        connection,
                        transaction,
                        themeId,
                        cancellationToken))
                {
                    return OperationResult.Failure(
                        OperationErrorCode.NotFound,
                        "主题不存在。",
                        "theme.tags.not_found");
                }

                await ReplaceTagsAsync(
                    connection,
                    transaction,
                    themeId,
                    validated.Value!,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return OperationResult.Success();
            },
            "theme.tags",
            cancellationToken);

    public Task<OperationResult> SetCurrentPersistentAsync(
        Guid? themeId,
        CancellationToken cancellationToken) =>
        RunAsync(
            async () =>
            {
                await using var connection =
                    await connectionFactory.OpenAsync(cancellationToken);
                await using var transaction =
                    (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

                if (themeId is not null &&
                    !await ThemeExistsAsync(
                        connection,
                        transaction,
                        themeId.Value,
                        cancellationToken))
                {
                    return OperationResult.Failure(
                        OperationErrorCode.NotFound,
                        "要设为持久主题的主题不存在。",
                        "theme.current.not_found");
                }

                await using (var clear = connection.CreateCommand())
                {
                    clear.Transaction = transaction;
                    clear.CommandText = """
                        UPDATE themes
                        SET is_current_persistent = 0
                        WHERE is_current_persistent = 1;
                        """;
                    await clear.ExecuteNonQueryAsync(cancellationToken);
                }

                if (themeId is not null)
                {
                    await using var set = connection.CreateCommand();
                    set.Transaction = transaction;
                    set.CommandText = """
                        UPDATE themes
                        SET is_current_persistent = 1,
                            last_used_utc = $lastUsedUtc
                        WHERE id = $id AND deleted_utc IS NULL;
                        """;
                    set.Parameters.AddWithValue(
                        "$lastUsedUtc",
                        timeProvider.GetUtcNow().ToString("O"));
                    set.Parameters.AddWithValue("$id", themeId.Value.ToString("D"));
                    await set.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return OperationResult.Success();
            },
            "theme.current",
            cancellationToken);

    public Task<OperationResult> RecordApplyResultAsync(
        Guid themeId,
        ThemeApplyResult result,
        string? userMessage,
        CancellationToken cancellationToken) =>
        RunAsync(
            async () =>
            {
                if (themeId == Guid.Empty ||
                    result is ThemeApplyResult.NeverApplied ||
                    userMessage?.Length > 512 ||
                    userMessage?.Any(char.IsControl) is true)
                {
                    return OperationResult.Failure(
                        OperationErrorCode.ValidationFailed,
                        "主题应用结果无效。",
                        "theme.apply_result.invalid");
                }

                await using var connection =
                    await connectionFactory.OpenAsync(cancellationToken);
                await using var transaction =
                    (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE themes
                    SET last_used_utc = CASE
                            WHEN $result = $succeeded THEN $lastUsedUtc
                            ELSE last_used_utc
                        END,
                        last_apply_result = $result,
                        last_apply_message = $message
                    WHERE id = $id AND deleted_utc IS NULL;
                    """;
                command.Parameters.AddWithValue("$result", (int)result);
                command.Parameters.AddWithValue(
                    "$succeeded",
                    (int)ThemeApplyResult.Succeeded);
                command.Parameters.AddWithValue(
                    "$lastUsedUtc",
                    timeProvider.GetUtcNow().ToString("O"));
                command.Parameters.AddWithValue(
                    "$message",
                    (object?)userMessage ?? DBNull.Value);
                command.Parameters.AddWithValue("$id", themeId.ToString("D"));
                var affected = await command.ExecuteNonQueryAsync(cancellationToken);
                if (affected != 1)
                {
                    return OperationResult.Failure(
                        OperationErrorCode.NotFound,
                        "主题不存在。",
                        "theme.apply_result.not_found");
                }

                await transaction.CommitAsync(cancellationToken);
                return OperationResult.Success();
            },
            "theme.apply_result",
            cancellationToken);

    public Task<OperationResult> DeleteAsync(
        Guid themeId,
        CancellationToken cancellationToken) =>
        RunAsync(
            async () =>
            {
                await using var connection =
                    await connectionFactory.OpenAsync(cancellationToken);
                await using var transaction =
                    (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE themes
                    SET deleted_utc = $deletedUtc,
                        is_current_persistent = 0
                    WHERE id = $id AND deleted_utc IS NULL;
                    """;
                command.Parameters.AddWithValue(
                    "$deletedUtc",
                    timeProvider.GetUtcNow().ToString("O"));
                command.Parameters.AddWithValue("$id", themeId.ToString("D"));
                var affected = await command.ExecuteNonQueryAsync(cancellationToken);
                if (affected != 1)
                {
                    return OperationResult.Failure(
                        OperationErrorCode.NotFound,
                        "主题不存在或已经删除。",
                        "theme.delete.not_found");
                }

                await transaction.CommitAsync(cancellationToken);
                return OperationResult.Success();
            },
            "theme.delete",
            cancellationToken);

    private static async Task InsertThemeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ThemePackage theme,
        ThemeCreateOptions options,
        string themeDirectoryRelative,
        string? thumbnailRelativePath,
        string contentSha256,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO themes(
                id,
                name,
                schema_version,
                created_utc,
                modified_utc,
                last_used_utc,
                is_favorite,
                sort_order,
                source_type,
                source_identifier,
                source_read_only,
                theme_directory_relative_path,
                thumbnail_relative_path,
                content_sha256,
                is_current_persistent,
                compatibility_status,
                last_apply_result,
                last_apply_message,
                deleted_utc)
            VALUES(
                $id,
                $name,
                $schemaVersion,
                $createdUtc,
                $modifiedUtc,
                NULL,
                0,
                0,
                $sourceType,
                $sourceIdentifier,
                $sourceReadOnly,
                $themeDirectory,
                $thumbnailPath,
                $contentSha256,
                0,
                $compatibilityStatus,
                $lastApplyResult,
                NULL,
                NULL);
            """;
        command.Parameters.AddWithValue("$id", theme.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", theme.Name);
        command.Parameters.AddWithValue("$schemaVersion", theme.SchemaVersion);
        command.Parameters.AddWithValue("$createdUtc", now.ToString("O"));
        command.Parameters.AddWithValue("$modifiedUtc", now.ToString("O"));
        command.Parameters.AddWithValue("$sourceType", (int)options.SourceType);
        command.Parameters.AddWithValue(
            "$sourceIdentifier",
            (object?)options.SourceIdentifier ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceReadOnly", options.IsSourceReadOnly ? 1 : 0);
        command.Parameters.AddWithValue("$themeDirectory", themeDirectoryRelative);
        command.Parameters.AddWithValue(
            "$thumbnailPath",
            (object?)thumbnailRelativePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$contentSha256", contentSha256);
        command.Parameters.AddWithValue(
            "$compatibilityStatus",
            (int)ThemeCompatibilityStatus.Unknown);
        command.Parameters.AddWithValue(
            "$lastApplyResult",
            (int)ThemeApplyResult.NeverApplied);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private Task<OperationResult> UpdateSingleAsync(
        Guid themeId,
        string columnName,
        object value,
        string diagnosticPrefix,
        CancellationToken cancellationToken) =>
        RunAsync(
            async () =>
            {
                var allowedColumns = new[] { "is_favorite", "sort_order" };
                if (!allowedColumns.Contains(columnName, StringComparer.Ordinal))
                {
                    return OperationResult.Failure(
                        OperationErrorCode.InternalError,
                        "不支持的元数据字段。",
                        $"{diagnosticPrefix}.column_invalid");
                }

                await using var connection =
                    await connectionFactory.OpenAsync(cancellationToken);
                await using var transaction =
                    (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"""
                    UPDATE themes
                    SET {columnName} = $value,
                        modified_utc = $modifiedUtc
                    WHERE id = $id AND deleted_utc IS NULL;
                    """;
                command.Parameters.AddWithValue("$value", value);
                command.Parameters.AddWithValue(
                    "$modifiedUtc",
                    timeProvider.GetUtcNow().ToString("O"));
                command.Parameters.AddWithValue("$id", themeId.ToString("D"));
                var affected = await command.ExecuteNonQueryAsync(cancellationToken);
                if (affected != 1)
                {
                    return OperationResult.Failure(
                        OperationErrorCode.NotFound,
                        "主题不存在。",
                        $"{diagnosticPrefix}.not_found");
                }

                await transaction.CommitAsync(cancellationToken);
                return OperationResult.Success();
            },
            diagnosticPrefix,
            cancellationToken);

    private async Task<OperationResult> CopyArtIfPresentAsync(
        ThemePackage source,
        ThemePackage destination,
        CancellationToken cancellationToken)
    {
        var artPath = RelativePathPolicy.Normalize(source.Art.File);
        if (!artPath.IsSuccess)
        {
            return OperationResult.Failure(artPath.Error!);
        }

        var sourceRelative =
            $"{StorageLayout.GetThemeDirectory(source.Id)}/{artPath.Value}";
        var sourcePath = pathResolver.Resolve(sourceRelative);
        if (!sourcePath.IsSuccess)
        {
            return OperationResult.Failure(sourcePath.Error!);
        }

        if (!File.Exists(sourcePath.Value))
        {
            return OperationResult.Success();
        }

        await using var sourceStream = new FileStream(
            sourcePath.Value!,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var assetStore = new FileThemeAssetStore(pathResolver.DataRoot);
        var copy = await assetStore.SaveAsync(
            destination.Id,
            destination.Art.File,
            sourceStream,
            cancellationToken);
        return copy.IsSuccess
            ? OperationResult.Success()
            : OperationResult.Failure(copy.Error!);
    }

    private async Task<ThemeDocumentReadResult> ReadThemeFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length > MaximumThemeDocumentBytes)
        {
            return new ThemeDocumentReadResult(
                ThemeDocumentReadStatus.Invalid,
                null,
                null,
                [
                    new ValidationIssue(
                        "theme.json.too_large",
                        "theme.json 超过 512 KiB 上限。",
                        ValidationSeverity.Error),
                ]);
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return serializer.Read(bytes);
    }

    private async Task<OperationResult<ThemePackage>> ReadThemeDocumentAsync(
        string themeDirectoryRelative,
        CancellationToken cancellationToken)
    {
        var normalizedDirectory = RelativePathPolicy.Normalize(themeDirectoryRelative);
        if (!normalizedDirectory.IsSuccess)
        {
            return OperationResult<ThemePackage>.Failure(normalizedDirectory.Error!);
        }

        var document = pathResolver.Resolve(
            $"{normalizedDirectory.Value}/{StorageLayout.ThemeFileName}");
        if (!document.IsSuccess)
        {
            return OperationResult<ThemePackage>.Failure(document.Error!);
        }

        if (!File.Exists(document.Value))
        {
            return OperationResult<ThemePackage>.Failure(
                OperationErrorCode.NotFound,
                "主题文件不存在。",
                "theme.document.missing");
        }

        var read = await ReadThemeFileAsync(document.Value!, cancellationToken);
        return read.Status switch
        {
            ThemeDocumentReadStatus.Success =>
                OperationResult<ThemePackage>.Success(read.Theme!),
            ThemeDocumentReadStatus.UnsupportedNewerSchema =>
                OperationResult<ThemePackage>.Failure(
                    OperationErrorCode.UnsupportedVersion,
                    read.Issues[0].UserMessage,
                    read.Issues[0].Code),
            _ =>
                OperationResult<ThemePackage>.Failure(
                    OperationErrorCode.ValidationFailed,
                    read.Issues[0].UserMessage,
                    read.Issues[0].Code),
        };
    }

    private static OperationResult<IReadOnlyList<string>> ValidateTags(
        IReadOnlyList<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        if (tags.Count > MaximumTagCount)
        {
            return OperationResult<IReadOnlyList<string>>.Failure(
                OperationErrorCode.ValidationFailed,
                $"标签不能超过 {MaximumTagCount} 个。",
                "theme.tags.too_many");
        }

        var normalized = new List<string>(tags.Count);
        var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag) ||
                tag.Length > MaximumTagLength ||
                tag.Any(char.IsControl))
            {
                return OperationResult<IReadOnlyList<string>>.Failure(
                    OperationErrorCode.ValidationFailed,
                    $"标签不能为空、不能包含控制字符且不能超过 {MaximumTagLength} 个字符。",
                    "theme.tags.invalid");
            }

            var trimmed = tag.Trim();
            if (!distinct.Add(trimmed))
            {
                return OperationResult<IReadOnlyList<string>>.Failure(
                    OperationErrorCode.ValidationFailed,
                    "标签不能重复。",
                    "theme.tags.duplicate");
            }

            normalized.Add(trimmed);
        }

        return OperationResult<IReadOnlyList<string>>.Success(normalized);
    }

    private OperationResult<string> ValidateOptionalDataRootPath(string? relativePath)
    {
        if (relativePath is null)
        {
            return OperationResult<string>.Success(string.Empty);
        }

        var normalized = RelativePathPolicy.Normalize(relativePath);
        if (!normalized.IsSuccess)
        {
            return OperationResult<string>.Failure(normalized.Error!);
        }

        var resolved = pathResolver.Resolve(normalized.Value!);
        return resolved.IsSuccess
            ? OperationResult<string>.Success(normalized.Value!)
            : OperationResult<string>.Failure(resolved.Error!);
    }

    private static async Task ReplaceTagsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid themeId,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken)
    {
        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM theme_tags WHERE theme_id = $themeId;";
            clear.Parameters.AddWithValue("$themeId", themeId.ToString("D"));
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        for (var position = 0; position < tags.Count; position++)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO theme_tags(theme_id, tag, position)
                VALUES ($themeId, $tag, $position);
                """;
            insert.Parameters.AddWithValue("$themeId", themeId.ToString("D"));
            insert.Parameters.AddWithValue("$tag", tags[position]);
            insert.Parameters.AddWithValue("$position", position);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<bool> ThemeExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid themeId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1
                FROM themes
                WHERE id = $id AND deleted_utc IS NULL);
            """;
        command.Parameters.AddWithValue("$id", themeId.ToString("D"));
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<IReadOnlyList<string>> ReadTagsAsync(
        SqliteConnection connection,
        Guid themeId,
        CancellationToken cancellationToken)
    {
        var tags = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT tag
            FROM theme_tags
            WHERE theme_id = $themeId
            ORDER BY position;
            """;
        command.Parameters.AddWithValue("$themeId", themeId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tags.Add(reader.GetString(0));
        }

        return tags;
    }

    private static async Task<ThemeRow?> ReadThemeRowAsync(
        SqliteConnection connection,
        Guid themeId,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null,
        bool includeDeleted = false)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT {ThemeColumns}
            FROM themes
            WHERE id = $id
              {(includeDeleted ? string.Empty : "AND deleted_utc IS NULL")};
            """;
        command.Parameters.AddWithValue("$id", themeId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadThemeRow(reader)
            : null;
    }

    private static ThemeRow ReadThemeRow(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetInt32(2),
            ParseTimestamp(reader.GetString(3)),
            ParseTimestamp(reader.GetString(4)),
            reader.IsDBNull(5) ? null : ParseTimestamp(reader.GetString(5)),
            reader.GetInt32(6) == 1,
            reader.GetInt32(7),
            (ThemeSourceType)reader.GetInt32(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.GetInt32(10) == 1,
            reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.GetString(13),
            reader.GetInt32(14) == 1,
            (ThemeCompatibilityStatus)reader.GetInt32(15),
            (ThemeApplyResult)reader.GetInt32(16),
            reader.IsDBNull(17) ? null : reader.GetString(17),
            reader.IsDBNull(18) ? null : ParseTimestamp(reader.GetString(18)));

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.ParseExact(
            value,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private static async Task<OperationResult<T>> RunAsync<T>(
        Func<Task<OperationResult<T>>> action,
        string diagnosticPrefix,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await action();
        }
        catch (OperationCanceledException)
        {
            return OperationResult<T>.Failure(
                OperationErrorCode.Cancelled,
                "操作已取消。",
                $"{diagnosticPrefix}.cancelled");
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResult<T>.Failure(
                OperationErrorCode.AccessDenied,
                "没有权限访问主题存储。",
                $"{diagnosticPrefix}.access_denied");
        }
        catch (SqliteException)
        {
            return OperationResult<T>.Failure(
                OperationErrorCode.StorageUnavailable,
                "SQLite 主题索引操作失败。",
                $"{diagnosticPrefix}.sqlite_failure");
        }
        catch (IOException)
        {
            return OperationResult<T>.Failure(
                OperationErrorCode.StorageUnavailable,
                "主题文件存储不可用。",
                $"{diagnosticPrefix}.io_failure");
        }
    }

    private static async Task<OperationResult> RunAsync(
        Func<Task<OperationResult>> action,
        string diagnosticPrefix,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await action();
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Failure(
                OperationErrorCode.Cancelled,
                "操作已取消。",
                $"{diagnosticPrefix}.cancelled");
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResult.Failure(
                OperationErrorCode.AccessDenied,
                "没有权限访问主题存储。",
                $"{diagnosticPrefix}.access_denied");
        }
        catch (SqliteException)
        {
            return OperationResult.Failure(
                OperationErrorCode.StorageUnavailable,
                "SQLite 主题索引操作失败。",
                $"{diagnosticPrefix}.sqlite_failure");
        }
        catch (IOException)
        {
            return OperationResult.Failure(
                OperationErrorCode.StorageUnavailable,
                "主题文件存储不可用。",
                $"{diagnosticPrefix}.io_failure");
        }
    }

    private const string ThemeColumns = """
        id,
        name,
        schema_version,
        created_utc,
        modified_utc,
        last_used_utc,
        is_favorite,
        sort_order,
        source_type,
        source_identifier,
        source_read_only,
        theme_directory_relative_path,
        thumbnail_relative_path,
        content_sha256,
        is_current_persistent,
        compatibility_status,
        last_apply_result,
        last_apply_message,
        deleted_utc
        """;

    private sealed record ThemeRow(
        Guid ThemeId,
        string DisplayName,
        int SchemaVersion,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset ModifiedAtUtc,
        DateTimeOffset? LastUsedAtUtc,
        bool IsFavorite,
        int SortOrder,
        ThemeSourceType SourceType,
        string? SourceIdentifier,
        bool IsSourceReadOnly,
        string ThemeDirectoryRelativePath,
        string? ThumbnailRelativePath,
        string ContentSha256,
        bool IsCurrentPersistent,
        ThemeCompatibilityStatus CompatibilityStatus,
        ThemeApplyResult LastApplyResult,
        string? LastApplyMessage,
        DateTimeOffset? DeletedUtc)
    {
        public ThemeSummary ToSummary(IReadOnlyList<string> tags) =>
            new(
                ThemeId,
                DisplayName,
                SchemaVersion,
                CreatedAtUtc,
                ModifiedAtUtc,
                LastUsedAtUtc,
                IsFavorite,
                SortOrder,
                tags,
                SourceType,
                SourceIdentifier,
                IsSourceReadOnly,
                ThemeDirectoryRelativePath,
                ThumbnailRelativePath,
                ContentSha256,
                IsCurrentPersistent,
                CompatibilityStatus,
                LastApplyResult,
                LastApplyMessage);
    }
}
