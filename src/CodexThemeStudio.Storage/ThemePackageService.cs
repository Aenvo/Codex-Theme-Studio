using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;
using CodexThemeStudio.ThemeCore;

namespace CodexThemeStudio.Storage;

public sealed class ThemePackageService : IThemePackageService
{
    public const int CurrentPackageSchemaVersion = 1;
    public const long MaximumPackageBytes = 32L * 1024 * 1024;
    public const long MaximumExpandedBytes = 64L * 1024 * 1024;
    public const int MaximumEntries = 16;

    private static readonly HashSet<string> AllowedEntries =
        new(StringComparer.Ordinal)
        {
            "manifest.json",
            "theme.json",
            "background.webp",
            "thumbnail.webp",
        };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    private readonly TrustedPathResolver pathResolver;
    private readonly IThemeRepository repository;
    private readonly IThemeImageImportService imageImport;
    private readonly ThemeDocumentSerializer serializer;

    public ThemePackageService(
        string dataRoot,
        IThemeRepository repository,
        IThemeImageImportService imageImport,
        ThemeDocumentSerializer? serializer = null)
    {
        pathResolver = new TrustedPathResolver(dataRoot);
        this.repository = repository;
        this.imageImport = imageImport;
        this.serializer = serializer ?? new ThemeDocumentSerializer();
    }

    public async Task<OperationResult<string>> ExportAsync(
        Guid themeId,
        string destinationFile,
        CancellationToken cancellationToken)
    {
        if (!IsAbsolutePackagePath(destinationFile))
        {
            return Failure<string>(
                OperationErrorCode.InvalidPath,
                "导出路径必须是以 .cttheme 结尾的绝对路径。",
                "theme_package.export.path_invalid");
        }

        var themeResult = await repository.GetAsync(themeId, cancellationToken);
        if (!themeResult.IsSuccess)
        {
            return OperationResult<string>.Failure(themeResult.Error!);
        }

        var theme = themeResult.Value!;
        var themeBytesResult = serializer.Serialize(
            theme with { Art = theme.Art with { File = "background.webp" } });
        if (!themeBytesResult.IsSuccess)
        {
            return OperationResult<string>.Failure(themeBytesResult.Error!);
        }

        var backgroundPath = pathResolver.Resolve(
            $"{StorageLayout.GetThemeDirectory(theme.Id)}/{theme.Art.File}");
        if (!backgroundPath.IsSuccess)
        {
            return OperationResult<string>.Failure(backgroundPath.Error!);
        }

        if (!File.Exists(backgroundPath.Value))
        {
            return Failure<string>(
                OperationErrorCode.NotFound,
                "主题背景文件不存在，无法导出。",
                "theme_package.export.background_missing");
        }

        try
        {
            var backgroundBytes = await ReadBoundedAsync(
                backgroundPath.Value!,
                ImagePipeline.MaximumInputBytes,
                cancellationToken);
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["theme.json"] = themeBytesResult.Value!,
                ["background.webp"] = backgroundBytes,
            };

            var thumbnail = await FindThumbnailAsync(theme.Id, cancellationToken);
            if (thumbnail is not null)
            {
                files["thumbnail.webp"] = await ReadBoundedAsync(
                    thumbnail,
                    ImagePipeline.MaximumInputBytes,
                    cancellationToken);
            }

            var manifest = new ThemePackageManifest(
                CurrentPackageSchemaVersion,
                theme.SchemaVersion,
                theme.Id,
                files.Select(pair => new ThemePackageFile(
                        pair.Key,
                        pair.Value.LongLength,
                        Hash(pair.Value)))
                    .OrderBy(file => file.Path, StringComparer.Ordinal)
                    .ToArray());
            files["manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(
                manifest,
                JsonOptions);

            var destination = Path.GetFullPath(destinationFile);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = $"{destination}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var stream = new FileStream(
                                 temporary,
                                 FileMode.CreateNew,
                                 FileAccess.ReadWrite,
                                 FileShare.None,
                                 64 * 1024,
                                 FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    using var archive = new ZipArchive(
                        stream,
                        ZipArchiveMode.Create,
                        leaveOpen: true);
                    foreach (var pair in files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var entry = archive.CreateEntry(pair.Key, CompressionLevel.Optimal);
                        entry.LastWriteTime = new DateTimeOffset(
                            1980,
                            1,
                            1,
                            0,
                            0,
                            0,
                            TimeSpan.Zero);
                        await using var entryStream = entry.Open();
                        await entryStream.WriteAsync(pair.Value, cancellationToken);
                    }
                }

                File.Move(temporary, destination, overwrite: true);
            }
            finally
            {
                TryDeleteCreatedFile(temporary);
            }

            return OperationResult<string>.Success(destination);
        }
        catch (OperationCanceledException)
        {
            return Failure<string>(
                OperationErrorCode.Cancelled,
                "主题包导出已取消。",
                "theme_package.export.cancelled");
        }
        catch (UnauthorizedAccessException)
        {
            return Failure<string>(
                OperationErrorCode.AccessDenied,
                "没有权限写入导出位置。",
                "theme_package.export.access_denied");
        }
        catch (IOException)
        {
            return Failure<string>(
                OperationErrorCode.StorageUnavailable,
                "主题包导出失败，请检查目标磁盘空间和可用性。",
                "theme_package.export.io_failure");
        }
    }

    public async Task<OperationResult<ThemePackageImportResult>> ImportAsync(
        string packageFile,
        CancellationToken cancellationToken)
    {
        if (!IsAbsolutePackagePath(packageFile) || !File.Exists(packageFile))
        {
            return Failure<ThemePackageImportResult>(
                OperationErrorCode.InvalidPath,
                "请选择存在的 .cttheme 主题包。",
                "theme_package.import.path_invalid");
        }

        var info = new FileInfo(packageFile);
        if (info.Length > MaximumPackageBytes)
        {
            return Failure<ThemePackageImportResult>(
                OperationErrorCode.ValidationFailed,
                "主题包超过 32 MB 上限。",
                "theme_package.import.archive_too_large");
        }

        var stagingRelative =
            $"{StorageLayout.RuntimeDirectory}/theme-imports/{Guid.NewGuid():N}";
        var staging = pathResolver.Resolve(stagingRelative);
        if (!staging.IsSuccess)
        {
            return OperationResult<ThemePackageImportResult>.Failure(staging.Error!);
        }

        try
        {
            Directory.CreateDirectory(staging.Value!);
            ThemePackageManifest manifest;
            await using (var stream = new FileStream(
                             packageFile,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                if (archive.Entries.Count is < 3 or > MaximumEntries)
                {
                    return InvalidImport(
                        "主题包条目数量无效。",
                        "theme_package.import.entry_count");
                }

                long expandedBytes = 0;
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!IsSafeEntry(entry.FullName) ||
                        !AllowedEntries.Contains(entry.FullName) ||
                        !names.Add(entry.FullName))
                    {
                        return InvalidImport(
                            "主题包包含恶意路径、重复条目或不允许的文件类型。",
                            "theme_package.import.entry_rejected");
                    }

                    if (entry.Length < 0 ||
                        entry.CompressedLength < 0 ||
                        entry.Length > MaximumExpandedBytes)
                    {
                        return InvalidImport(
                            "主题包条目大小无效。",
                            "theme_package.import.entry_size");
                    }

                    expandedBytes = checked(expandedBytes + entry.Length);
                    if (expandedBytes > MaximumExpandedBytes)
                    {
                        return InvalidImport(
                            "主题包解压后超过 64 MB 上限。",
                            "theme_package.import.expanded_too_large");
                    }
                }

                if (!names.Contains("manifest.json") ||
                    !names.Contains("theme.json") ||
                    !names.Contains("background.webp"))
                {
                    return InvalidImport(
                        "主题包缺少 manifest.json、theme.json 或 background.webp。",
                        "theme_package.import.required_missing");
                }

                foreach (var entry in archive.Entries)
                {
                    var destination = Path.Combine(staging.Value!, entry.FullName);
                    await using var input = entry.Open();
                    await using var output = new FileStream(
                        destination,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        64 * 1024,
                        FileOptions.Asynchronous | FileOptions.WriteThrough);
                    await input.CopyToAsync(output, cancellationToken);
                }
            }

            var manifestBytes = await File.ReadAllBytesAsync(
                Path.Combine(staging.Value!, "manifest.json"),
                cancellationToken);
            try
            {
                manifest = JsonSerializer.Deserialize<ThemePackageManifest>(
                               manifestBytes,
                               JsonOptions)
                           ?? throw new JsonException();
            }
            catch (JsonException)
            {
                return InvalidImport(
                    "manifest.json 已损坏或包含未知字段。",
                    "theme_package.import.manifest_invalid");
            }

            if (manifest.PackageSchemaVersion > CurrentPackageSchemaVersion)
            {
                return Failure<ThemePackageImportResult>(
                    OperationErrorCode.UnsupportedVersion,
                    $"主题包使用较新的 Package Schema v{manifest.PackageSchemaVersion}，当前版本不会导入。",
                    "theme_package.import.schema_newer");
            }

            var verification = await VerifyManifestAsync(
                staging.Value!,
                manifest,
                cancellationToken);
            if (!verification.IsSuccess)
            {
                return OperationResult<ThemePackageImportResult>.Failure(verification.Error!);
            }

            var themeBytes = await File.ReadAllBytesAsync(
                Path.Combine(staging.Value!, "theme.json"),
                cancellationToken);
            var read = serializer.Read(themeBytes);
            if (read.Status == ThemeDocumentReadStatus.UnsupportedNewerSchema)
            {
                return Failure<ThemePackageImportResult>(
                    OperationErrorCode.UnsupportedVersion,
                    read.Issues[0].UserMessage,
                    read.Issues[0].Code);
            }

            if (read.Status != ThemeDocumentReadStatus.Success)
            {
                return Failure<ThemePackageImportResult>(
                    OperationErrorCode.ValidationFailed,
                    read.Issues[0].UserMessage,
                    read.Issues[0].Code);
            }

            var theme = read.Theme!;
            if (theme.Id != manifest.ThemeId ||
                theme.SchemaVersion != manifest.ThemeSchemaVersion)
            {
                return InvalidImport(
                    "manifest.json 与 theme.json 的主题标识或 Schema 不一致。",
                    "theme_package.import.theme_manifest_mismatch");
            }

            var existing = await repository.GetAsync(theme.Id, cancellationToken);
            if (existing.IsSuccess)
            {
                theme = theme with
                {
                    Id = Guid.NewGuid(),
                    Name = $"{theme.Name}（导入副本）",
                };
            }

            await using var background = new FileStream(
                Path.Combine(staging.Value!, "background.webp"),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var imported = await imageImport.ImportNewAsync(
                theme,
                background,
                "background.webp",
                new ThemeCreateOptions(
                    ThemeSourceType.Imported,
                    Hash(await File.ReadAllBytesAsync(packageFile, cancellationToken)),
                    Tags: ["已导入"]),
                cancellationToken);
            if (!imported.IsSuccess)
            {
                return OperationResult<ThemePackageImportResult>.Failure(imported.Error!);
            }

            var packageHash = Hash(
                await File.ReadAllBytesAsync(packageFile, cancellationToken));
            return OperationResult<ThemePackageImportResult>.Success(
                new ThemePackageImportResult(
                    imported.Value!.Theme.Id,
                    imported.Value.Theme.Name,
                    packageHash));
        }
        catch (InvalidDataException)
        {
            return InvalidImport(
                "主题包不是有效的 ZIP 文件或已损坏。",
                "theme_package.import.zip_invalid");
        }
        catch (OverflowException)
        {
            return InvalidImport(
                "主题包声明的解压大小无效。",
                "theme_package.import.size_overflow");
        }
        catch (OperationCanceledException)
        {
            return Failure<ThemePackageImportResult>(
                OperationErrorCode.Cancelled,
                "主题包导入已取消。",
                "theme_package.import.cancelled");
        }
        catch (UnauthorizedAccessException)
        {
            return Failure<ThemePackageImportResult>(
                OperationErrorCode.AccessDenied,
                "没有权限读取主题包或写入暂存目录。",
                "theme_package.import.access_denied");
        }
        catch (IOException)
        {
            return Failure<ThemePackageImportResult>(
                OperationErrorCode.StorageUnavailable,
                "主题包导入失败，请检查磁盘空间和数据位置。",
                "theme_package.import.io_failure");
        }
        finally
        {
            TryDeleteCreatedDirectory(staging.Value);
        }
    }

    private async Task<OperationResult> VerifyManifestAsync(
        string staging,
        ThemePackageManifest manifest,
        CancellationToken cancellationToken)
    {
        if (manifest.PackageSchemaVersion != CurrentPackageSchemaVersion ||
            manifest.ThemeSchemaVersion != ThemePackageContractValidator.CurrentSchemaVersion ||
            manifest.ThemeId == Guid.Empty ||
            manifest.Files.Count is < 2 or > 3)
        {
            return OperationResult.Failure(
                OperationErrorCode.ValidationFailed,
                "manifest.json 的版本或主题标识无效。",
                "theme_package.import.manifest_contract");
        }

        var expected = new HashSet<string>(
            Directory.EnumerateFiles(staging)
                .Select(Path.GetFileName)
                .Where(name => !string.Equals(name, "manifest.json", StringComparison.Ordinal))!,
            StringComparer.Ordinal);
        if (manifest.Files.Select(file => file.Path).Distinct(StringComparer.Ordinal).Count() !=
                manifest.Files.Count ||
            !expected.SetEquals(manifest.Files.Select(file => file.Path)))
        {
            return OperationResult.Failure(
                OperationErrorCode.ValidationFailed,
                "manifest.json 与主题包文件列表不一致。",
                "theme_package.import.manifest_files_mismatch");
        }

        foreach (var file in manifest.Files)
        {
            if (!AllowedEntries.Contains(file.Path) ||
                file.Path == "manifest.json" ||
                file.Size < 0 ||
                file.Sha256.Length != 64)
            {
                return OperationResult.Failure(
                    OperationErrorCode.ValidationFailed,
                    "manifest.json 包含无效文件描述。",
                    "theme_package.import.manifest_file_invalid");
            }

            var bytes = await File.ReadAllBytesAsync(
                Path.Combine(staging, file.Path),
                cancellationToken);
            if (bytes.LongLength != file.Size ||
                !string.Equals(Hash(bytes), file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult.Failure(
                    OperationErrorCode.ValidationFailed,
                    $"主题包文件 {file.Path} 的 SHA-256 校验失败。",
                    "theme_package.import.hash_mismatch");
            }
        }

        return OperationResult.Success();
    }

    private async Task<string?> FindThumbnailAsync(
        Guid themeId,
        CancellationToken cancellationToken)
    {
        var summaries = await repository.ListAsync(cancellationToken);
        var relative = summaries.IsSuccess
            ? summaries.Value!.FirstOrDefault(item => item.ThemeId == themeId)
                ?.ThumbnailRelativePath
            : null;
        if (string.IsNullOrWhiteSpace(relative))
        {
            return null;
        }

        var resolved = pathResolver.Resolve(relative);
        return resolved.IsSuccess && File.Exists(resolved.Value)
            ? resolved.Value
            : null;
    }

    private static bool IsSafeEntry(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value == Path.GetFileName(value) &&
        !Path.IsPathFullyQualified(value) &&
        !value.Contains("..", StringComparison.Ordinal) &&
        !value.Contains('/') &&
        !value.Contains('\\') &&
        !value.Any(char.IsControl);

    private static bool IsAbsolutePackagePath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Path.IsPathFullyQualified(path) &&
        string.Equals(Path.GetExtension(path), ".cttheme", StringComparison.OrdinalIgnoreCase);

    private static async Task<byte[]> ReadBoundedAsync(
        string path,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length > maximumBytes)
        {
            throw new IOException("File exceeds the package component limit.");
        }

        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static OperationResult<ThemePackageImportResult> InvalidImport(
        string message,
        string diagnostic) =>
        Failure<ThemePackageImportResult>(
            OperationErrorCode.ValidationFailed,
            message,
            diagnostic);

    private static OperationResult<T> Failure<T>(
        OperationErrorCode code,
        string message,
        string diagnostic) =>
        OperationResult<T>.Failure(code, message, diagnostic);

    private static void TryDeleteCreatedFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteCreatedDirectory(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
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
}
