using System.IO;
using System.Text.RegularExpressions;

namespace CodexThemeStudio.Desktop;

internal static partial class PersistenceBundleLocator
{
    private const int MaximumParentSearchDepth = 8;

    public static string Find(string applicationDirectory, string applicationVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);

        var applicationRoot = Path.GetFullPath(applicationDirectory);
        if (IsCompleteBundle(applicationRoot))
        {
            return applicationRoot;
        }

        if (!VersionPattern().IsMatch(applicationVersion))
        {
            return applicationRoot;
        }

        var directory = new DirectoryInfo(applicationRoot);
        for (var depth = 0;
             directory is not null && depth < MaximumParentSearchDepth;
             depth++, directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "CodexThemeStudio.sln")))
            {
                continue;
            }

            var releaseRoot = Path.Combine(
                directory.FullName,
                "artifacts",
                "release",
                applicationVersion);
            var candidate = Path.Combine(
                releaseRoot,
                $"CodexThemeManager-{applicationVersion}-win-x64-portable");
            if (IsPathWithin(candidate, releaseRoot) &&
                IsCompleteBundle(candidate))
            {
                return Path.GetFullPath(candidate);
            }

            break;
        }

        return applicationRoot;
    }

    internal static bool IsCompleteBundle(string candidate)
    {
        try
        {
            var root = Path.GetFullPath(candidate);
            if (!Directory.Exists(root) || IsReparsePoint(root))
            {
                return false;
            }

            var nestedAgent = Path.Combine(
                root,
                "agent",
                "CodexThemeStudio.Agent.exe");
            var flatAgent = Path.Combine(root, "CodexThemeStudio.Agent.exe");
            var agent = File.Exists(nestedAgent) ? nestedAgent : flatAgent;
            var node = Path.Combine(root, "runtime", "node", "node.exe");
            var injector = Path.Combine(
                root,
                "runtime",
                "injector",
                "index.mjs");

            return IsRegularFileWithinRoot(agent, root) &&
                   IsRegularFileWithinRoot(node, root) &&
                   IsRegularFileWithinRoot(injector, root);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            IOException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsRegularFileWithinRoot(string path, string root)
    {
        if (!File.Exists(path) ||
            !IsPathWithin(path, root) ||
            IsReparsePoint(path))
        {
            return false;
        }

        var directory = new DirectoryInfo(Path.GetDirectoryName(path)!);
        while (!string.Equals(
                   directory.FullName,
                   root,
                   StringComparison.OrdinalIgnoreCase))
        {
            if (!directory.Exists || IsReparsePoint(directory.FullName))
            {
                return false;
            }

            directory = directory.Parent!;
        }

        return true;
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool IsPathWithin(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(
        @"^\d+\.\d+\.\d+(?:\.\d+)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
