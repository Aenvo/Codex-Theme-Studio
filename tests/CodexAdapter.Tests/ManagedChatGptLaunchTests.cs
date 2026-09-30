using System.Diagnostics;
using CodexThemeStudio.CodexAdapter;
using CodexThemeStudio.Contracts.Models;

namespace CodexThemeStudio.CodexAdapter.Tests;

public sealed class ManagedChatGptLaunchTests
{
    [Fact]
    public void RequestClose_TreatsAlreadyExitedProcessAsSuccess()
    {
        var result = InjectorCommandClient.RequestExactProcessClose(
            new CodexProcessInfo(
                int.MaxValue,
                DateTimeOffset.UtcNow,
                @"C:\Program Files\WindowsApps\OpenAI.Codex\ChatGPT.exe",
                null),
            @"C:\Program Files\WindowsApps\OpenAI.Codex\ChatGPT.exe");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void RequestClose_RejectsPidReuseBeforeSendingClose()
    {
        using var process = Process.GetCurrentProcess();
        var executablePath = process.MainModule!.FileName;
        var result = InjectorCommandClient.RequestExactProcessClose(
            new CodexProcessInfo(
                process.Id,
                process.StartTime.ToUniversalTime().AddMinutes(-5),
                executablePath,
                null),
            executablePath);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "themed_launch.process_identity_changed",
            result.Error!.DiagnosticCode);
    }

    [Fact]
    public async Task RequestCloseWhenReady_TreatsAlreadyExitedProcessAsSuccess()
    {
        var result = await InjectorCommandClient.RequestExactProcessCloseWhenReadyAsync(
            new CodexProcessInfo(
                int.MaxValue,
                DateTimeOffset.UtcNow,
                @"C:\Program Files\WindowsApps\OpenAI.Codex\ChatGPT.exe",
                null),
            @"C:\Program Files\WindowsApps\OpenAI.Codex\ChatGPT.exe",
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task EnsureExited_TreatsAlreadyExitedProcessAsSuccess()
    {
        var result = await InjectorCommandClient.EnsureExactProcessExitedAsync(
            new CodexProcessInfo(
                int.MaxValue,
                DateTimeOffset.UtcNow,
                @"C:\Program Files\WindowsApps\OpenAI.Codex\ChatGPT.exe",
                null),
            @"C:\Program Files\WindowsApps\OpenAI.Codex\ChatGPT.exe",
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void RestartManagerShutdown_RejectsPidReuseBeforeRequestingShutdown()
    {
        using var process = Process.GetCurrentProcess();
        var executablePath = process.MainModule!.FileName;
        var result = RestartManagerProcessShutdown.Request(
            new CodexProcessInfo(
                process.Id,
                process.StartTime.ToUniversalTime().AddMinutes(-5),
                executablePath,
                null),
            executablePath);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "themed_launch.process_identity_changed",
            result.Error!.DiagnosticCode);
    }
}
