using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexThemeStudio.CodexAdapter.MacOS;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.CodexAdapter.MacOS.Tests;

public sealed class MacHelperClientTests : IDisposable
{
    private readonly string directory;
    private readonly string helperPath;
    private readonly string helperHash;

    public MacHelperClientTests()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException();
        }

        directory = Path.Combine(
            Path.GetTempPath(),
            "codex-theme-studio-mac-adapter-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        helperPath = Path.Combine(directory, "helper");
        File.WriteAllBytes(helperPath, "fixture"u8.ToArray());
        File.SetUnixFileMode(
            helperPath,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);
        helperHash = Convert.ToHexString(
            SHA256.HashData(File.ReadAllBytes(helperPath)));
    }

    [Fact]
    public async Task SerializesDashedCommandAndMapsVerifiedResponse()
    {
        var request = CreateRequest(CodexPlatformCommand.QualifyAndApply);
        var runner = new FakeRunner(request.RequestId);
        var client = CreateClient(runner);

        var result = await client.ExecuteAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("qualify-and-apply", runner.Command);
        Assert.Equal("com.openai.codex", result.Value!.Installation!.ProductIdentifier);
        Assert.Equal(CodexInspectorDisposition.Closed, result.Value.InspectorDisposition);
        Assert.DoesNotContain("art", runner.RequestJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("css", runner.RequestJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsHelperHashMismatchBeforeStartingProcess()
    {
        var runner = new FakeRunner(Guid.NewGuid());
        var client = new MacHelperClient(
            new MacHelperClientOptions
            {
                HelperPath = helperPath,
                ExpectedSha256 = new string('0', 64),
            },
            runner);

        var result = await client.ExecuteAsync(
            CreateRequest(CodexPlatformCommand.Discover),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.AccessDenied, result.Error!.Code);
        Assert.Equal(0, runner.RunCount);
    }

    [Fact]
    public async Task ProductionIdentityIsUnconfiguredAndFailsBeforeLaunch()
    {
        Assert.False(MacPackagedRuntimeIdentity.IsConfigured);
        Assert.False(MacPackagedRuntimeIdentity.VerifyHelper(helperPath));
        var strict = await MacPackagedRuntimeIdentity.VerifyStrictAsync(
            helperPath,
            CancellationToken.None);
        Assert.False(strict.CompleteChainMatch);
        Assert.Equal("helper.identity_mismatch", strict.ErrorCode);
        var client = new MacHelperClient(helperPath);

        var result = await client.ExecuteAsync(
            CreateRequest(CodexPlatformCommand.Discover),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.AccessDenied, result.Error!.Code);
    }

    [Fact]
    public async Task RejectsMismatchedResponseIdentity()
    {
        var request = CreateRequest(CodexPlatformCommand.Discover);
        var runner = new FakeRunner(Guid.NewGuid());
        var result = await CreateClient(runner).ExecuteAsync(
            request,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.InvalidResponse, result.Error!.Code);
    }

    [Fact]
    public async Task TimeoutIsStructuredAndDoesNotExposeProcessOutput()
    {
        var runner = new FakeRunner(Guid.NewGuid()) { TimedOut = true };
        var result = await CreateClient(runner).ExecuteAsync(
            CreateRequest(CodexPlatformCommand.Cleanup),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.Timeout, result.Error!.Code);
        Assert.Equal("operation.timeout", result.Error.DiagnosticCode);
    }

    [Fact]
    public async Task CancellationBeforeStartIsStructured()
    {
        var runner = new FakeRunner(Guid.NewGuid());
        using var source = new CancellationTokenSource();
        source.Cancel();

        var result = await CreateClient(runner).ExecuteAsync(
            CreateRequest(CodexPlatformCommand.Discover),
            source.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.Cancelled, result.Error!.Code);
        Assert.Equal(0, runner.RunCount);
    }

    [Fact]
    public async Task UnknownResponseFieldFailsClosed()
    {
        var request = CreateRequest(CodexPlatformCommand.Discover);
        var runner = new FakeRunner(request.RequestId)
        {
            AddUnknownResponseField = true,
        };

        var result = await CreateClient(runner).ExecuteAsync(
            request,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.InvalidResponse, result.Error!.Code);
    }

    [Fact]
    public async Task PreservesPrimaryAndRecoveryFailuresSeparately()
    {
        var request = CreateRequest(CodexPlatformCommand.InspectRuntime);
        var runner = new FakeRunner(request.RequestId)
        {
            ReturnStructuredFailure = true,
        };

        var result = await CreateClient(runner).ExecuteAsync(
            request,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("protocol.request_invalid", result.PrimaryError!.Code);
        Assert.Equal("request", result.PrimaryError.Stage);
        Assert.Equal(
            "inspector.close_request_failed",
            result.RecoveryError!.Code);
        Assert.Equal("close", result.RecoveryError.Stage);
    }

    public void Dispose()
    {
        Directory.Delete(directory, recursive: true);
    }

    private MacHelperClient CreateClient(IHelperProcessRunner runner) =>
        new(
            new MacHelperClientOptions
            {
                HelperPath = helperPath,
                ExpectedSha256 = helperHash,
            },
            runner);

    private static CodexPlatformRequest CreateRequest(
        CodexPlatformCommand command) =>
        new(
            1,
            1,
            Guid.NewGuid(),
            command,
            command == CodexPlatformCommand.Discover ? 8_000 : 20_000,
            "/Applications/ChatGPT.app",
            command is CodexPlatformCommand.ApplyTemporary or
                CodexPlatformCommand.QualifyAndApply
                ? new CodexThemeProjection(
                    1,
                    Guid.Parse("5be08c24-d21f-4db0-bdb0-d6d4cc779d7b"),
                    ThemeVariant.Dark,
                    new CodexThemePaletteProjection(
                        "#111111",
                        "#181818",
                        "#2563EB",
                        "#F5F5F5",
                        "#A3A3A3",
                        "#303030"))
                : null);

    private sealed class FakeRunner(Guid responseRequestId) : IHelperProcessRunner
    {
        public bool TimedOut { get; init; }
        public bool AddUnknownResponseField { get; init; }
        public bool ReturnStructuredFailure { get; init; }
        public int RunCount { get; private set; }
        public string RequestJson { get; private set; } = string.Empty;
        public string? Command { get; private set; }

        public Task<HelperProcessResult> RunAsync(
            string executablePath,
            ReadOnlyMemory<byte> request,
            TimeSpan timeout)
        {
            RunCount++;
            RequestJson = Encoding.UTF8.GetString(request.Span);
            Command = JsonDocument.Parse(request).RootElement
                .GetProperty("command")
                .GetString();
            if (TimedOut)
            {
                return Task.FromResult(
                    new HelperProcessResult(-1, [], [], true));
            }

            if (ReturnStructuredFailure)
            {
                var failureOutput = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    schemaVersion = 1,
                    protocolVersion = 1,
                    toolVersion = "0.2.2",
                    requestId = responseRequestId,
                    status = "error",
                    result = (object?)null,
                    error = new
                    {
                        code = "protocol.request_invalid",
                        stage = "request",
                    },
                    recoveryError = new
                    {
                        code = "inspector.close_request_failed",
                        stage = "close",
                    },
                });
                return Task.FromResult(
                    new HelperProcessResult(1, failureOutput, [], false));
            }

            var output = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1,
                protocolVersion = 1,
                toolVersion = "0.1.0",
                requestId = responseRequestId,
                status = "ok",
                result = new
                {
                    installation = new
                    {
                        platform = "macOS",
                        productIdentifier = "com.openai.codex",
                        publisherIdentifier = "2DC432GLL2",
                        version = "1",
                        buildVersion = "1",
                        executablePath =
                            "/Applications/ChatGPT.app/Contents/MacOS/ChatGPT",
                        executableSha256 = new string('A', 64),
                        signatureValid = true,
                        hardenedRuntime = true,
                        gatekeeperAccepted = true,
                    },
                    process = new
                    {
                        processId = 42,
                        parentProcessId = 1,
                        startedAtUtc = "2026-01-01T00:00:00Z",
                        executablePath =
                            "/Applications/ChatGPT.app/Contents/MacOS/ChatGPT",
                        architecture = "arm64",
                    },
                    capabilities = new
                    {
                        pureCssPalette = true,
                        backgroundImage = false,
                        temporaryApply = true,
                        cleanup = true,
                        requalification = true,
                    },
                    cleanupDisposition = "verified",
                    inspectorDisposition = "closed",
                    eligibleWindowCount = 1,
                    appliedWindowCount = 1,
                    residualCount = 0,
                    portListenerCount = 0,
                },
                error = (object?)null,
            });
            if (AddUnknownResponseField)
            {
                var root = System.Text.Json.Nodes.JsonNode.Parse(output)!.AsObject();
                root["privateData"] = "rejected";
                output = JsonSerializer.SerializeToUtf8Bytes(root);
            }
            return Task.FromResult(
                new HelperProcessResult(0, output, [], false));
        }
    }
}
