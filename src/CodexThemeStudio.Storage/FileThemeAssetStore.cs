using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Results;
using CodexThemeStudio.ThemeCore;

namespace CodexThemeStudio.Storage;

public sealed class FileThemeAssetStore : IThemeAssetStore
{
    private const long MaximumAssetBytes = 16L * 1024 * 1024;

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
                        "主题资源超过 16 MB 上限。",
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
}

