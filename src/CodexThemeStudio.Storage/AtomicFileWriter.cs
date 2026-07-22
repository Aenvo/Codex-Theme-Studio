using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Storage;

public static class AtomicFileWriter
{
    public static async Task<OperationResult> WriteAsync(
        string destinationPath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var directory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return OperationResult.Failure(
                OperationErrorCode.InvalidPath,
                "目标文件缺少父目录。",
                "atomic.parent.missing");
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(destinationPath))
            {
                File.Replace(temporaryPath, destinationPath, null);
            }
            else
            {
                File.Move(temporaryPath, destinationPath);
            }

            return OperationResult.Success();
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Failure(
                OperationErrorCode.Cancelled,
                "写入已取消。",
                "atomic.cancelled");
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResult.Failure(
                OperationErrorCode.AccessDenied,
                "没有权限写入目标文件。",
                "atomic.access_denied");
        }
        catch (IOException)
        {
            return OperationResult.Failure(
                OperationErrorCode.StorageUnavailable,
                "无法原子写入目标文件。",
                "atomic.io_failure");
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

