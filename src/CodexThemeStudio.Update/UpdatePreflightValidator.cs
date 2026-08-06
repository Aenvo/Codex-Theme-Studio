using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Update;

public static class UpdatePreflightValidator
{
    private const long MaximumExpandedBytes = 1_073_741_824;
    private const long ApplicationReserveBytes = 134_217_728;
    private const long CacheReserveBytes = 67_108_864;

    public static OperationResult Check(
        string applicationRoot,
        string cacheRoot,
        long zipBytes,
        long? expandedBytes = null)
    {
        try
        {
            var appRoot = Path.GetFullPath(applicationRoot);
            var appParent = Directory.GetParent(appRoot)?.FullName;
            if (appParent is null || !Directory.Exists(appRoot) ||
                IsReparsePoint(appRoot) || IsReparsePoint(appParent))
            {
                return Failure("应用目录或父目录不是受支持的普通目录。", "update.preflight.reparse");
            }

            if (!File.Exists(Path.Combine(appRoot, "CodexThemeManager.exe")))
            {
                return Failure("当前不是受支持的 Codex Theme Studio 便携包，请前往 GitHub 手动更新。", "update.preflight.not_portable");
            }

            var probe = Path.Combine(appParent, $".cts-update-write-{Guid.NewGuid():N}.tmp");
            try
            {
                using (File.Create(probe)) { }
            }
            finally
            {
                if (File.Exists(probe)) File.Delete(probe);
            }

            Directory.CreateDirectory(cacheRoot);
            if (IsReparsePoint(cacheRoot))
            {
                return Failure("更新缓存目录不能是符号链接或 Junction。", "update.preflight.cache_reparse");
            }

            var appDrive = new DriveInfo(Path.GetPathRoot(appRoot)!);
            var cacheDrive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(cacheRoot))!);
            var requiredApp = checked((expandedBytes ?? MaximumExpandedBytes) + ApplicationReserveBytes);
            var requiredCache = checked(zipBytes + CacheReserveBytes);
            if (appDrive.AvailableFreeSpace < requiredApp ||
                cacheDrive.AvailableFreeSpace < requiredCache)
            {
                return Failure("可用磁盘空间不足，无法安全下载并保留回滚副本。", "update.preflight.disk_space");
            }

            return OperationResult.Success();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return OperationResult.Failure(
                OperationErrorCode.AccessDenied,
                "无法写入应用目录或更新缓存，请检查目录权限。",
                $"update.preflight.{exception.GetType().Name}");
        }
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static OperationResult Failure(string message, string code) =>
        OperationResult.Failure(OperationErrorCode.ValidationFailed, message, code);
}
