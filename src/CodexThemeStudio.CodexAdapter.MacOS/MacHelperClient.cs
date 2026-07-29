using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexThemeStudio.CodexRuntime;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.CodexAdapter.MacOS;

public sealed class MacHelperClient : ICodexPlatformBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly MacHelperClientOptions options;
    private readonly IHelperProcessRunner runner;

    public MacHelperClient(string helperPath)
        : this(
            new MacHelperClientOptions
            {
                HelperPath = helperPath,
                ExpectedSha256 = GeneratedMacRuntimeIdentity.HelperSha256,
            },
            new HelperProcessRunner())
    {
    }

    internal MacHelperClient(
        MacHelperClientOptions options,
        IHelperProcessRunner? runner = null)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.runner = runner ?? new HelperProcessRunner();
    }

    public async Task<CodexPlatformBridgeResult> ExecuteAsync(
        CodexPlatformRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            VerifyHelper(options);
            var wireRequest = WireRequest.FromContract(request);
            var requestBytes = JsonSerializer.SerializeToUtf8Bytes(
                wireRequest,
                JsonOptions);
            if (requestBytes.Length > MacHelperClientOptions.MaximumRequestBytes)
            {
                return Failure(
                    OperationErrorCode.ProtocolRejected,
                    "protocol.request_too_large");
            }

            var timeout = TimeSpan.FromMilliseconds(
                checked(request.DeadlineMilliseconds + 2_000));
            var process = await runner.RunAsync(
                options.HelperPath,
                requestBytes,
                timeout);
            if (process.TimedOut)
            {
                return Failure(OperationErrorCode.Timeout, "operation.timeout");
            }
            if (process.StandardOutput.Length == 0 ||
                process.StandardOutput.Length >
                    MacHelperClientOptions.MaximumResponseBytes ||
                process.StandardError.Length >
                    MacHelperClientOptions.MaximumErrorBytes)
            {
                return Failure(
                    OperationErrorCode.InvalidResponse,
                    "protocol.response_invalid");
            }

            WireDocument? document;
            try
            {
                document = JsonSerializer.Deserialize<WireDocument>(
                    process.StandardOutput,
                    JsonOptions);
            }
            catch (JsonException)
            {
                return Failure(
                    OperationErrorCode.InvalidResponse,
                    "protocol.response_invalid");
            }

            if (document is null ||
                document.SchemaVersion != request.SchemaVersion ||
                document.ProtocolVersion != request.ProtocolVersion ||
                document.RequestId != request.RequestId)
            {
                return Failure(
                    OperationErrorCode.InvalidResponse,
                    "protocol.response_mismatch");
            }

            if (process.ExitCode != 0 ||
                document.Status != "ok" ||
                document.Result is null)
            {
                var code = document.Error?.Code ?? "helper.failed";
                return Failure(
                    MapError(code),
                    code,
                    document.Error?.Stage ?? "helper",
                    document.RecoveryError is null
                        ? null
                        : new CodexPlatformFailure(
                            document.RecoveryError.Code,
                            document.RecoveryError.Stage));
            }

            CodexPlatformResponse response;
            try
            {
                response = document.Result.ToContract(
                    document.SchemaVersion,
                    document.ProtocolVersion,
                    document.ToolVersion,
                    document.RequestId);
            }
            catch (InvalidOperationException)
            {
                return Failure(
                    OperationErrorCode.InvalidResponse,
                    "protocol.response_invalid");
            }

            return CodexPlatformBridgeResult.Success(response);
        }
        catch (OperationCanceledException)
        {
            return Failure(OperationErrorCode.Cancelled, "operation.cancelled");
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(
                OperationErrorCode.AccessDenied,
                "helper.identity_mismatch");
        }
        catch (IOException)
        {
            return Failure(
                OperationErrorCode.ExternalToolFailure,
                "helper.io_failed");
        }
        catch (InvalidOperationException)
        {
            return Failure(
                OperationErrorCode.ExternalToolFailure,
                "helper.start_failed");
        }
    }

    private static void VerifyHelper(MacHelperClientOptions options)
    {
        if (!GeneratedMacRuntimeIdentity.IsConfigured &&
            options.ExpectedSha256 == GeneratedMacRuntimeIdentity.HelperSha256)
        {
            throw new UnauthorizedAccessException();
        }

        if (!OperatingSystem.IsMacOS() ||
            !Path.IsPathFullyQualified(options.HelperPath) ||
            options.ExpectedSha256.Length != 64 ||
            !options.ExpectedSha256.All(Uri.IsHexDigit))
        {
            throw new UnauthorizedAccessException();
        }

        var file = new FileInfo(options.HelperPath);
        if (!file.Exists ||
            file.LinkTarget is not null ||
            Path.GetFullPath(options.HelperPath) != options.HelperPath)
        {
            throw new UnauthorizedAccessException();
        }
        var mode = File.GetUnixFileMode(options.HelperPath);
        var executable = UnixFileMode.UserExecute |
            UnixFileMode.GroupExecute |
            UnixFileMode.OtherExecute;
        if ((mode & executable) == 0)
        {
            throw new UnauthorizedAccessException();
        }

        using var stream = file.OpenRead();
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(
            options.ExpectedSha256,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException();
        }
    }

    private static CodexPlatformBridgeResult Failure(
        OperationErrorCode code,
        string diagnosticCode,
        string stage = "adapter",
        CodexPlatformFailure? recoveryError = null) =>
        CodexPlatformBridgeResult.Failure(
            code,
            "macOS Runtime 请求未能安全完成。",
            diagnosticCode,
            stage,
            recoveryError);

    private static OperationErrorCode MapError(string code) =>
        code switch
        {
            "bundle.not_found" or "process.not_running" =>
                OperationErrorCode.CodexNotFound,
            "bundle.identity_mismatch" or "bundle.signature_invalid" or
                "helper.identity_mismatch" or
                "helper.runtime_identity_unconfigured" or
                "helper.runtime_manifest_hash_mismatch" or
                "helper.runtime_hash_mismatch" =>
                OperationErrorCode.CodexIdentityMismatch,
            "process.identity_changed" =>
                OperationErrorCode.IdentityChanged,
            "port.in_use" or "port.non_loopback" or "port.owner_mismatch" =>
                OperationErrorCode.PortInUse,
            "operation.timeout" or "external.command_timeout" =>
                OperationErrorCode.Timeout,
            "inspector.activation_failed" or "inspector.target_invalid" or
                "inspector.close_failed" =>
                OperationErrorCode.InspectorUnavailable,
            "protocol.version_mismatch" or "protocol.request_too_large" or
                "protocol.request_invalid" or "privacy.rejected" =>
                OperationErrorCode.ProtocolRejected,
            "renderer.apply_failed" or "renderer.verify_failed" or
                "renderer.cleanup_failed" =>
                OperationErrorCode.InvalidResponse,
            _ => OperationErrorCode.ExternalToolFailure,
        };

    private sealed record WireRequest(
        int SchemaVersion,
        int ProtocolVersion,
        Guid RequestId,
        string Command,
        int DeadlineMilliseconds,
        string BundlePath,
        WireTheme? Theme)
    {
        public static WireRequest FromContract(CodexPlatformRequest request) =>
            new(
                request.SchemaVersion,
                request.ProtocolVersion,
                request.RequestId,
                request.Command switch
                {
                    CodexPlatformCommand.Discover => "discover",
                    CodexPlatformCommand.InspectRuntime => "inspect-runtime",
                    CodexPlatformCommand.ApplyTemporary => "apply-temporary",
                    CodexPlatformCommand.QualifyAndApply => "qualify-and-apply",
                    CodexPlatformCommand.Cleanup => "cleanup",
                    _ => throw new InvalidOperationException(),
                },
                request.DeadlineMilliseconds,
                request.BundlePath,
                request.Theme is null ? null : WireTheme.FromContract(request.Theme));
    }

    private sealed record WireTheme(
        int SchemaVersion,
        Guid ThemeId,
        string Variant,
        CodexThemePaletteProjection Palette)
    {
        public static WireTheme FromContract(CodexThemeProjection theme) =>
            new(
                theme.SchemaVersion,
                theme.ThemeId,
                theme.Variant.ToString().ToLowerInvariant(),
                theme.Palette);
    }

    private sealed record WireDocument(
        int SchemaVersion,
        int ProtocolVersion,
        string ToolVersion,
        Guid RequestId,
        string Status,
        WireResult? Result,
        WireError? Error,
        WireError? RecoveryError);

    private sealed record WireError(string Code, string Stage);

    private sealed record WireResult(
        WireInstallation? Installation,
        WireProcess? Process,
        CodexRuntimeCapabilities? Capabilities,
        string CleanupDisposition,
        string InspectorDisposition,
        int EligibleWindowCount,
        int AppliedWindowCount,
        int ResidualCount,
        int PortListenerCount)
    {
        public CodexPlatformResponse ToContract(
            int schemaVersion,
            int protocolVersion,
            string toolVersion,
            Guid requestId)
        {
            if (string.IsNullOrWhiteSpace(toolVersion) ||
                EligibleWindowCount < 0 ||
                AppliedWindowCount < 0 ||
                ResidualCount < 0 ||
                PortListenerCount < 0)
            {
                throw new InvalidOperationException();
            }

            return new(
                schemaVersion,
                protocolVersion,
                toolVersion,
                requestId,
                true,
                Installation?.ToContract(),
                Process?.ToContract(),
                Capabilities,
                CleanupDisposition switch
                {
                    "notNeeded" => CodexCleanupDisposition.NotNeeded,
                    "verified" => CodexCleanupDisposition.Verified,
                    "unverified" => CodexCleanupDisposition.Unverified,
                    _ => throw new InvalidOperationException(),
                },
                InspectorDisposition switch
                {
                    "notOpened" => CodexInspectorDisposition.NotOpened,
                    "closed" => CodexInspectorDisposition.Closed,
                    "residual" => CodexInspectorDisposition.Residual,
                    "unknown" => CodexInspectorDisposition.Unknown,
                    _ => throw new InvalidOperationException(),
                },
                EligibleWindowCount,
                AppliedWindowCount,
                ResidualCount,
                PortListenerCount,
                null);
        }
    }

    private sealed record WireInstallation(
        string Platform,
        string ProductIdentifier,
        string PublisherIdentifier,
        string Version,
        string BuildVersion,
        string ExecutablePath,
        string ExecutableSha256,
        bool SignatureValid,
        bool HardenedRuntime,
        bool GatekeeperAccepted)
    {
        public CodexInstallationIdentityV2 ToContract() =>
            ProductIdentifier.Length > 0 &&
            PublisherIdentifier.Length > 0 &&
            ExecutablePath.StartsWith("/", StringComparison.Ordinal) &&
            ExecutableSha256.Length == 64 &&
            ExecutableSha256.All(Uri.IsHexDigit)
                ? new(
                Platform == "macOS"
                    ? CodexHostPlatform.MacOS
                    : throw new InvalidOperationException(),
                ProductIdentifier,
                PublisherIdentifier,
                Version,
                BuildVersion,
                ExecutablePath,
                ExecutableSha256,
                SignatureValid,
                HardenedRuntime,
                GatekeeperAccepted)
                : throw new InvalidOperationException();
    }

    private sealed record WireProcess(
        int ProcessId,
        int ParentProcessId,
        DateTimeOffset StartedAtUtc,
        string ExecutablePath,
        string Architecture)
    {
        public CodexProcessIdentityV2 ToContract() =>
            ProcessId > 0 &&
            ParentProcessId >= 0 &&
            ExecutablePath.StartsWith("/", StringComparison.Ordinal) &&
            Architecture == "arm64"
                ? new(
                ProcessId,
                ParentProcessId,
                StartedAtUtc,
                ExecutablePath,
                Architecture)
                : throw new InvalidOperationException();
    }
}
