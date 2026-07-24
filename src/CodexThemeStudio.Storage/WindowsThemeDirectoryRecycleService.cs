using System.Security;
using CodexThemeStudio.Contracts.Results;
using Microsoft.VisualBasic.FileIO;

namespace CodexThemeStudio.Storage;

public sealed class WindowsThemeDirectoryRecycleService : IThemeDirectoryRecycleService
{
    public OperationResult MoveToSystemRecycleBin(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        try
        {
            if (!Directory.Exists(fullPath))
            {
                return OperationResult.Failure(
                    OperationErrorCode.NotFound,
                    "主题目录不存在；为避免丢失索引，未执行永久删除。",
                    "theme.purge.directory_missing");
            }

            FileSystem.DeleteDirectory(
                fullPath,
                UIOption.OnlyErrorDialogs,
                RecycleOption.SendToRecycleBin,
                UICancelOption.ThrowException);
            if (Directory.Exists(fullPath))
            {
                return OperationResult.Failure(
                    OperationErrorCode.StorageUnavailable,
                    "Windows 回收站未确认接收主题目录；数据库记录保持不变。",
                    "theme.purge.recycle_not_confirmed");
            }

            return OperationResult.Success();
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Failure(
                OperationErrorCode.Cancelled,
                "已取消永久删除。",
                "theme.purge.cancelled");
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResult.Failure(
                OperationErrorCode.AccessDenied,
                "没有权限将主题目录移入 Windows 回收站。",
                "theme.purge.access_denied");
        }
        catch (SecurityException)
        {
            return OperationResult.Failure(
                OperationErrorCode.AccessDenied,
                "安全策略阻止将主题目录移入 Windows 回收站。",
                "theme.purge.security_denied");
        }
        catch (IOException)
        {
            return OperationResult.Failure(
                OperationErrorCode.StorageUnavailable,
                "无法将主题目录移入 Windows 回收站。",
                "theme.purge.io_failure");
        }
    }
}
