using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.CodexAdapter;

public sealed class InjectorCommandClient :
    ICodexDiscoveryService,
    ICodexInspectorService,
    IInjectorRendererClient
{
    private const int MaxOutputCharacters = 256 * 1024;
    private readonly string nodeExecutablePath;
    private readonly string injectorScriptPath;
    private readonly TimeSpan commandTimeout;

    public InjectorCommandClient(
        string nodeExecutablePath,
        string injectorScriptPath,
        TimeSpan? commandTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(injectorScriptPath);

        this.nodeExecutablePath = Path.GetFullPath(nodeExecutablePath);
        this.injectorScriptPath = Path.GetFullPath(injectorScriptPath);
        this.commandTimeout = commandTimeout ?? TimeSpan.FromSeconds(45);
    }

    public async Task<OperationResult<CodexInstallationInfo>> FindInstallationAsync(
        CancellationToken cancellationToken)
    {
        var response = await ExecuteAsync(["discover"], cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            return OperationResult<CodexInstallationInfo>.Failure(response.Error!);
        }

        try
        {
            var installation = response.Value!.RootElement
                .GetProperty("installation")
                .Deserialize<CodexInstallationInfo>(JsonOptions);

            return installation is not null && ProcessIdentityPolicy.IsOfficialInstallation(installation)
                ? OperationResult<CodexInstallationInfo>.Success(installation)
                : OperationResult<CodexInstallationInfo>.Failure(
                    OperationErrorCode.CodexIdentityMismatch,
                    "检测到的 Codex 安装未通过官方 Store 包身份校验。",
                    "installation_identity_rejected");
        }
        catch (JsonException)
        {
            return InvalidResponse<CodexInstallationInfo>("discover_payload_invalid");
        }
    }

    public async Task<OperationResult<IReadOnlyList<CodexProcessInfo>>> FindProcessesAsync(
        CodexInstallationInfo installation,
        CancellationToken cancellationToken)
    {
        if (!ProcessIdentityPolicy.IsOfficialInstallation(installation))
        {
            return OperationResult<IReadOnlyList<CodexProcessInfo>>.Failure(
                OperationErrorCode.CodexIdentityMismatch,
                "Codex 安装身份未通过校验。",
                "installation_identity_rejected");
        }

        var response = await ExecuteAsync(["discover"], cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            return OperationResult<IReadOnlyList<CodexProcessInfo>>.Failure(response.Error!);
        }

        try
        {
            var processes = response.Value!.RootElement
                .GetProperty("processes")
                .Deserialize<CodexProcessInfo[]>(JsonOptions) ?? [];
            return OperationResult<IReadOnlyList<CodexProcessInfo>>.Success(processes);
        }
        catch (JsonException)
        {
            return InvalidResponse<IReadOnlyList<CodexProcessInfo>>("discover_processes_invalid");
        }
    }

    public async Task<OperationResult<CodexProbeResult>> ProbeAsync(
        CodexProcessInfo process,
        CancellationToken cancellationToken)
    {
        var response = await ExecuteAsync(
            ["probe", "--pid", process.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            return OperationResult<CodexProbeResult>.Failure(response.Error!);
        }

        try
        {
            var probe = response.Value!.RootElement
                .GetProperty("probe")
                .Deserialize<CodexProbeResult>(JsonOptions);
            return probe is null
                ? InvalidResponse<CodexProbeResult>("probe_payload_missing")
                : OperationResult<CodexProbeResult>.Success(probe);
        }
        catch (JsonException)
        {
            return InvalidResponse<CodexProbeResult>("probe_payload_invalid");
        }
    }

    public async Task<OperationResult> CloseInspectorAsync(
        CodexProcessInfo process,
        CancellationToken cancellationToken)
    {
        var response = await ExecuteAsync(
            [
                "close-inspector",
                "--pid",
                process.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ],
            cancellationToken).ConfigureAwait(false);
        return response.IsSuccess
            ? OperationResult.Success()
            : OperationResult.Failure(response.Error!);
    }

    public Task<OperationResult<RendererRuntimeResult>> ApplyAsync(
        CodexProcessInfo process,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken) =>
        ExecuteRendererAsync("renderer-apply", process, payload, cancellationToken);

    public Task<OperationResult<RendererRuntimeResult>> GetStatusAsync(
        CodexProcessInfo process,
        CancellationToken cancellationToken) =>
        ExecuteRendererAsync("renderer-status", process, null, cancellationToken);

    public Task<OperationResult<RendererRuntimeResult>> CleanupAsync(
        CodexProcessInfo process,
        CancellationToken cancellationToken) =>
        ExecuteRendererAsync("renderer-cleanup", process, null, cancellationToken);

    private async Task<OperationResult<RendererRuntimeResult>> ExecuteRendererAsync(
        string command,
        CodexProcessInfo process,
        ReadOnlyMemory<byte>? payload,
        CancellationToken cancellationToken)
    {
        var response = await ExecuteAsync(
            [
                command,
                "--pid",
                process.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ],
            cancellationToken,
            payload).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            return OperationResult<RendererRuntimeResult>.Failure(response.Error!);
        }

        using var document = response.Value!;
        try
        {
            var renderer = document.RootElement
                .GetProperty("renderer")
                .Deserialize<RendererRuntimeResult>(JsonOptions);
            return renderer is null
                ? InvalidResponse<RendererRuntimeResult>("renderer_payload_missing")
                : OperationResult<RendererRuntimeResult>.Success(renderer);
        }
        catch (JsonException)
        {
            return InvalidResponse<RendererRuntimeResult>("renderer_payload_invalid");
        }
    }

    private async Task<OperationResult<JsonDocument>> ExecuteAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        ReadOnlyMemory<byte>? standardInput = null)
    {
        if (!File.Exists(nodeExecutablePath) || !File.Exists(injectorScriptPath))
        {
            return OperationResult<JsonDocument>.Failure(
                OperationErrorCode.ExternalToolFailure,
                "随包 Injector Runtime 不完整。",
                "injector_files_missing");
        }

        var lease = await InspectorOperationLease.AcquireAsync(
            arguments[0],
            cancellationToken);
        if (!lease.IsSuccess)
        {
            return OperationResult<JsonDocument>.Failure(lease.Error!);
        }

        using var inspectorLease = lease.Value!;
        var startInfo = new ProcessStartInfo(nodeExecutablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(injectorScriptPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return OperationResult<JsonDocument>.Failure(
                OperationErrorCode.ExternalToolFailure,
                "无法启动本地 Injector Runtime。",
                "injector_start_failed");
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(commandTimeout);

        try
        {
            var stdoutTask = ReadLimitedAsync(process.StandardOutput, timeoutSource.Token);
            var stderrTask = ReadLimitedAsync(process.StandardError, timeoutSource.Token);
            if (standardInput is { } input)
            {
                await process.StandardInput.BaseStream
                    .WriteAsync(input, timeoutSource.Token)
                    .ConfigureAwait(false);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            var payload = process.ExitCode == 0 ? stdout : stderr;
            var document = JsonDocument.Parse(payload);

            if (process.ExitCode == 0)
            {
                return OperationResult<JsonDocument>.Success(document);
            }

            using (document)
            {
                return OperationResult<JsonDocument>.Failure(ParseError(document.RootElement));
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryTerminate(process);
            return OperationResult<JsonDocument>.Failure(
                OperationErrorCode.Timeout,
                "Codex Inspector 操作超时。",
                "injector_timeout");
        }
        catch (OperationCanceledException)
        {
            TryTerminate(process);
            return OperationResult<JsonDocument>.Failure(
                OperationErrorCode.Cancelled,
                "操作已取消。",
                "injector_cancelled");
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            TryTerminate(process);
            return OperationResult<JsonDocument>.Failure(
                OperationErrorCode.InvalidResponse,
                "Injector 返回了无效的结构化响应。",
                "injector_response_invalid");
        }
        catch (IOException)
        {
            TryTerminate(process);
            return OperationResult<JsonDocument>.Failure(
                OperationErrorCode.ExternalToolFailure,
                "Injector 通信通道意外关闭。",
                "injector_io_failure");
        }
    }

    private static async Task<string> ReadLimitedAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var builder = new StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return builder.ToString();
            }

            if (builder.Length + read > MaxOutputCharacters)
            {
                throw new InvalidDataException("Injector output exceeded the configured limit.");
            }

            builder.Append(buffer, 0, read);
        }
    }

    private static OperationError ParseError(JsonElement root)
    {
        var error = root.TryGetProperty("error", out var errorElement) ? errorElement : default;
        var code = error.ValueKind is JsonValueKind.Object &&
            error.TryGetProperty("code", out var codeElement)
            ? codeElement.GetString()
            : null;
        var retryable = error.ValueKind is JsonValueKind.Object &&
            error.TryGetProperty("retryable", out var retryableElement) &&
            retryableElement.ValueKind is JsonValueKind.True;

        return new OperationError(
            MapErrorCode(code),
            retryable
                ? "Codex Inspector 操作暂时失败，可以重试。"
                : "Codex Inspector 操作未通过安全校验。",
            code);
    }

    private static OperationErrorCode MapErrorCode(string? code) => code switch
    {
        "codex_not_installed" or "codex_not_running" => OperationErrorCode.CodexNotFound,
        "validation_failed" => OperationErrorCode.ValidationFailed,
        "access_denied" => OperationErrorCode.AccessDenied,
        "port_in_use" => OperationErrorCode.PortInUse,
        "identity_changed" => OperationErrorCode.IdentityChanged,
        "process_exited" => OperationErrorCode.ProcessExited,
        "timeout" => OperationErrorCode.Timeout,
        "unsupported_version" => OperationErrorCode.UnsupportedVersion,
        "invalid_response" => OperationErrorCode.InvalidResponse,
        "protocol_rejected" => OperationErrorCode.ProtocolRejected,
        _ => OperationErrorCode.ExternalToolFailure,
    };

    private static OperationResult<T> InvalidResponse<T>(string diagnosticCode) =>
        OperationResult<T>.Failure(
            OperationErrorCode.InvalidResponse,
            "Injector 返回缺少必要字段。",
            diagnosticCode);

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

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

internal sealed class InspectorOperationLease : IDisposable
{
    private const string SemaphoreName =
        @"Local\CodexThemeStudio.InspectorOperationSemaphore.v1";
    private readonly Semaphore? semaphore;
    private readonly bool ownsSemaphore;

    private InspectorOperationLease(Semaphore? semaphore, bool ownsSemaphore)
    {
        this.semaphore = semaphore;
        this.ownsSemaphore = ownsSemaphore;
    }

    public static async Task<OperationResult<InspectorOperationLease>> AcquireAsync(
        string command,
        CancellationToken cancellationToken)
    {
        if (command is "discover" or "prepare" or "self-test" ||
            !OperatingSystem.IsWindows())
        {
            return OperationResult<InspectorOperationLease>.Success(
                new InspectorOperationLease(null, false));
        }

        var semaphore = new Semaphore(
            initialCount: 1,
            maximumCount: 1,
            SemaphoreName);
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        try
        {
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (semaphore.WaitOne(TimeSpan.Zero))
                {
                    return OperationResult<InspectorOperationLease>.Success(
                        new InspectorOperationLease(semaphore, true));
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }

            semaphore.Dispose();
            return OperationResult<InspectorOperationLease>.Failure(
                OperationErrorCode.Conflict,
                "另一个 Codex Inspector 操作正在执行，请稍后重试。",
                "injector.operation_busy");
        }
        catch (OperationCanceledException)
        {
            semaphore.Dispose();
            return OperationResult<InspectorOperationLease>.Failure(
                OperationErrorCode.Cancelled,
                "等待 Codex Inspector 操作锁已取消。",
                "injector.operation_lock_cancelled");
        }
    }

    public void Dispose()
    {
        if (ownsSemaphore)
        {
            semaphore!.Release();
        }

        semaphore?.Dispose();
    }
}
