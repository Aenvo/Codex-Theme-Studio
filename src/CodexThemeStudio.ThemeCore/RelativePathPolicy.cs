using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.ThemeCore;

public static class RelativePathPolicy
{
    private static readonly HashSet<string> ReservedNames = CreateReservedNames();
    private static readonly char[] InvalidCharacters = ['<', '>', ':', '"', '|', '?', '*'];

    public static OperationResult<string> Normalize(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return Invalid("相对路径不能为空。", "path.empty");
        }

        if (Path.IsPathRooted(relativePath) ||
            relativePath.StartsWith('\\') ||
            relativePath.StartsWith('/'))
        {
            return Invalid("不允许绝对路径。", "path.absolute");
        }

        var normalizedSeparators = relativePath.Replace('\\', '/');
        var segments = normalizedSeparators.Split('/');

        if (segments.Any(segment => segment.Length == 0))
        {
            return Invalid("路径不能包含空目录段。", "path.empty_segment");
        }

        foreach (var segment in segments)
        {
            if (segment is "." or "..")
            {
                return Invalid("路径不能包含 . 或 ..。", "path.traversal");
            }

            if (segment.Any(character =>
                    char.IsControl(character) ||
                    InvalidCharacters.Contains(character)))
            {
                return Invalid("路径包含 Windows 不支持的字符。", "path.invalid_character");
            }

            if (segment.EndsWith(' ') || segment.EndsWith('.'))
            {
                return Invalid("路径段不能以空格或句点结尾。", "path.ambiguous_segment");
            }

            var baseName = segment.Split('.')[0];
            if (ReservedNames.Contains(baseName))
            {
                return Invalid("路径包含 Windows 保留设备名。", "path.reserved_name");
            }
        }

        return OperationResult<string>.Success(string.Join('/', segments));
    }

    private static OperationResult<string> Invalid(
        string userMessage,
        string diagnosticCode) =>
        OperationResult<string>.Failure(
            OperationErrorCode.InvalidPath,
            userMessage,
            diagnosticCode);

    private static HashSet<string> CreateReservedNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON",
            "PRN",
            "AUX",
            "NUL",
        };

        for (var index = 1; index <= 9; index++)
        {
            names.Add($"COM{index}");
            names.Add($"LPT{index}");
        }

        return names;
    }
}

