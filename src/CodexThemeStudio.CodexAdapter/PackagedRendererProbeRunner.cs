using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.CodexAdapter;

public sealed record PackagedRendererProbeProgress(
    string Stage,
    int Port,
    string? ManagedUserDataPath,
    int? ActivatedProcessId = null);

public sealed record PackagedRendererPortProbeResult(
    int ProcessId,
    int Port,
    string BrowserId,
    int TargetCount,
    int EligibleTargetCount,
    IReadOnlyList<string> RouteTypes,
    bool CanaryApplied,
    bool CanaryCleaned,
    string LaunchTransport,
    string? ManagedUserDataPath,
    bool ManagedProfilePopulated);

public sealed class PackagedRendererProbeRunner(
    string nodeExecutablePath,
    string injectorScriptPath)
{
    private readonly string nodePath = RequireFile(nodeExecutablePath);
    private readonly string injectorPath = RequireFile(injectorScriptPath);

    public async Task<OperationResult<PackagedRendererPortProbeResult>> RunAsync(
        string applicationUserModelId,
        string expectedExecutablePath,
        string? managedUserDataPath,
        TimeSpan waitForProcessExit,
        Func<PackagedRendererProbeProgress, Task>? reportProgress,
        CancellationToken cancellationToken,
        bool terminateLaunchedProcessOnFailure = true)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Failure(
                OperationErrorCode.NotImplemented,
                "包激活参数探针仅支持 Windows。",
                "packaged_renderer.unsupported_platform");
        }

        var expectedPath = Path.GetFullPath(expectedExecutablePath);
        if (!File.Exists(expectedPath) ||
            string.IsNullOrWhiteSpace(applicationUserModelId))
        {
            return Failure(
                OperationErrorCode.ValidationFailed,
                "包激活参数探针参数无效。",
                "packaged_renderer.arguments_invalid");
        }

        string? profilePath = null;
        if (managedUserDataPath is not null)
        {
            try
            {
                profilePath = PrepareManagedUserDataPath(managedUserDataPath);
            }
            catch (Exception exception) when (
                exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
                return Failure(
                    OperationErrorCode.ValidationFailed,
                    "隔离用户数据目录无效。",
                    "packaged_renderer.user_data_path_invalid");
            }
        }

        var stopped = await WaitForProcessExitAsync(
            expectedPath,
            waitForProcessExit,
            cancellationToken).ConfigureAwait(false);
        if (!stopped)
        {
            return Failure(
                OperationErrorCode.Conflict,
                "ChatGPT 仍在运行，未执行包激活参数探针。",
                "packaged_renderer.process_still_running");
        }

        var port = ReserveEphemeralPort();
        var activationArguments = BuildActivationArguments(profilePath, port);
        await PublishProgressAsync(
            reportProgress,
            new PackagedRendererProbeProgress("prepared", port, profilePath))
            .ConfigureAwait(false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));
        var token = timeout.Token;
        var completed = false;
        int? launchedProcessId = null;
        Process? node = null;
        try
        {
            launchedProcessId = PackagedApplicationActivator.Activate(
                applicationUserModelId,
                activationArguments);
            await PublishProgressAsync(
                reportProgress,
                new PackagedRendererProbeProgress(
                    "activated",
                    port,
                    profilePath,
                    launchedProcessId))
                .ConfigureAwait(false);

            node = StartProbeProcess(launchedProcessId.Value, expectedPath, port);
            await PublishProgressAsync(
                reportProgress,
                new PackagedRendererProbeProgress(
                    "probing-renderer",
                    port,
                    profilePath,
                    launchedProcessId))
                .ConfigureAwait(false);
            var stdoutTask = ReadLimitedAsync(node.StandardOutput, token);
            var stderrTask = ReadLimitedAsync(node.StandardError, token);
            await node.WaitForExitAsync(token).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            if (node.ExitCode != 0)
            {
                var failure = ReadProbeFailure(stderr);
                return Failure(
                    OperationErrorCode.ExternalToolFailure,
                    failure.UserMessage,
                    failure.DiagnosticCode);
            }

            var probe = ParseProbe(stdout);
            if (probe is null)
            {
                return Failure(
                    OperationErrorCode.InvalidResponse,
                    "Renderer 回环端口返回无效响应。",
                    "packaged_renderer.probe_response_invalid");
            }

            completed = true;
            var profilePopulated = profilePath is not null &&
                IsDirectoryPopulated(profilePath);
            await PublishProgressAsync(
                reportProgress,
                new PackagedRendererProbeProgress(
                    "canary-cleaned",
                    port,
                    profilePath,
                    launchedProcessId))
                .ConfigureAwait(false);
            return OperationResult<PackagedRendererPortProbeResult>.Success(
                new PackagedRendererPortProbeResult(
                    launchedProcessId.Value,
                    port,
                    probe.BrowserId,
                    probe.TargetCount,
                    probe.EligibleTargetCount,
                    probe.RouteTypes,
                    probe.CanaryApplied,
                    probe.CanaryCleaned,
                    "windows-package-activation-arguments",
                    profilePath,
                    profilePopulated));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(
                OperationErrorCode.Timeout,
                "包激活参数探针超时。",
                "packaged_renderer.probe_timeout");
        }
        catch (OperationCanceledException)
        {
            return Failure(
                OperationErrorCode.Cancelled,
                "包激活参数探针已取消。",
                "packaged_renderer.probe_cancelled");
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or
            System.Runtime.InteropServices.COMException or
            InvalidDataException or
            SocketException)
        {
            return Failure(
                OperationErrorCode.ExternalToolFailure,
                "包激活参数启动失败，已执行恢复。",
                "packaged_renderer.launch_failed");
        }
        finally
        {
            if (node is not null)
            {
                TryTerminate(node);
                node.Dispose();
            }

            if (!completed &&
                terminateLaunchedProcessOnFailure &&
                launchedProcessId is { } processId)
            {
                TryTerminateExpectedProcess(processId, expectedPath);
            }
        }
    }

    internal static string BuildActivationArguments(string? managedUserDataPath, int port)
    {
        if (port is < 1024 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        var arguments = "--remote-debugging-address=127.0.0.1 " +
            $"--remote-debugging-port={port}";
        if (managedUserDataPath is null)
        {
            return arguments;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(managedUserDataPath);
        var fullPath = Path.GetFullPath(managedUserDataPath);
        if (!Path.IsPathFullyQualified(fullPath) ||
            fullPath.IndexOfAny(['\0', '"']) >= 0)
        {
            throw new ArgumentException(
                "The managed user-data path is invalid.",
                nameof(managedUserDataPath));
        }

        return $"{arguments} \"--user-data-dir={fullPath}\"";
    }

    private Process StartProbeProcess(int processId, string executablePath, int port)
    {
        var startInfo = InjectorCommandClient.CreateProcessStartInfo(
            nodePath,
            injectorPath,
            [
                "renderer-port-probe",
                "--pid",
                processId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--executable",
                executablePath,
                "--port",
                port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ],
            redirectStandardInput: false);
        var process = new Process { StartInfo = startInfo };
        process.Start();
        return process;
    }

    private static int ReserveEphemeralPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string PrepareManagedUserDataPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!Path.IsPathFullyQualified(fullPath))
        {
            throw new ArgumentException("The user-data path must be absolute.", nameof(path));
        }

        Directory.CreateDirectory(fullPath);
        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("The user-data path cannot be a reparse point.");
        }

        return fullPath;
    }

    private static async Task<bool> WaitForProcessExitAsync(
        string expectedExecutablePath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (IsExpectedProcessRunning(expectedExecutablePath))
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken)
                .ConfigureAwait(false);
        }

        return true;
    }

    private static bool IsExpectedProcessRunning(string expectedExecutablePath)
    {
        var processName = Path.GetFileNameWithoutExtension(expectedExecutablePath);
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                try
                {
                    if (string.Equals(
                            Path.GetFullPath(process.MainModule!.FileName),
                            expectedExecutablePath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or
                    System.ComponentModel.Win32Exception or
                    NotSupportedException)
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static ProbeValue? ParseProbe(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.GetProperty("service").GetString() != "CodexThemeStudio.Injector" ||
                root.GetProperty("protocolVersion").GetInt32() != 1 ||
                root.GetProperty("status").GetString() != "ok")
            {
                return null;
            }

            var probe = root.GetProperty("portProbe");
            return new ProbeValue(
                probe.GetProperty("browserId").GetString() ?? string.Empty,
                probe.GetProperty("targetCount").GetInt32(),
                probe.GetProperty("eligibleTargetCount").GetInt32(),
                probe.GetProperty("routeTypes")
                    .EnumerateArray()
                    .Select(item => item.GetString() ?? string.Empty)
                    .ToArray(),
                probe.GetProperty("canaryApplied").GetBoolean(),
                probe.GetProperty("canaryCleaned").GetBoolean());
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    internal static string ReadDiagnosticCode(string value)
        => ReadProbeFailure(value).DiagnosticCode;

    internal static ProbeFailure ReadProbeFailure(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            var error = document.RootElement.GetProperty("error");
            var diagnostic = error.TryGetProperty("diagnosticCode", out var diagnosticCode) &&
                diagnosticCode.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(diagnosticCode.GetString())
                ? diagnosticCode.GetString()!
                : error.TryGetProperty("code", out var code) &&
                   code.ValueKind == JsonValueKind.String &&
                   !string.IsNullOrWhiteSpace(code.GetString())
                    ? code.GetString()!
                    : "packaged_renderer.port_probe_failed";
            return new ProbeFailure(
                diagnostic,
                BuildProbeFailureMessage(error));
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return new ProbeFailure(
                "packaged_renderer.port_probe_failed",
                "Renderer 回环端口探针失败。");
        }
    }

    private static string BuildProbeFailureMessage(JsonElement error)
    {
        const string fallback = "Renderer 回环端口探针失败。";
        if (!error.TryGetProperty("details", out var details) ||
            details.ValueKind != JsonValueKind.Object ||
            !details.TryGetProperty("targetCount", out var targetCount) ||
            !details.TryGetProperty("candidateCount", out var candidateCount) ||
            targetCount.ValueKind != JsonValueKind.Number ||
            candidateCount.ValueKind != JsonValueKind.Number)
        {
            return fallback;
        }

        var routes = details.TryGetProperty("routeTypes", out var routeTypes) &&
            routeTypes.ValueKind == JsonValueKind.Array
            ? string.Join(",", routeTypes.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Take(8))
            : string.Empty;
        var evaluations = details.TryGetProperty("evaluations", out var evaluationArray) &&
            evaluationArray.ValueKind == JsonValueKind.Array
            ? evaluationArray.EnumerateArray().Take(8).ToArray()
            : [];
        var structures = evaluations.Select(FormatEvaluation)
            .Where(value => !string.IsNullOrWhiteSpace(value));
        var routeSummary = string.IsNullOrWhiteSpace(routes) ? "无" : routes;
        var structureSummary = string.Join(";", structures);
        return $"{fallback} 页面 {targetCount.GetInt32()}，候选 " +
            $"{candidateCount.GetInt32()}，路由 {routeSummary}" +
            (string.IsNullOrWhiteSpace(structureSummary)
                ? "。"
                : $"，结构 {structureSummary}。");
    }

    private static string FormatEvaluation(JsonElement evaluation)
    {
        if (evaluation.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        var reason = evaluation.TryGetProperty("reason", out var reasonValue) &&
            reasonValue.ValueKind == JsonValueKind.String
            ? reasonValue.GetString() ?? "unknown"
            : "unknown";
        if (!evaluation.TryGetProperty("features", out var features) ||
            features.ValueKind != JsonValueKind.Object)
        {
            return reason;
        }

        static int Flag(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.True
                ? 1
                : 0;
        return $"{reason}[shell={Flag(features, "shell")}," +
            $"sidebar={Flag(features, "sidebar")}," +
            $"content={Flag(features, "content")}," +
            $"composer={Flag(features, "composer")}]";
    }

    private static async Task<string> ReadLimitedAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var builder = new StringBuilder();
        while (true)
        {
            var count = await reader.ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            if (count == 0)
            {
                return builder.ToString();
            }

            if (builder.Length + count > 256 * 1024)
            {
                throw new InvalidDataException("Probe output is too large.");
            }

            builder.Append(buffer, 0, count);
        }
    }

    private static Task PublishProgressAsync(
        Func<PackagedRendererProbeProgress, Task>? reportProgress,
        PackagedRendererProbeProgress progress) =>
        reportProgress is null ? Task.CompletedTask : reportProgress(progress);

    private static bool IsDirectoryPopulated(string path)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(path).Any();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void TryTerminateExpectedProcess(int processId, string expectedPath)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var actualPath = Path.GetFullPath(process.MainModule!.FileName);
            if (string.Equals(actualPath, expectedPath, StringComparison.OrdinalIgnoreCase) &&
                !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                _ = process.WaitForExit(5_000);
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidOperationException or
            System.ComponentModel.Win32Exception or
            NotSupportedException)
        {
            // Best effort for the exact probe process only.
        }
    }

    private static string RequireFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        return File.Exists(fullPath)
            ? fullPath
            : throw new FileNotFoundException("Required runtime file was not found.", fullPath);
    }

    private static OperationResult<PackagedRendererPortProbeResult> Failure(
        OperationErrorCode code,
        string message,
        string diagnosticCode) =>
        OperationResult<PackagedRendererPortProbeResult>.Failure(
            code,
            message,
            diagnosticCode);

    internal sealed record ProbeValue(
        string BrowserId,
        int TargetCount,
        int EligibleTargetCount,
        IReadOnlyList<string> RouteTypes,
        bool CanaryApplied,
        bool CanaryCleaned);

    internal sealed record ProbeFailure(
        string DiagnosticCode,
        string UserMessage);
}
