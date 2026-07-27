using System.Diagnostics;
using System.Text.Json;
using CodexThemeStudio.Application.MacOS;
using CodexThemeStudio.CodexAdapter.MacOS;
using CodexThemeStudio.MacOS.AcceptanceHarness;

if (args is ["schema"])
{
    Emit(new
    {
        schemaVersion = 1,
        toolVersion = MacProductApplicationService.ToolVersion,
        commands = new[] { "schema", "source-self-test", "run" },
        maximumRequestBytes = AcceptanceProtocol.MaximumRequestBytes,
        maximumResponseBytes = AcceptanceProtocol.MaximumResponseBytes,
        maximumDeadlineMilliseconds =
            AcceptanceProtocol.MaximumDeadlineMilliseconds,
        inspectorDiagnosticMaximumSignalCount = 2,
        inspectorDiagnosticAcceptsTheme = false,
        acceptedOperations = new[]
        {
            "qualification-cycle",
            "inspector-diagnostic",
        },
        acceptedThemeFields = new[]
        {
            "schemaVersion",
            "themeId",
            "variant",
            "palette.background",
            "palette.surface",
            "palette.foreground",
            "palette.muted",
            "palette.accent",
            "palette.border",
        },
    }, 0);
}

if (args is ["source-self-test"])
{
    var configured = false;
    if (MacPackagedRuntimeIdentity.IsConfigured)
    {
        var assemblyId = TryGetAssemblyId(AppContext.BaseDirectory);
        if (assemblyId is not null)
        {
            var composition = await MacProductCompositionRoot.CreateAsync(
                AppContext.BaseDirectory.TrimEnd(
                    Path.DirectorySeparatorChar),
                assemblyId,
                CancellationToken.None);
            configured = composition.IsSuccess;
        }
    }

    Emit(new
    {
        schemaVersion = 1,
        toolVersion = MacProductApplicationService.ToolVersion,
        status = "ok",
        packagedRuntimeIdentityConfigured = configured,
        checks = new[]
        {
            "stdin-only-theme",
            "strict-unknown-field-rejection",
            "short-lived-live-authorization",
            "product-composition-root",
            "bounded-json-output",
            "independent-final-cleanup",
            "theme-free-inspector-diagnostic",
        },
    }, 0);
}

if (args is ["run"])
{
    Guid requestId = Guid.Empty;
    string? operation = null;
    try
    {
        var elapsed = Stopwatch.StartNew();
        var input = await ReadRequestAsync();
        var request = AcceptanceProtocol.Decode(
            input,
            DateTimeOffset.UtcNow);
        requestId = request.RequestId;
        operation = request.Operation;
        using var hardLimit = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(
                AcceptanceProtocol.ProcessHardLimitMilliseconds));
        var composition = await MacProductCompositionRoot.CreateAsync(
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),
            request.Authorization.StagingAssemblyId,
            hardLimit.Token);
        if (!composition.IsSuccess || composition.Service is null)
        {
            Emit(EntryError(
                operation,
                requestId,
                composition.ErrorCode ??
                    "runtime.identity_verification_failed",
                "identity",
                runtimeIdentityVerified: false), 1);
        }

        var mainBudget = TimeSpan.FromMilliseconds(
            AcceptanceProtocol.ProcessHardLimitMilliseconds) -
            MacProductApplicationService.CleanupGrace -
            elapsed.Elapsed;
        if (mainBudget <= TimeSpan.Zero)
        {
            Emit(EntryError(
                operation,
                requestId,
                "operation.timeout",
                "identity",
                runtimeIdentityVerified: true), 1);
        }
        using var deadline = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(Math.Min(
                request.DeadlineMilliseconds,
                mainBudget.TotalMilliseconds)));
        if (request.Operation == "inspector-diagnostic")
        {
            var diagnostic = await composition.Service!
                .RunInspectorDiagnosticAsync(
                    request.RequestId,
                    deadline.Token);
            Emit(diagnostic, diagnostic.Status == "ok" ? 0 : 1);
            return;
        }
        var result = await composition.Service!.RunQualificationCycleAsync(
            request.RequestId,
            request.Theme!,
            deadline.Token);
        Emit(result, result.Status == "ok" ? 0 : 1);
    }
    catch (AcceptanceProtocolException exception)
    {
        Emit(EntryError(
            operation,
            requestId,
            exception.Code,
            exception.Stage,
            runtimeIdentityVerified: false), 1);
    }
    catch
    {
        Emit(EntryError(
            operation,
            requestId,
            "operation.unexpected",
            "harness",
            runtimeIdentityVerified: false), 1);
    }
}

Emit(new
{
    schemaVersion = 1,
    status = "error",
    error = new { code = "usage.invalid", stage = "usage" },
}, 2);

static async Task<byte[]> ReadRequestAsync()
{
    using var input = Console.OpenStandardInput();
    using var output = new MemoryStream();
    var buffer = new byte[8 * 1024];
    while (true)
    {
        var count = await input.ReadAsync(buffer);
        if (count == 0)
        {
            return output.ToArray();
        }
        if (output.Length + count > AcceptanceProtocol.MaximumRequestBytes)
        {
            throw new AcceptanceProtocolException(
                "protocol.request_too_large",
                "request");
        }
        output.Write(buffer, 0, count);
    }
}

static object EntryError(
    string? operation,
    Guid requestId,
    string code,
    string stage,
    bool runtimeIdentityVerified) =>
    operation == "inspector-diagnostic"
        ? MacInspectorDiagnosticResult.CreateUnverifiedFailure(
            MacProductApplicationService.ToolVersion,
            requestId,
            runtimeIdentityVerified,
            code,
            stage)
        : MacQualificationCycleResult.CreateUnverifiedFailure(
            MacProductApplicationService.ToolVersion,
            requestId,
            runtimeIdentityVerified,
            code,
            stage);

static string? TryGetAssemblyId(string baseDirectory)
{
    try
    {
        var macOS = new DirectoryInfo(
            baseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var contents = macOS.Parent;
        var runtime = contents?.Parent;
        var assembly = runtime?.Parent;
        return macOS.Name == "MacOS" &&
            contents?.Name == "Contents" &&
            runtime?.Name == "CodexThemeStudio.runtime"
                ? assembly?.Name
                : null;
    }
    catch
    {
        return null;
    }
}

static void Emit(object value, int exitCode)
{
    var output = AcceptanceProtocol.Encode(value);
    var standardOutput = Console.OpenStandardOutput();
    standardOutput.Write(output);
    standardOutput.WriteByte(0x0A);
    standardOutput.Flush();
    Environment.Exit(exitCode);
}
