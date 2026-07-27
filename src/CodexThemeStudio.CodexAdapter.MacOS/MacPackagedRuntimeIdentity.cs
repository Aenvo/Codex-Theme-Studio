using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;

namespace CodexThemeStudio.CodexAdapter.MacOS;

public sealed record MacRuntimeIdentityVerification(
    bool PackagedRuntimeIdentityConfigured,
    bool ManifestToHelperMatch,
    bool HelperToDotNetMatch,
    bool RuntimeFilesMatch,
    string? ErrorCode)
{
    public bool CompleteChainMatch =>
        PackagedRuntimeIdentityConfigured &&
        ManifestToHelperMatch &&
        HelperToDotNetMatch &&
        RuntimeFilesMatch &&
        ErrorCode is null;
}

public static class MacPackagedRuntimeIdentity
{
    private const int MaximumOutputBytes = 256 * 1024;
    private const int MaximumErrorBytes = 32 * 1024;

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

    public static async Task<MacRuntimeIdentityVerification> VerifyStrictAsync(
        string helperPath,
        CancellationToken cancellationToken)
    {
        if (!VerifyHelper(helperPath))
        {
            return Failure("helper.identity_mismatch");
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = helperPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("self-test");
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return Failure("helper.start_failed");
            }

            var outputTask = ReadBoundedAsync(
                process.StandardOutput.BaseStream,
                MaximumOutputBytes,
                cancellationToken);
            var errorTask = ReadBoundedAsync(
                process.StandardError.BaseStream,
                MaximumErrorBytes,
                cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
                throw;
            }
            var output = await outputTask;
            _ = await errorTask;
            if (process.ExitCode != 0 || output.Length == 0)
            {
                return Failure("helper.runtime_verification_failed");
            }

            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !HasExactProperties(
                    root,
                    "status",
                    "schemaVersion",
                    "protocolVersion",
                    "toolVersion",
                    "packagedRuntimeIdentityConfigured",
                    "manifestToHelperMatch",
                    "runtimeFilesMatch") ||
                root.GetProperty("status").GetString() != "ok" ||
                root.GetProperty("schemaVersion").GetInt32() != 1 ||
                root.GetProperty("protocolVersion").GetInt32() != 1 ||
                string.IsNullOrWhiteSpace(
                    root.GetProperty("toolVersion").GetString()))
            {
                return Failure("helper.runtime_response_invalid");
            }

            var configured = root
                .GetProperty("packagedRuntimeIdentityConfigured")
                .GetBoolean();
            var manifestMatch = root
                .GetProperty("manifestToHelperMatch")
                .GetBoolean();
            var runtimeFilesMatch = root
                .GetProperty("runtimeFilesMatch")
                .GetBoolean();
            if (!configured || !manifestMatch || !runtimeFilesMatch)
            {
                return Failure("helper.runtime_identity_mismatch");
            }

            return new MacRuntimeIdentityVerification(
                true,
                true,
                true,
                true,
                null);
        }
        catch (OperationCanceledException)
        {
            return Failure(
                cancellationToken.IsCancellationRequested
                    ? "operation.cancelled"
                    : "operation.timeout");
        }
        catch (Exception exception) when (
            exception is IOException or
            InvalidOperationException or
            JsonException or
            KeyNotFoundException)
        {
            return Failure("helper.runtime_verification_failed");
        }
    }

    private static MacRuntimeIdentityVerification Failure(string errorCode) =>
        new(false, false, false, false, errorCode);

    private static bool HasExactProperties(
        JsonElement element,
        params string[] expected)
    {
        var actual = element.EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var sortedExpected = expected.Order(StringComparer.Ordinal).ToArray();
        return actual.SequenceEqual(sortedExpected, StringComparer.Ordinal);
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream input,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8 * 1024];
        while (true)
        {
            var count = await input.ReadAsync(buffer, cancellationToken);
            if (count == 0)
            {
                return output.ToArray();
            }

            if (output.Length + count > maximumBytes)
            {
                throw new InvalidDataException();
            }

            output.Write(buffer, 0, count);
        }
    }
}
