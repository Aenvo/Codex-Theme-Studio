using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;
using CodexThemeStudio.ThemeCore;

namespace CodexThemeStudio.CodexAdapter;

public sealed record PersistenceSnapshotDescriptor(
    Guid SnapshotId,
    Guid ThemeId,
    string Fingerprint,
    string SnapshotRoot);

public sealed record VerifiedPersistenceSnapshot(
    PersistenceSnapshotDescriptor Descriptor,
    ThemePackage Theme,
    byte[] Image);

public interface IPersistenceSnapshotStore
{
    Task<OperationResult<PersistenceSnapshotDescriptor>> CreateAsync(
        ThemePackage theme,
        IThemeAssetStore assetStore,
        string snapshotRoot,
        bool createRoot,
        CancellationToken cancellationToken);

    Task<OperationResult<PersistenceSnapshotDescriptor>> GetCurrentDescriptorAsync(
        string snapshotRoot,
        CancellationToken cancellationToken);

    Task<OperationResult> ActivateAsync(
        PersistenceSnapshotDescriptor descriptor,
        CancellationToken cancellationToken);

    Task<OperationResult<VerifiedPersistenceSnapshot>> ReadCurrentAsync(
        string snapshotRoot,
        CancellationToken cancellationToken);
}

public sealed class PersistenceSnapshotStore : IPersistenceSnapshotStore
{
    private const int SchemaVersion = 1;
    private const int MaximumManifestBytes = 16 * 1024;
    private readonly ThemeDocumentSerializer themeSerializer = new();

    public async Task<OperationResult<PersistenceSnapshotDescriptor>> CreateAsync(
        ThemePackage theme,
        IThemeAssetStore assetStore,
        string snapshotRoot,
        bool createRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(assetStore);
        var root = ValidateRoot(snapshotRoot, createRoot);
        if (!root.IsSuccess)
        {
            return OperationResult<PersistenceSnapshotDescriptor>.Failure(root.Error!);
        }

        var serializedTheme = themeSerializer.Serialize(theme);
        if (!serializedTheme.IsSuccess)
        {
            return OperationResult<PersistenceSnapshotDescriptor>.Failure(
                serializedTheme.Error!);
        }

        var imageResult = await ReadImageAsync(theme, assetStore, cancellationToken);
        if (!imageResult.IsSuccess)
        {
            return OperationResult<PersistenceSnapshotDescriptor>.Failure(
                imageResult.Error!);
        }

        var themeHash = Sha256(serializedTheme.Value!);
        var imageHash = Sha256(imageResult.Value!);
        var fingerprint = Sha256(
            Encoding.UTF8.GetBytes($"{themeHash}:{imageHash}"));
        var snapshotId = Guid.NewGuid();
        var snapshotsDirectory = Path.Combine(root.Value!, "snapshots");
        var temporaryDirectory = Path.Combine(
            snapshotsDirectory,
            $".{snapshotId:N}.staging");
        var finalDirectory = Path.Combine(snapshotsDirectory, snapshotId.ToString("D"));
        var imageFileName = $"background{Path.GetExtension(theme.Art.File).ToLowerInvariant()}";
        var manifest = new SnapshotManifest(
            SchemaVersion,
            snapshotId,
            theme.Id,
            "theme.json",
            imageFileName,
            themeHash,
            imageHash,
            fingerprint,
            DateTimeOffset.UtcNow);

        try
        {
            Directory.CreateDirectory(snapshotsDirectory);
            Directory.CreateDirectory(temporaryDirectory);
            await File.WriteAllBytesAsync(
                Path.Combine(temporaryDirectory, "theme.json"),
                serializedTheme.Value!,
                cancellationToken);
            await File.WriteAllBytesAsync(
                Path.Combine(temporaryDirectory, imageFileName),
                imageResult.Value!,
                cancellationToken);
            await File.WriteAllBytesAsync(
                Path.Combine(temporaryDirectory, "manifest.json"),
                JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions),
                cancellationToken);
            Directory.Move(temporaryDirectory, finalDirectory);
            return OperationResult<PersistenceSnapshotDescriptor>.Success(
                new PersistenceSnapshotDescriptor(
                    snapshotId,
                    theme.Id,
                    fingerprint,
                    root.Value!));
        }
        catch (OperationCanceledException)
        {
            return Failure<PersistenceSnapshotDescriptor>(
                OperationErrorCode.Cancelled,
                "持久化快照创建已取消。",
                "persistence.snapshot.cancelled");
        }
        catch (UnauthorizedAccessException)
        {
            return Failure<PersistenceSnapshotDescriptor>(
                OperationErrorCode.AccessDenied,
                "没有权限创建持久化快照。",
                "persistence.snapshot.access_denied");
        }
        catch (IOException)
        {
            return Failure<PersistenceSnapshotDescriptor>(
                OperationErrorCode.StorageUnavailable,
                "无法创建持久化快照。",
                "persistence.snapshot.io_failure");
        }
    }

    public async Task<OperationResult<PersistenceSnapshotDescriptor>>
        GetCurrentDescriptorAsync(
            string snapshotRoot,
            CancellationToken cancellationToken)
    {
        var root = ValidateRoot(snapshotRoot, createRoot: false);
        if (!root.IsSuccess)
        {
            return OperationResult<PersistenceSnapshotDescriptor>.Failure(root.Error!);
        }

        var pointerPath = Path.Combine(root.Value!, "current.json");
        if (!File.Exists(pointerPath))
        {
            return OperationResult<PersistenceSnapshotDescriptor>.Failure(
                OperationErrorCode.NotFound,
                "尚未创建当前主题快照。",
                "persistence.pointer.missing");
        }

        try
        {
            var pointer = JsonSerializer.Deserialize<SnapshotPointer>(
                await File.ReadAllBytesAsync(pointerPath, cancellationToken),
                JsonOptions);
            if (pointer is null ||
                pointer.SchemaVersion != SchemaVersion ||
                pointer.SnapshotId == Guid.Empty ||
                pointer.ThemeId == Guid.Empty ||
                !IsSha256(pointer.Fingerprint))
            {
                return Failure<PersistenceSnapshotDescriptor>(
                    OperationErrorCode.InvalidResponse,
                    "当前主题快照指针已损坏。",
                    "persistence.pointer.invalid");
            }

            return OperationResult<PersistenceSnapshotDescriptor>.Success(
                new PersistenceSnapshotDescriptor(
                    pointer.SnapshotId,
                    pointer.ThemeId,
                    pointer.Fingerprint,
                    root.Value!));
        }
        catch (Exception exception)
            when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return Failure<PersistenceSnapshotDescriptor>(
                OperationErrorCode.StorageUnavailable,
                "无法读取当前主题快照指针。",
                "persistence.pointer.read_failed");
        }
    }

    public async Task<OperationResult> ActivateAsync(
        PersistenceSnapshotDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var root = ValidateRoot(descriptor.SnapshotRoot, createRoot: false);
        if (!root.IsSuccess)
        {
            return OperationResult.Failure(root.Error!);
        }

        var snapshotDirectory = Path.Combine(
            root.Value!,
            "snapshots",
            descriptor.SnapshotId.ToString("D"));
        if (!Directory.Exists(snapshotDirectory))
        {
            return OperationResult.Failure(
                OperationErrorCode.NotFound,
                "要激活的主题快照不存在。",
                "persistence.snapshot.not_found");
        }

        var pointer = new SnapshotPointer(
            SchemaVersion,
            descriptor.SnapshotId,
            descriptor.ThemeId,
            descriptor.Fingerprint);
        return await WriteAtomicAsync(
            Path.Combine(root.Value!, "current.json"),
            JsonSerializer.SerializeToUtf8Bytes(pointer, JsonOptions),
            cancellationToken);
    }

    public async Task<OperationResult<VerifiedPersistenceSnapshot>> ReadCurrentAsync(
        string snapshotRoot,
        CancellationToken cancellationToken)
    {
        var descriptor = await GetCurrentDescriptorAsync(
            snapshotRoot,
            cancellationToken);
        if (!descriptor.IsSuccess)
        {
            return OperationResult<VerifiedPersistenceSnapshot>.Failure(
                descriptor.Error!);
        }

        var value = descriptor.Value!;
        var directory = Path.Combine(
            value.SnapshotRoot,
            "snapshots",
            value.SnapshotId.ToString("D"));
        try
        {
            var manifestPath = Path.Combine(directory, "manifest.json");
            var manifestInfo = new FileInfo(manifestPath);
            if (!manifestInfo.Exists ||
                manifestInfo.Length is <= 0 or > MaximumManifestBytes ||
                (manifestInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return InvalidSnapshot("persistence.snapshot.manifest_invalid");
            }

            var manifest = JsonSerializer.Deserialize<SnapshotManifest>(
                await File.ReadAllBytesAsync(manifestPath, cancellationToken),
                JsonOptions);
            if (manifest is null ||
                manifest.SchemaVersion != SchemaVersion ||
                manifest.SnapshotId != value.SnapshotId ||
                manifest.ThemeId != value.ThemeId ||
                manifest.Fingerprint != value.Fingerprint ||
                !IsSafeFileName(manifest.ThemeFile) ||
                !IsSafeFileName(manifest.ImageFile))
            {
                return InvalidSnapshot("persistence.snapshot.manifest_mismatch");
            }

            var themeBytes = await ReadTrustedFileAsync(
                directory,
                manifest.ThemeFile,
                512 * 1024,
                cancellationToken);
            var imageBytes = await ReadTrustedFileAsync(
                directory,
                manifest.ImageFile,
                RendererPayloadFactory.MaximumImageBytes,
                cancellationToken);
            if (themeBytes is null ||
                imageBytes is null ||
                Sha256(themeBytes) != manifest.ThemeSha256 ||
                Sha256(imageBytes) != manifest.ImageSha256 ||
                Sha256(Encoding.UTF8.GetBytes(
                    $"{manifest.ThemeSha256}:{manifest.ImageSha256}")) !=
                    manifest.Fingerprint)
            {
                return InvalidSnapshot("persistence.snapshot.hash_mismatch");
            }

            var readTheme = themeSerializer.Read(themeBytes);
            if (readTheme.Status != ThemeDocumentReadStatus.Success ||
                readTheme.Theme!.Id != manifest.ThemeId ||
                !RendererPayloadFactory.Create(readTheme.Theme, imageBytes).IsSuccess)
            {
                return InvalidSnapshot("persistence.snapshot.theme_invalid");
            }

            return OperationResult<VerifiedPersistenceSnapshot>.Success(
                new VerifiedPersistenceSnapshot(value, readTheme.Theme, imageBytes));
        }
        catch (Exception exception)
            when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return InvalidSnapshot("persistence.snapshot.read_failed");
        }
    }

    private static async Task<OperationResult<byte[]>> ReadImageAsync(
        ThemePackage theme,
        IThemeAssetStore assetStore,
        CancellationToken cancellationToken)
    {
        var opened = await assetStore.OpenReadAsync(
            $"themes/{theme.Id:D}/{theme.Art.File}",
            cancellationToken);
        if (!opened.IsSuccess)
        {
            return OperationResult<byte[]>.Failure(opened.Error!);
        }

        await using var stream = opened.Value!;
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > RendererPayloadFactory.MaximumImageBytes)
            {
                return Failure<byte[]>(
                    OperationErrorCode.ValidationFailed,
                    "主题图片超过 16 MB 安全上限。",
                    "persistence.snapshot.image_too_large");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        var bytes = buffer.ToArray();
        var payload = RendererPayloadFactory.Create(theme, bytes);
        return payload.IsSuccess
            ? OperationResult<byte[]>.Success(bytes)
            : OperationResult<byte[]>.Failure(payload.Error!);
    }

    private static OperationResult<string> ValidateRoot(
        string snapshotRoot,
        bool createRoot)
    {
        if (string.IsNullOrWhiteSpace(snapshotRoot) ||
            !Path.IsPathFullyQualified(snapshotRoot))
        {
            return OperationResult<string>.Failure(
                OperationErrorCode.InvalidPath,
                "持久化快照目录必须是绝对路径。",
                "persistence.snapshot.root_invalid");
        }

        var root = Path.GetFullPath(snapshotRoot);
        try
        {
            if (!Directory.Exists(root))
            {
                if (!createRoot)
                {
                    return OperationResult<string>.Failure(
                        OperationErrorCode.StorageUnavailable,
                        "持久化快照目录当前不可用；不会创建空主题库。",
                        "persistence.snapshot.root_unavailable");
                }

                Directory.CreateDirectory(root);
            }

            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            {
                return OperationResult<string>.Failure(
                    OperationErrorCode.InvalidPath,
                    "持久化快照目录不能是符号链接或 Junction。",
                    "persistence.snapshot.root_reparse");
            }

            return OperationResult<string>.Success(root);
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResult<string>.Failure(
                OperationErrorCode.AccessDenied,
                "没有权限访问持久化快照目录。",
                "persistence.snapshot.root_access_denied");
        }
        catch (IOException)
        {
            return OperationResult<string>.Failure(
                OperationErrorCode.StorageUnavailable,
                "持久化快照目录不可用。",
                "persistence.snapshot.root_io_failure");
        }
    }

    private static async Task<byte[]?> ReadTrustedFileAsync(
        string directory,
        string fileName,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, fileName);
        var info = new FileInfo(path);
        if (!info.Exists ||
            info.Length is <= 0 ||
            info.Length > maximumBytes ||
            (info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            return null;
        }

        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    private static async Task<OperationResult> WriteAtomicAsync(
        string destination,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var temporary = Path.Combine(
            Path.GetDirectoryName(destination)!,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(destination))
            {
                File.Replace(temporary, destination, null);
            }
            else
            {
                File.Move(temporary, destination);
            }

            return OperationResult.Success();
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Failure(
                OperationErrorCode.Cancelled,
                "原子切换主题快照已取消。",
                "persistence.pointer.cancelled");
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResult.Failure(
                OperationErrorCode.AccessDenied,
                "没有权限切换当前主题快照。",
                "persistence.pointer.access_denied");
        }
        catch (IOException)
        {
            return OperationResult.Failure(
                OperationErrorCode.StorageUnavailable,
                "无法原子切换当前主题快照。",
                "persistence.pointer.io_failure");
        }
    }

    private static bool IsSafeFileName(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value == Path.GetFileName(value) &&
        value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static OperationResult<VerifiedPersistenceSnapshot> InvalidSnapshot(
        string diagnosticCode) =>
        Failure<VerifiedPersistenceSnapshot>(
            OperationErrorCode.InvalidResponse,
            "当前主题快照损坏，Agent 已停止应用。",
            diagnosticCode);

    private static OperationResult<T> Failure<T>(
        OperationErrorCode code,
        string message,
        string diagnosticCode) =>
        OperationResult<T>.Failure(code, message, diagnosticCode);

    private sealed record SnapshotPointer(
        int SchemaVersion,
        Guid SnapshotId,
        Guid ThemeId,
        string Fingerprint);

    private sealed record SnapshotManifest(
        int SchemaVersion,
        Guid SnapshotId,
        Guid ThemeId,
        string ThemeFile,
        string ImageFile,
        string ThemeSha256,
        string ImageSha256,
        string Fingerprint,
        DateTimeOffset CreatedAtUtc);

    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = true,
    };
}
