using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;
using CodexThemeStudio.ThemeCore;

namespace CodexThemeStudio.Storage;

public sealed class FileThemeAssetStore : IThemeAssetStore
{
    private const long MaximumAssetBytes =
        ImageSizeLimits.MaximumManagedImageBytes;
    private const long MaximumThemeDocumentBytes = 512L * 1024;

    private readonly TrustedPathResolver pathResolver;

    public FileThemeAssetStore(string dataRoot)
    {
        pathResolver = new TrustedPathResolver(dataRoot);
    }

    public async Task<OperationResult<string>> SaveAsync(
        Guid themeId,
        string fileName,
        Stream content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (themeId == Guid.Empty)
        {
            return OperationResult<string>.Failure(
                OperationErrorCode.ValidationFailed,
                "主题 ID 不能为空。",
                "asset.theme_id.empty");
        }

        var normalizedFileName = RelativePathPolicy.Normalize(fileName);
        if (!normalizedFileName.IsSuccess)
        {
            return OperationResult<string>.Failure(normalizedFileName.Error!);
        }

        var themeDirectoryRelative = StorageLayout.GetThemeDirectory(themeId);
        var themeDirectory = pathResolver.Resolve(themeDirectoryRelative);
        if (!themeDirectory.IsSuccess)
        {
            return OperationResult<string>.Failure(themeDirectory.Error!);
        }

        try
        {
            Directory.CreateDirectory(themeDirectory.Value!);

            var relativePath = $"{themeDirectoryRelative}/{normalizedFileName.Value}";
            var destination = pathResolver.Resolve(relativePath);
            if (!destination.IsSuccess)
            {
                return OperationResult<string>.Failure(destination.Error!);
            }

            using var buffer = new MemoryStream();
            var copyBuffer = new byte[64 * 1024];
            long total = 0;
            int read;

            while ((read = await content.ReadAsync(
                       copyBuffer,
                       cancellationToken)) > 0)
            {
                total += read;
                if (total > MaximumAssetBytes)
                {
                    return OperationResult<string>.Failure(
                        OperationErrorCode.ValidationFailed,
                        "主题资源超过 32 MiB 上限。",
                        "asset.too_large");
                }

                await buffer.WriteAsync(
                    copyBuffer.AsMemory(0, read),
                    cancellationToken);
            }

            var write = await AtomicFileWriter.WriteAsync(
                destination.Value!,
                buffer.ToArray(),
                cancellationToken);
            return write.IsSuccess
                ? OperationResult<string>.Success(relativePath)
                : OperationResult<string>.Failure(write.Error!);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<string>.Failure(
                OperationErrorCode.Cancelled,
                "资源写入已取消。",
                "asset.cancelled");
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResult<string>.Failure(
                OperationErrorCode.AccessDenied,
                "没有权限写入主题资源。",
                "asset.access_denied");
        }
        catch (IOException)
        {
            return OperationResult<string>.Failure(
                OperationErrorCode.StorageUnavailable,
                "主题资源存储不可用。",
                "asset.io_failure");
        }
    }

    public Task<OperationResult<Stream>> OpenReadAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var resolved = pathResolver.Resolve(relativePath);
        if (!resolved.IsSuccess)
        {
            return Task.FromResult(
                OperationResult<Stream>.Failure(resolved.Error!));
        }

        try
        {
            if (!File.Exists(resolved.Value))
            {
                return Task.FromResult(
                    OperationResult<Stream>.Failure(
                        OperationErrorCode.NotFound,
                        "主题资源不存在。",
                        "asset.not_found"));
            }

            Stream stream = new FileStream(
                resolved.Value!,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult(OperationResult<Stream>.Success(stream));
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(
                OperationResult<Stream>.Failure(
                    OperationErrorCode.AccessDenied,
                    "没有权限读取主题资源。",
                    "asset.access_denied"));
        }
        catch (IOException)
        {
            return Task.FromResult(
                OperationResult<Stream>.Failure(
                    OperationErrorCode.StorageUnavailable,
                    "无法读取主题资源。",
                    "asset.io_failure"));
        }
    }

    public Task<OperationResult> DeleteGeneratedAssetsAsync(
        Guid themeId,
        IReadOnlyCollection<string> fileNames,
        bool deleteEmptyThemeDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (themeId == Guid.Empty)
        {
            return Task.FromResult(OperationResult.Failure(
                OperationErrorCode.ValidationFailed,
                "主题 ID 不能为空。",
                "asset.delete_generated.theme_id.empty"));
        }

        var directory = pathResolver.Resolve(StorageLayout.GetThemeDirectory(themeId));
        if (!directory.IsSuccess || !Directory.Exists(directory.Value))
        {
            return Task.FromResult(directory.IsSuccess
                ? OperationResult.Success()
                : OperationResult.Failure(directory.Error!));
        }

        try
        {
            if ((File.GetAttributes(directory.Value!) & FileAttributes.ReparsePoint) != 0)
            {
                return Task.FromResult(OperationResult.Failure(
                    OperationErrorCode.InvalidPath,
                    "主题目录是链接或重解析点，未删除任何资源。",
                    "asset.delete_generated.directory_reparse_point"));
            }

            if (deleteEmptyThemeDirectory &&
                File.Exists(Path.Combine(directory.Value!, StorageLayout.ThemeFileName)))
            {
                return Task.FromResult(OperationResult.Failure(
                    OperationErrorCode.Conflict,
                    "主题目录已包含 theme.json，未删除任何草稿资源。",
                    "asset.delete_generated.directory_has_theme"));
            }

            var assets = new List<string>();
            foreach (var fileName in fileNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var normalized = RelativePathPolicy.Normalize(fileName);
                if (!normalized.IsSuccess ||
                    !string.Equals(
                        normalized.Value,
                        Path.GetFileName(normalized.Value),
                        StringComparison.Ordinal) ||
                    string.Equals(
                        normalized.Value,
                        StorageLayout.ThemeFileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(OperationResult.Failure(
                        OperationErrorCode.InvalidPath,
                        "仅能删除主题目录内明确列出的单个资源文件。",
                        "asset.delete_generated.file_name.invalid"));
                }

                var asset = pathResolver.Resolve(
                    $"{StorageLayout.GetThemeDirectory(themeId)}/{normalized.Value}");
                if (!asset.IsSuccess)
                {
                    return Task.FromResult(OperationResult.Failure(asset.Error!));
                }

                if (File.Exists(asset.Value) &&
                    (File.GetAttributes(asset.Value!) & FileAttributes.ReparsePoint) != 0)
                {
                    return Task.FromResult(OperationResult.Failure(
                        OperationErrorCode.InvalidPath,
                        "主题资源是链接或重解析点，未删除任何资源。",
                        "asset.delete_generated.file_reparse_point"));
                }

                assets.Add(asset.Value!);
            }

            var themeDocument = Path.Combine(
                directory.Value!,
                StorageLayout.ThemeFileName);
            if (File.Exists(themeDocument))
            {
                var documentInfo = new FileInfo(themeDocument);
                if ((documentInfo.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    documentInfo.Length > MaximumThemeDocumentBytes)
                {
                    return Task.FromResult(OperationResult.Failure(
                        OperationErrorCode.ValidationFailed,
                        "主题文件无法安全验证，未删除任何资源。",
                        "asset.delete_generated.theme_document.invalid"));
                }

                var read = new ThemeDocumentSerializer().Read(
                    File.ReadAllBytes(themeDocument));
                if (read.Status is not ThemeDocumentReadStatus.Success)
                {
                    return Task.FromResult(OperationResult.Failure(
                        OperationErrorCode.ValidationFailed,
                        "主题文件无法安全验证，未删除任何资源。",
                        "asset.delete_generated.theme_document.invalid"));
                }

                if (assets.Any(asset =>
                        string.Equals(
                            Path.GetFileName(asset),
                            read.Theme!.Art.File,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    return Task.FromResult(OperationResult.Failure(
                        OperationErrorCode.Conflict,
                        "目标资源仍被当前主题引用，未删除任何资源。",
                        "asset.delete_generated.asset_still_referenced"));
                }
            }

            foreach (var asset in assets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(asset))
                {
                    File.Delete(asset);
                }
            }

            if (deleteEmptyThemeDirectory &&
                !Directory.EnumerateFileSystemEntries(directory.Value!).Any())
            {
                Directory.Delete(directory.Value!);
            }

            return Task.FromResult(OperationResult.Success());
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(OperationResult.Failure(
                OperationErrorCode.Cancelled,
                "已取消删除未引用的主题资源。",
                "asset.delete_generated.cancelled"));
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(OperationResult.Failure(
                OperationErrorCode.AccessDenied,
                "没有权限删除未引用的主题资源。",
                "asset.delete_generated.access_denied"));
        }
        catch (IOException)
        {
            return Task.FromResult(OperationResult.Failure(
                OperationErrorCode.StorageUnavailable,
                "无法删除未引用的主题资源。",
                "asset.delete_generated.io_failure"));
        }
    }
}
