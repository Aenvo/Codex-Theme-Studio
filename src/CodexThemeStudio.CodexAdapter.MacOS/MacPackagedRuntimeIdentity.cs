using System.Security.Cryptography;

namespace CodexThemeStudio.CodexAdapter.MacOS;

public static class MacPackagedRuntimeIdentity
{
    public static bool IsConfigured => GeneratedMacRuntimeIdentity.IsConfigured;

    public static bool VerifyHelper(string helperPath)
    {
        if (!IsConfigured ||
            !OperatingSystem.IsMacOS() ||
            !Path.IsPathFullyQualified(helperPath) ||
            Path.GetFullPath(helperPath) != helperPath)
        {
            return false;
        }

        var file = new FileInfo(helperPath);
        if (!file.Exists || file.LinkTarget is not null)
        {
            return false;
        }

        var mode = File.GetUnixFileMode(helperPath);
        var executableBits = UnixFileMode.UserExecute |
            UnixFileMode.GroupExecute |
            UnixFileMode.OtherExecute;
        if ((mode & executableBits) == 0)
        {
            return false;
        }

        using var stream = file.OpenRead();
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        return actual.Equals(
            GeneratedMacRuntimeIdentity.HelperSha256,
            StringComparison.OrdinalIgnoreCase);
    }
}
