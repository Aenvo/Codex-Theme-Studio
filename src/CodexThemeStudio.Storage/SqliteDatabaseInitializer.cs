using CodexThemeStudio.Contracts.Results;
using Microsoft.Data.Sqlite;

namespace CodexThemeStudio.Storage;

public sealed class SqliteDatabaseInitializer
{
    public const int CurrentDatabaseVersion = 1;

    private const string MigrationSql = """
        CREATE TABLE IF NOT EXISTS themes (
            id TEXT PRIMARY KEY NOT NULL,
            name TEXT NOT NULL,
            schema_version INTEGER NOT NULL,
            created_utc TEXT NOT NULL,
            modified_utc TEXT NOT NULL,
            last_used_utc TEXT NULL,
            is_favorite INTEGER NOT NULL DEFAULT 0 CHECK (is_favorite IN (0, 1)),
            sort_order INTEGER NOT NULL DEFAULT 0,
            source_type INTEGER NOT NULL,
            source_identifier TEXT NULL,
            source_read_only INTEGER NOT NULL DEFAULT 0 CHECK (source_read_only IN (0, 1)),
            theme_directory_relative_path TEXT NOT NULL UNIQUE,
            thumbnail_relative_path TEXT NULL,
            content_sha256 TEXT NOT NULL,
            is_current_persistent INTEGER NOT NULL DEFAULT 0 CHECK (is_current_persistent IN (0, 1)),
            last_apply_result INTEGER NOT NULL DEFAULT 0,
            last_apply_message TEXT NULL,
            deleted_utc TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS theme_tags (
            theme_id TEXT NOT NULL,
            tag TEXT NOT NULL,
            position INTEGER NOT NULL,
            PRIMARY KEY (theme_id, tag),
            FOREIGN KEY (theme_id) REFERENCES themes(id) ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS ix_themes_active_sort
            ON themes(deleted_utc, sort_order, name, id);

        CREATE INDEX IF NOT EXISTS ix_theme_tags_theme_position
            ON theme_tags(theme_id, position);

        CREATE UNIQUE INDEX IF NOT EXISTS ux_themes_current_persistent
            ON themes(is_current_persistent)
            WHERE is_current_persistent = 1 AND deleted_utc IS NULL;
        """;

    private readonly TrustedPathResolver pathResolver;
    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteDatabaseInitializer(string dataRoot)
    {
        pathResolver = new TrustedPathResolver(dataRoot);
        var databasePathResult = pathResolver.Resolve(
            $"{StorageLayout.DatabaseDirectory}/{StorageLayout.DatabaseFileName}");
        if (!databasePathResult.IsSuccess)
        {
            throw new ArgumentException(
                databasePathResult.Error!.UserMessage,
                nameof(dataRoot));
        }

        connectionFactory = new SqliteConnectionFactory(databasePathResult.Value!);
    }

    public string DatabasePath => connectionFactory.DatabasePath;

    public async Task<OperationResult> InitializeAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var rootCheck = pathResolver.EnsureRootIsTrusted();
            if (!rootCheck.IsSuccess)
            {
                return rootCheck;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);

            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await EnsureMigrationTableAsync(connection, cancellationToken);

            var version = await GetCurrentVersionAsync(connection, cancellationToken);
            if (version > CurrentDatabaseVersion)
            {
                return OperationResult.Failure(
                    OperationErrorCode.UnsupportedVersion,
                    $"数据库版本 {version} 高于当前支持的版本 {CurrentDatabaseVersion}，不会修改。",
                    "storage.database.newer_version");
            }

            if (version == CurrentDatabaseVersion)
            {
                return OperationResult.Success();
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var migration = connection.CreateCommand())
            {
                migration.Transaction = (SqliteTransaction)transaction;
                migration.CommandText = MigrationSql;
                await migration.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var record = connection.CreateCommand())
            {
                record.Transaction = (SqliteTransaction)transaction;
                record.CommandText = """
                    INSERT INTO schema_migrations(version, applied_utc)
                    VALUES ($version, $appliedUtc);
                    """;
                record.Parameters.AddWithValue("$version", CurrentDatabaseVersion);
                record.Parameters.AddWithValue(
                    "$appliedUtc",
                    DateTimeOffset.UtcNow.ToString("O"));
                await record.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Success();
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Failure(
                OperationErrorCode.Cancelled,
                "数据库初始化已取消。",
                "storage.database.cancelled");
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResult.Failure(
                OperationErrorCode.AccessDenied,
                "没有权限初始化数据库。",
                "storage.database.access_denied");
        }
        catch (SqliteException)
        {
            return OperationResult.Failure(
                OperationErrorCode.StorageUnavailable,
                "SQLite 数据库初始化失败。",
                "storage.database.sqlite_failure");
        }
        catch (IOException)
        {
            return OperationResult.Failure(
                OperationErrorCode.StorageUnavailable,
                "数据库目录不可用。",
                "storage.database.io_failure");
        }
    }

    private static async Task EnsureMigrationTableAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER PRIMARY KEY NOT NULL,
                applied_utc TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> GetCurrentVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }
}
