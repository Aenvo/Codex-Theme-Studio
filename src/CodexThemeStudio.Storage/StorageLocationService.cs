using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;
using Microsoft.Data.Sqlite;

namespace CodexThemeStudio.Storage;

public sealed class StorageLocationService : IStorageLocationService
{
    private const int BootstrapSchemaVersion = 1;
    private const int MaximumBootstrapBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    private readonly string bootstrapFilePath;
    private readonly SemaphoreSlim migrationGate = new(1, 1);
    private readonly Func<string, bool> writeAccessProbe;
    private readonly Func<string, long?> availableBytesProvider;

    public StorageLocationService(
        string bootstrapFilePath,
        Func<string, bool>? writeAccessProbe = null,
        Func<string, long?>? availableBytesProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bootstrapFilePath);
        if (!Path.IsPathFullyQualified(bootstrapFilePath))
        {
            throw new ArgumentException(
                "Bootstrap path must be absolute.",
                nameof(bootstrapFilePath));
        }

        this.bootstrapFilePath = Path.GetFullPath(bootstrapFilePath);
        this.writeAccessProbe = writeAccessProbe ?? HasWriteAccess;
        this.availableBytesProvider = availableBytesProvider ?? GetAvailableBytes;
    }

    public static StorageLocationService CreateDefault()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return new StorageLocationService(
            Path.Combine(localApplicationData, "CodexThemeStudio", "bootstrap.json"));
    }

    public StorageMigrationReport? LastMigrationReport { get; private set; }

    public async Task<OperationResult<StorageLocationStatus>> InitializeAsync(
        string defaultDataRoot,
        CancellationToken cancellationToken)
    {
        if (File.Exists(bootstrapFilePath))
        {
            return await GetStatusAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(defaultDataRoot) ||
            !Path.IsPathFullyQualified(defaultDataRoot))
        {
            return OperationResult<StorageLocationStatus>.Failure(
                OperationErrorCode.InvalidPath,
                "默认数据目录必须是绝对路径。",
                "storage.bootstrap.data_root.invalid");
        }

        try
        {
            var fullDataRoot = Path.GetFullPath(defaultDataRoot);
            Directory.CreateDirectory(fullDataRoot);

            var pathResolver = new TrustedPathResolver(fullDataRoot);
            var rootCheck = pathResolver.EnsureRootIsTrusted();
            if (!rootCheck.IsSuccess)
            {
                return OperationResult<StorageLocationStatus>.Failure(rootCheck.Error!);
            }

            foreach (var relativeDirectory in StorageLayout.RequiredDirectories)
            {
                var directory = pathResolver.Resolve(relativeDirectory);
                if (!directory.IsSuccess)
                {
                    return OperationResult<StorageLocationStatus>.Failure(directory.Error!);
                }

                Directory.CreateDirectory(directory.Value!);
            }

            var bootstrap = new BootstrapDocument(
                BootstrapSchemaVersion,
                fullDataRoot);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(bootstrap, JsonOptions);
            var write = await AtomicFileWriter.WriteAsync(
                bootstrapFilePath,
                bytes,
                cancellationToken);
            if (!write.IsSuccess)
            {
                return OperationResult<StorageLocationStatus>.Failure(write.Error!);
            }

            return CreateStatus(fullDataRoot);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<StorageLocationStatus>.Failure(
                OperationErrorCode.Cancelled,
                "数据目录初始化已取消。",
                "storage.bootstrap.cancelled");
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResult<StorageLocationStatus>.Failure(
                OperationErrorCode.AccessDenied,
                "没有权限初始化数据目录。",
                "storage.bootstrap.access_denied");
        }
        catch (IOException)
        {
            return OperationResult<StorageLocationStatus>.Failure(
                OperationErrorCode.StorageUnavailable,
                "数据目录初始化失败。",
                "storage.bootstrap.io_failure");
        }
    }

    public async Task<OperationResult<StorageLocationStatus>> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(bootstrapFilePath))
        {
            return OperationResult<StorageLocationStatus>.Failure(
                OperationErrorCode.NotFound,
                "尚未配置数据目录。",
                "storage.bootstrap.missing");
        }

        try
        {
            var fileInfo = new FileInfo(bootstrapFilePath);
            if (fileInfo.Length > MaximumBootstrapBytes)
            {
                return InvalidBootstrap("定位文件超过允许大小。");
            }

            var bytes = await File.ReadAllBytesAsync(
                bootstrapFilePath,
                cancellationToken);
            var bootstrap = JsonSerializer.Deserialize<BootstrapDocument>(
                bytes,
                JsonOptions);

            if (bootstrap is null ||
                bootstrap.SchemaVersion != BootstrapSchemaVersion ||
                string.IsNullOrWhiteSpace(bootstrap.DataRoot) ||
                !Path.IsPathFullyQualified(bootstrap.DataRoot))
            {
                return InvalidBootstrap("数据目录定位文件无效。");
            }

            return CreateStatus(Path.GetFullPath(bootstrap.DataRoot));
        }
        catch (JsonException)
        {
            return InvalidBootstrap("数据目录定位文件已损坏或包含未知字段。");
        }
        catch (OperationCanceledException)
        {
            return OperationResult<StorageLocationStatus>.Failure(
                OperationErrorCode.Cancelled,
                "读取数据目录状态已取消。",
                "storage.bootstrap.cancelled");
        }
        catch (IOException)
        {
            return OperationResult<StorageLocationStatus>.Failure(
                OperationErrorCode.StorageUnavailable,
                "无法读取数据目录定位文件。",
                "storage.bootstrap.io_failure");
        }
    }

    public async Task<OperationResult<StorageLocationStatus>> MigrateAsync(
        string destinationDataRoot,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(destinationDataRoot) ||
            !Path.IsPathFullyQualified(destinationDataRoot))
        {
            return OperationResult<StorageLocationStatus>.Failure(
                OperationErrorCode.InvalidPath,
                "目标数据目录必须是绝对路径。",
                "storage.migration.destination_invalid");
        }

        var gateAcquired = false;
        string? stagingRoot = null;
        LastMigrationReport = null;
        try
        {
            await migrationGate.WaitAsync(cancellationToken);
            gateAcquired = true;
            var current = await GetStatusAsync(cancellationToken);
            if (!current.IsSuccess)
            {
                return OperationResult<StorageLocationStatus>.Failure(current.Error!);
            }

            if (!current.Value!.IsAvailable)
            {
                return OperationResult<StorageLocationStatus>.Failure(
                    OperationErrorCode.StorageUnavailable,
                    "当前数据目录离线；不会创建替代空库或开始迁移。",
                    "storage.migration.source_offline");
            }

            if (!current.Value.IsWritable)
            {
                return OperationResult<StorageLocationStatus>.Failure(
                    OperationErrorCode.AccessDenied,
                    "当前数据目录只读，无法安全暂停写入并迁移。",
                    "storage.migration.source_read_only");
            }

            var sourceRoot = Path.GetFullPath(current.Value.DataRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var destinationRoot = Path.GetFullPath(destinationDataRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(sourceRoot, destinationRoot, StringComparison.OrdinalIgnoreCase) ||
                IsNestedPath(sourceRoot, destinationRoot) ||
                IsNestedPath(destinationRoot, sourceRoot))
            {
                return OperationResult<StorageLocationStatus>.Failure(
                    OperationErrorCode.InvalidPath,
                    "目标目录不能与当前数据目录相同，也不能互相嵌套。",
                    "storage.migration.destination_overlap");
            }

            if (Directory.Exists(destinationRoot) &&
                Directory.EnumerateFileSystemEntries(destinationRoot).Any())
            {
                return OperationResult<StorageLocationStatus>.Failure(
                    OperationErrorCode.Conflict,
                    "目标目录必须不存在或为空，避免覆盖现有数据。",
                    "storage.migration.destination_not_empty");
            }

            var destinationParent = Path.GetDirectoryName(destinationRoot);
            if (string.IsNullOrWhiteSpace(destinationParent))
            {
                return OperationResult<StorageLocationStatus>.Failure(
                    OperationErrorCode.InvalidPath,
                    "无法确定目标目录的父目录。",
                    "storage.migration.parent_invalid");
            }

            Directory.CreateDirectory(destinationParent);
            if (!writeAccessProbe(destinationParent))
            {
                return OperationResult<StorageLocationStatus>.Failure(
                    OperationErrorCode.AccessDenied,
                    "目标位置只读或没有写入权限。",
                    "storage.migration.destination_read_only");
            }

            var checkpoint = await CheckpointDatabaseAsync(
                Path.Combine(
                    sourceRoot,
                    StorageLayout.DatabaseDirectory,
                    StorageLayout.DatabaseFileName),
                cancellationToken);
            if (!checkpoint.IsSuccess)
            {
                return OperationResult<StorageLocationStatus>.Failure(checkpoint.Error!);
            }

            var sourceFiles = EnumerateMigrationFiles(sourceRoot).ToArray();
            var totalBytes = sourceFiles.Sum(file => new FileInfo(file).Length);
            var availableBytes = availableBytesProvider(destinationRoot);
            if (availableBytes is not null &&
                availableBytes.Value < totalBytes + (16L * 1024 * 1024))
            {
                return OperationResult<StorageLocationStatus>.Failure(
                    OperationErrorCode.StorageUnavailable,
                    "目标磁盘空间不足，迁移未开始。",
                    "storage.migration.disk_full");
            }

            stagingRoot = Path.Combine(
                destinationParent,
                $".cts-migration-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingRoot);
            await using var lockStream = new FileStream(
                Path.Combine(sourceRoot, StorageLayout.RuntimeDirectory, "migration.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                1,
                FileOptions.DeleteOnClose);

            foreach (var sourceFile in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(sourceRoot, sourceFile);
                var destinationFile = Path.Combine(stagingRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
                await CopyFileAsync(sourceFile, destinationFile, cancellationToken);
            }

            foreach (var directory in StorageLayout.RequiredDirectories)
            {
                Directory.CreateDirectory(Path.Combine(stagingRoot, directory));
            }

            var sourceFingerprint = await FingerprintAsync(
                sourceRoot,
                sourceFiles.Select(file => Path.GetRelativePath(sourceRoot, file)),
                cancellationToken);
            var copiedFiles = EnumerateMigrationFiles(stagingRoot).ToArray();
            var copiedFingerprint = await FingerprintAsync(
                stagingRoot,
                copiedFiles.Select(file => Path.GetRelativePath(stagingRoot, file)),
                cancellationToken);
            if (sourceFingerprint.FileCount != copiedFingerprint.FileCount ||
                sourceFingerprint.TotalBytes != copiedFingerprint.TotalBytes ||
                !string.Equals(
                    sourceFingerprint.CombinedSha256,
                    copiedFingerprint.CombinedSha256,
                    StringComparison.Ordinal))
            {
                return OperationResult<StorageLocationStatus>.Failure(
                    OperationErrorCode.ValidationFailed,
                    "迁移副本的文件数量、大小或 SHA-256 与源目录不一致。",
                    "storage.migration.fingerprint_mismatch");
            }

            var databasePath = Path.Combine(
                stagingRoot,
                StorageLayout.DatabaseDirectory,
                StorageLayout.DatabaseFileName);
            var integrity = await VerifyDatabaseAsync(databasePath, cancellationToken);
            if (!integrity.IsSuccess)
            {
                return OperationResult<StorageLocationStatus>.Failure(integrity.Error!);
            }

            if (Directory.Exists(destinationRoot))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(stagingRoot))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var destinationEntry = Path.Combine(
                        destinationRoot,
                        Path.GetFileName(entry));
                    if (Directory.Exists(entry))
                    {
                        Directory.Move(entry, destinationEntry);
                    }
                    else
                    {
                        File.Move(entry, destinationEntry);
                    }
                }

                Directory.Delete(stagingRoot);
                stagingRoot = null;
            }
            else
            {
                Directory.Move(stagingRoot, destinationRoot);
                stagingRoot = null;
            }
            var bootstrap = new BootstrapDocument(
                BootstrapSchemaVersion,
                destinationRoot);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(bootstrap, JsonOptions);
            var write = await AtomicFileWriter.WriteAsync(
                bootstrapFilePath,
                bytes,
                cancellationToken);
            if (!write.IsSuccess)
            {
                return OperationResult<StorageLocationStatus>.Failure(write.Error!);
            }

            var status = CreateStatus(destinationRoot);
            if (!status.IsSuccess)
            {
                return status;
            }

            LastMigrationReport = new StorageMigrationReport(
                sourceRoot,
                destinationRoot,
                copiedFingerprint.FileCount,
                copiedFingerprint.TotalBytes,
                copiedFingerprint.CombinedSha256,
                true,
                true,
                true);
            return status;
        }
        catch (OperationCanceledException)
        {
            return OperationResult<StorageLocationStatus>.Failure(
                OperationErrorCode.Cancelled,
                "数据迁移已取消；定位文件仍指向原数据目录。",
                "storage.migration.cancelled");
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResult<StorageLocationStatus>.Failure(
                OperationErrorCode.AccessDenied,
                "没有权限读取源目录或写入目标目录；数据位置未切换。",
                "storage.migration.access_denied");
        }
        catch (IOException exception) when (IsDiskFull(exception))
        {
            return OperationResult<StorageLocationStatus>.Failure(
                OperationErrorCode.StorageUnavailable,
                "目标磁盘空间不足；数据位置未切换。",
                "storage.migration.disk_full");
        }
        catch (IOException)
        {
            return OperationResult<StorageLocationStatus>.Failure(
                OperationErrorCode.StorageUnavailable,
                "迁移被中断或目标存储不可用；数据位置未切换。",
                "storage.migration.io_failure");
        }
        finally
        {
            TryDeleteCreatedDirectory(stagingRoot);
            if (gateAcquired)
            {
                migrationGate.Release();
            }
        }
    }

    private static OperationResult<StorageLocationStatus> CreateStatus(string dataRoot)
    {
        if (!Directory.Exists(dataRoot))
        {
            return OperationResult<StorageLocationStatus>.Success(
                new StorageLocationStatus(
                    dataRoot,
                    false,
                    false,
                    null,
                    "已配置的数据目录当前不可用；不会创建新的空库。"));
        }

        var resolver = new TrustedPathResolver(dataRoot);
        var rootCheck = resolver.EnsureRootIsTrusted();
        if (!rootCheck.IsSuccess)
        {
            return OperationResult<StorageLocationStatus>.Failure(rootCheck.Error!);
        }

        try
        {
            var root = Path.GetPathRoot(dataRoot);
            var drive = string.IsNullOrWhiteSpace(root)
                ? null
                : new DriveInfo(root);
            var isWritable = HasWriteAccess(dataRoot);

            return OperationResult<StorageLocationStatus>.Success(
                new StorageLocationStatus(
                    dataRoot,
                    true,
                    isWritable,
                    drive?.AvailableFreeSpace,
                    isWritable ? "数据目录可用。" : "数据目录只读。"));
        }
        catch (IOException)
        {
            return OperationResult<StorageLocationStatus>.Success(
                new StorageLocationStatus(
                    dataRoot,
                    true,
                    false,
                    null,
                    "无法确认数据目录的可用空间或写入权限。"));
        }
    }

    private static bool HasWriteAccess(string dataRoot)
    {
        var probe = Path.Combine(dataRoot, $".write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            using (File.Open(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
            }

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        finally
        {
            if (File.Exists(probe))
            {
                File.Delete(probe);
            }
        }
    }

    private static long? GetAvailableBytes(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateMigrationFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path =>
                !string.Equals(Path.GetFileName(path), "migration.lock", StringComparison.Ordinal) &&
                !Path.GetFileName(path).Contains(".tmp", StringComparison.Ordinal));

    private static bool IsNestedPath(string parent, string candidate) =>
        candidate.StartsWith(
            parent + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private static async Task CopyFileAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static async Task<Fingerprint> FingerprintAsync(
        string root,
        IEnumerable<string> relativePaths,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        long bytes = 0;
        foreach (var relative in relativePaths
                     .Select(path => path.Replace('\\', '/'))
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            var content = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            bytes += content.LongLength;
            lines.Add($"{relative}\0{content.LongLength}\0{Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()}");
        }

        var combined = SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Join("\n", lines)));
        return new Fingerprint(
            lines.Count,
            bytes,
            Convert.ToHexString(combined).ToLowerInvariant());
    }

    private static async Task<OperationResult> VerifyDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(databasePath))
        {
            return OperationResult.Failure(
                OperationErrorCode.NotFound,
                "迁移副本缺少 SQLite 数据库。",
                "storage.migration.database_missing");
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var result = Convert.ToString(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
        return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase)
            ? OperationResult.Success()
            : OperationResult.Failure(
                OperationErrorCode.ValidationFailed,
                "迁移副本的 SQLite 完整性检查失败。",
                "storage.migration.database_integrity");
    }

    private static async Task<OperationResult> CheckpointDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(databasePath))
        {
            return OperationResult.Failure(
                OperationErrorCode.NotFound,
                "当前数据目录缺少 SQLite 数据库。",
                "storage.migration.source_database_missing");
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 5,
        };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return OperationResult.Success();
    }

    private static bool IsDiskFull(IOException exception)
    {
        var code = exception.HResult & 0xFFFF;
        return code is 0x27 or 0x70;
    }

    private static void TryDeleteCreatedDirectory(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) &&
                Path.GetFileName(path).StartsWith(".cts-migration-", StringComparison.Ordinal) &&
                Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static OperationResult<StorageLocationStatus> InvalidBootstrap(
        string userMessage) =>
        OperationResult<StorageLocationStatus>.Failure(
            OperationErrorCode.ValidationFailed,
            userMessage,
            "storage.bootstrap.invalid");

    private sealed record BootstrapDocument(
        int SchemaVersion,
        string DataRoot);

    private sealed record Fingerprint(
        int FileCount,
        long TotalBytes,
        string CombinedSha256);
}
