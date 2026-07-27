using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexThemeStudio.CodexAdapter.MacOS;
using CodexThemeStudio.CodexRuntime;

namespace CodexThemeStudio.Application.MacOS;

public sealed record MacProductCompositionResult(
    bool IsSuccess,
    MacProductApplicationService? Service,
    string? ErrorCode);

public static class MacProductCompositionRoot
{
    public static async Task<MacProductCompositionResult> CreateAsync(
        string macOSDirectory,
        string expectedAssemblyId,
        CancellationToken cancellationToken)
    {
        var verification = await MacStagingIdentityVerifier.VerifyAsync(
            macOSDirectory,
            expectedAssemblyId,
            cancellationToken);
        if (!verification.IsSuccess || verification.HelperPath is null)
        {
            return new MacProductCompositionResult(
                false,
                null,
                verification.ErrorCode);
        }

        var bridge = new MacHelperClient(verification.HelperPath);
        var coordinator = new CodexRuntimeCoordinator(
            bridge,
            new InMemoryCodexQualificationStore());
        return new MacProductCompositionResult(
            true,
            new MacProductApplicationService(coordinator),
            null);
    }
}

internal sealed record MacStagingVerification(
    bool IsSuccess,
    string? HelperPath,
    string? ErrorCode);

internal static class MacStagingIdentityVerifier
{
    private const string ReceiptToolVersion = "0.3.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static async Task<MacStagingVerification> VerifyAsync(
        string macOSDirectory,
        string expectedAssemblyId,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!MacPackagedRuntimeIdentity.IsConfigured ||
                !IsSha256(expectedAssemblyId) ||
                !Path.IsPathFullyQualified(macOSDirectory))
            {
                return Failure("runtime.identity_unconfigured");
            }

            var macOS = RequireDirectory(macOSDirectory);
            var contents = RequireDirectory(
                Directory.GetParent(macOS.FullName)?.FullName);
            var runtime = RequireDirectory(
                Directory.GetParent(contents.FullName)?.FullName);
            var assembly = RequireDirectory(
                Directory.GetParent(runtime.FullName)?.FullName);
            if (macOS.Name != "MacOS" ||
                contents.Name != "Contents" ||
                runtime.Name != "CodexThemeStudio.runtime" ||
                !assembly.Name.Equals(
                    expectedAssemblyId,
                    StringComparison.Ordinal))
            {
                return Failure("runtime.layout_invalid");
            }

            var helpers = RequireDirectory(
                Path.Combine(contents.FullName, "Helpers"));
            var resources = RequireDirectory(
                Path.Combine(contents.FullName, "Resources"));
            _ = RequireDirectory(
                Path.Combine(resources.FullName, "runtime"));
            _ = RequireDirectory(
                Path.Combine(resources.FullName, "runtime", "macos"));
            var helperPath = RequireFile(Path.Combine(
                helpers.FullName,
                "CodexThemeStudio.MacHelper"));
            var manifestPath = RequireFile(Path.Combine(
                resources.FullName,
                "runtime-manifest.json"));
            var receiptPath = RequireFile(
                Path.Combine(runtime.FullName, "assembly-receipt.json"));
            if (new FileInfo(receiptPath).Length is <= 0 or > 256 * 1024)
            {
                return Failure("runtime.receipt_invalid");
            }

            var strict = await MacPackagedRuntimeIdentity.VerifyStrictAsync(
                helperPath,
                cancellationToken);
            if (!strict.CompleteChainMatch)
            {
                return Failure(
                    strict.ErrorCode ?? "runtime.identity_mismatch");
            }

            var receiptBytes = await File.ReadAllBytesAsync(
                receiptPath,
                cancellationToken);
            var receipt = JsonSerializer.Deserialize<AssemblyReceipt>(
                receiptBytes,
                JsonOptions);
            if (receipt is null ||
                receipt.SchemaVersion != 2 ||
                receipt.ToolVersion != ReceiptToolVersion ||
                !receipt.CompleteChainMatch ||
                !receipt.HelperToDotNetMatch ||
                !receipt.ManifestToHelperMatch ||
                !receipt.PackagedRuntimeIdentityConfigured ||
                !IsSha256(receipt.AssemblyId) ||
                !IsSha256(receipt.ManifestSha256) ||
                !IsSha256(receipt.HelperSha256) ||
                !IsSha256(receipt.DotnetPayloadHash) ||
                receipt.DotnetFiles is null)
            {
                return Failure("runtime.receipt_invalid");
            }

            var manifestHash = HashFile(manifestPath);
            var helperHash = HashFile(helperPath);
            var payload = BuildManagedPayload(macOS.FullName);
            var payloadHash = HashPayload(payload);
            var assemblyId = HashText(
                $"{manifestHash}\n{helperHash}\n{payloadHash}\n");
            if (!assemblyId.Equals(expectedAssemblyId, StringComparison.Ordinal) ||
                !receipt.AssemblyId.Equals(
                    expectedAssemblyId,
                    StringComparison.Ordinal) ||
                !receipt.ManifestSha256.Equals(
                    manifestHash,
                    StringComparison.Ordinal) ||
                !receipt.HelperSha256.Equals(
                    helperHash,
                    StringComparison.Ordinal) ||
                !receipt.DotnetPayloadHash.Equals(
                    payloadHash,
                    StringComparison.Ordinal) ||
                !receipt.DotnetFiles.SequenceEqual(payload))
            {
                return Failure("runtime.assembly_identity_mismatch");
            }

            return new MacStagingVerification(true, helperPath, null);
        }
        catch (OperationCanceledException)
        {
            return Failure("operation.cancelled");
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            JsonException or
            InvalidOperationException)
        {
            return Failure("runtime.identity_verification_failed");
        }
    }

    internal static IReadOnlyList<ManagedPayloadEntry> BuildManagedPayload(
        string macOSDirectory)
    {
        var directory = RequireDirectory(macOSDirectory);
        var entries = new List<ManagedPayloadEntry>();
        foreach (var path in Directory.EnumerateFiles(
                     directory.FullName,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            var file = new FileInfo(path);
            if (file.LinkTarget is not null ||
                !IsManagedPayloadName(file.Name))
            {
                throw new InvalidDataException();
            }

            entries.Add(new ManagedPayloadEntry(
                $"Contents/MacOS/{file.Name}",
                file.Length,
                HashFile(file.FullName)));
        }

        entries.Sort((left, right) =>
            StringComparer.Ordinal.Compare(left.Path, right.Path));
        if (entries.Count == 0 ||
            entries.Select(entry => entry.Path)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != entries.Count)
        {
            throw new InvalidDataException();
        }

        return entries;
    }

    internal static string HashPayload(
        IReadOnlyList<ManagedPayloadEntry> entries)
    {
        var builder = new StringBuilder();
        foreach (var entry in entries)
        {
            if (!entry.Path.StartsWith(
                    "Contents/MacOS/",
                    StringComparison.Ordinal) ||
                entry.Size <= 0 ||
                !IsSha256(entry.Sha256))
            {
                throw new InvalidDataException();
            }
            builder.Append(entry.Path)
                .Append('\t')
                .Append(entry.Size)
                .Append('\t')
                .Append(entry.Sha256)
                .Append('\n');
        }
        return HashText(builder.ToString());
    }

    private static bool IsManagedPayloadName(string name) =>
        name.EndsWith(".dll", StringComparison.Ordinal) ||
        name.EndsWith(".deps.json", StringComparison.Ordinal) ||
        name.EndsWith(".runtimeconfig.json", StringComparison.Ordinal);

    private static DirectoryInfo RequireDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !Path.IsPathFullyQualified(path))
        {
            throw new InvalidDataException();
        }
        var directory = new DirectoryInfo(Path.GetFullPath(path));
        if (!directory.Exists || directory.LinkTarget is not null)
        {
            throw new InvalidDataException();
        }
        return directory;
    }

    private static string RequireFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists || file.LinkTarget is not null)
        {
            throw new InvalidDataException();
        }
        return fullPath;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashText(string value) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static bool IsSha256(string value) =>
        value is not null &&
        value.Length == 64 &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static MacStagingVerification Failure(string code) =>
        new(false, null, code);

    private sealed record AssemblyReceipt(
        int SchemaVersion,
        string ToolVersion,
        string AssemblyId,
        string ManifestSha256,
        string HelperSha256,
        string DotnetPayloadHash,
        IReadOnlyList<ManagedPayloadEntry> DotnetFiles,
        bool ManifestToHelperMatch,
        bool HelperToDotNetMatch,
        bool CompleteChainMatch,
        bool PackagedRuntimeIdentityConfigured);
}

internal sealed record ManagedPayloadEntry(
    string Path,
    long Size,
    string Sha256);
