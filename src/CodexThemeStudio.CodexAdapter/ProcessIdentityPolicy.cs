using CodexThemeStudio.Contracts.Models;

namespace CodexThemeStudio.CodexAdapter;

public static class ProcessIdentityPolicy
{
    public const string OfficialPackageFamilyName = "OpenAI.Codex_2p2nqsd0c76g0";
    public const string OfficialPublisherId = "2p2nqsd0c76g0";

    public static bool IsOfficialInstallation(CodexInstallationInfo installation)
    {
        ArgumentNullException.ThrowIfNull(installation);

        return installation.IsStoreSigned &&
            string.Equals(
                installation.PackageFamilyName,
                OfficialPackageFamilyName,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                installation.PublisherId,
                OfficialPublisherId,
                StringComparison.OrdinalIgnoreCase) &&
            IsExecutableInsidePackage(installation.ExecutablePath, installation.PackageFullName);
    }

    public static bool IsSameProcess(CodexProcessInfo expected, CodexProcessInfo actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        return expected.ProcessId == actual.ProcessId &&
            expected.StartedAtUtc.EqualsExact(actual.StartedAtUtc) &&
            string.Equals(
                Path.GetFullPath(expected.ExecutablePath),
                Path.GetFullPath(actual.ExecutablePath),
                StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMainProcessCommandLine(string commandLine) =>
        !string.IsNullOrWhiteSpace(commandLine) &&
        !commandLine.Contains("--type=", StringComparison.OrdinalIgnoreCase);

    private static bool IsExecutableInsidePackage(string executablePath, string packageFullName)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || string.IsNullOrWhiteSpace(packageFullName))
        {
            return false;
        }

        var normalized = Path.GetFullPath(executablePath);
        var expectedSegment = $"{Path.DirectorySeparatorChar}{packageFullName}{Path.DirectorySeparatorChar}";

        return normalized.Contains(expectedSegment, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetFileName(normalized), "ChatGPT.exe", StringComparison.OrdinalIgnoreCase);
    }
}

internal static class DateTimeOffsetExtensions
{
    public static bool EqualsExact(this DateTimeOffset left, DateTimeOffset right) =>
        left.ToUniversalTime().Ticks == right.ToUniversalTime().Ticks;
}
