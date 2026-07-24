using System.Diagnostics;
using System.Text.Json;

namespace CodexThemeStudio.CodexAdapter.Tests;

public sealed class WindowsDiscoveryCompatibilityTests
{
    [Fact]
    public async Task ExactExecutableModes_ReturnStructuredJsonOnWindowsPowerShell51()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var powerShell = Path.Combine(
            Environment.SystemDirectory,
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        Assert.True(File.Exists(powerShell), $"Windows PowerShell 5.1 was not found: {powerShell}");

        using var discover = await InvokeAsync(
            powerShell,
            "Discover",
            processId: null);
        Assert.Equal("manualExecutable", discover.RootElement
            .GetProperty("package")
            .GetProperty("source")
            .GetString());
        Assert.Equal(JsonValueKind.Array, discover.RootElement
            .GetProperty("processes")
            .ValueKind);

        using var snapshot = await InvokeAsync(
            powerShell,
            "Snapshot",
            processId: 0);
        Assert.Equal(JsonValueKind.Null, snapshot.RootElement
            .GetProperty("process")
            .ValueKind);
    }

    private static async Task<JsonDocument> InvokeAsync(
        string powerShell,
        string mode,
        int? processId)
    {
        var script = Path.Combine(
            FindRepositoryRoot(),
            "runtime",
            "injector",
            "windows-discovery.ps1");
        var startInfo = new ProcessStartInfo(powerShell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("-Mode");
        startInfo.ArgumentList.Add(mode);
        if (processId.HasValue)
        {
            startInfo.ArgumentList.Add("-ProcessId");
            startInfo.ArgumentList.Add(processId.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        }

        startInfo.ArgumentList.Add("-ExecutablePath");
        startInfo.ArgumentList.Add(powerShell);

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await process.WaitForExitAsync(timeout.Token);
        var output = await standardOutput;
        var error = await standardError;

        Assert.True(
            process.ExitCode == 0,
            $"windows-discovery.ps1 exited with {process.ExitCode}: {error}");
        Assert.False(
            string.IsNullOrWhiteSpace(output),
            "windows-discovery.ps1 returned an empty response.");
        return JsonDocument.Parse(output);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "runtime",
                    "injector",
                    "windows-discovery.ps1")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root from the test output directory.");
    }
}
