using CodexThemeStudio.Contracts.Results;
using CodexThemeStudio.ThemeCore;

namespace CodexThemeStudio.Storage;

public sealed class TrustedPathResolver
{
    private readonly string dataRootPrefix;

    public TrustedPathResolver(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        if (!Path.IsPathFullyQualified(dataRoot))
        {
            throw new ArgumentException("DataRoot must be an absolute path.", nameof(dataRoot));
        }

        DataRoot = Path.GetFullPath(dataRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        dataRootPrefix = DataRoot + Path.DirectorySeparatorChar;
    }

    public string DataRoot { get; }

    public OperationResult<string> Resolve(
        string relativePath,
        bool inspectReparsePoints = true)
    {
        var normalized = RelativePathPolicy.Normalize(relativePath);
        if (!normalized.IsSuccess)
        {
            return OperationResult<string>.Failure(normalized.Error!);
        }

        var platformRelativePath = normalized.Value!.Replace(
            '/',
            Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(DataRoot, platformRelativePath));

        if (!fullPath.StartsWith(dataRootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<string>.Failure(
                OperationErrorCode.InvalidPath,
                "路径超出可信数据目录。",
                "path.outside_data_root");
        }

        if (inspectReparsePoints)
        {
            var reparseCheck = EnsureNoReparsePoints(fullPath);
            if (!reparseCheck.IsSuccess)
            {
                return OperationResult<string>.Failure(reparseCheck.Error!);
            }
        }

        return OperationResult<string>.Success(fullPath);
    }

    public OperationResult EnsureRootIsTrusted()
    {
        if (!Directory.Exists(DataRoot))
        {
            return OperationResult.Failure(
                OperationErrorCode.StorageUnavailable,
                "数据目录不存在。",
                "storage.data_root.missing");
        }

        return HasReparsePoint(DataRoot)
            ? OperationResult.Failure(
                OperationErrorCode.InvalidPath,
                "数据根目录不能是符号链接或 Junction。",
                "path.data_root.reparse_point")
            : OperationResult.Success();
    }

    private OperationResult EnsureNoReparsePoints(string fullPath)
    {
        var rootCheck = EnsureRootIsTrusted();
        if (!rootCheck.IsSuccess)
        {
            return rootCheck;
        }

        var relative = Path.GetRelativePath(DataRoot, fullPath);
        var current = DataRoot;

        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                break;
            }

            if (HasReparsePoint(current))
            {
                return OperationResult.Failure(
                    OperationErrorCode.InvalidPath,
                    "路径不能经过符号链接或 Junction。",
                    "path.reparse_point");
            }
        }

        return OperationResult.Success();
    }

    private static bool HasReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}

