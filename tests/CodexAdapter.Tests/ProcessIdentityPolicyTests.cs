namespace CodexThemeStudio.CodexAdapter.Tests;

using CodexThemeStudio.CodexAdapter;
using CodexThemeStudio.Contracts.Models;

public sealed class ProcessIdentityPolicyTests
{
    [Fact]
    public void OfficialInstallation_RequiresStoreIdentityAndPackageOwnedExecutable()
    {
        var installation = CreateInstallation(
            @"C:\Program Files\WindowsApps\OpenAI.Codex_1.2.3.4_x64__2p2nqsd0c76g0\app\ChatGPT.exe");

        Assert.True(ProcessIdentityPolicy.IsOfficialInstallation(installation));
        Assert.False(ProcessIdentityPolicy.IsOfficialInstallation(
            installation with { PublisherId = "untrusted" }));
        Assert.False(ProcessIdentityPolicy.IsOfficialInstallation(
            installation with { ExecutablePath = @"C:\Other\ChatGPT.exe" }));
    }

    [Theory]
    [InlineData("\"ChatGPT.exe\"")]
    [InlineData("\"ChatGPT.exe\" --inspect=9229")]
    public void MainProcessCommandLine_AcceptsOnlyCommandsWithoutType(string commandLine)
    {
        Assert.True(ProcessIdentityPolicy.IsMainProcessCommandLine(commandLine));
    }

    [Theory]
    [InlineData("\"ChatGPT.exe\" --type=renderer")]
    [InlineData("\"ChatGPT.exe\" --TYPE=gpu-process")]
    public void MainProcessCommandLine_RejectsChromiumChildren(string commandLine)
    {
        Assert.False(ProcessIdentityPolicy.IsMainProcessCommandLine(commandLine));
    }

    [Fact]
    public void SameProcess_RejectsPidReuseByCreationTime()
    {
        var startedAt = DateTimeOffset.Parse(
            "2026-07-20T10:00:00.0000000+00:00",
            System.Globalization.CultureInfo.InvariantCulture);
        var expected = new CodexProcessInfo(42, startedAt, @"C:\Codex\ChatGPT.exe", null);
        var reused = expected with { StartedAtUtc = startedAt.AddMilliseconds(1) };

        Assert.False(ProcessIdentityPolicy.IsSameProcess(expected, reused));
    }

    private static CodexInstallationInfo CreateInstallation(string executablePath) =>
        new(
            "OpenAI.Codex_2p2nqsd0c76g0",
            "OpenAI.Codex_1.2.3.4_x64__2p2nqsd0c76g0",
            "1.2.3.4",
            executablePath);
}
