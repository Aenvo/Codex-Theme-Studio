using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;
using CodexThemeStudio.ThemeCore;

namespace CodexThemeStudio.CodexAdapter;

public sealed class OkkSkinExternalThemeSource : IExternalThemeSource
{
    private const long MaximumImageBytes =
        ImageSizeLimits.MaximumManagedImageBytes;
    private readonly ICodexDiscoveryService discovery;
    private readonly IInjectorRendererClient? rendererClient;
    private readonly ICurrentSessionStore? sessionStore;
    private readonly string root;

    public OkkSkinExternalThemeSource(ICodexDiscoveryService discovery, string? root = null)
        : this(discovery, null, null, root, initialize: true)
    {
    }

    public OkkSkinExternalThemeSource(
        ICodexDiscoveryService discovery,
        IInjectorRendererClient rendererClient,
        string? root = null)
        : this(
            discovery,
            rendererClient ?? throw new ArgumentNullException(nameof(rendererClient)),
            null,
            root,
            initialize: true)
    {
    }

    public OkkSkinExternalThemeSource(
        ICodexDiscoveryService discovery,
        ICurrentSessionStore sessionStore,
        string? root = null)
        : this(
            discovery,
            null,
            sessionStore ?? throw new ArgumentNullException(nameof(sessionStore)),
            root,
            initialize: true)
    {
    }

    private OkkSkinExternalThemeSource(
        ICodexDiscoveryService discovery,
        IInjectorRendererClient? rendererClient,
        ICurrentSessionStore? sessionStore,
        string? root,
        bool initialize)
    {
        _ = initialize;
        this.discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        this.rendererClient = rendererClient;
        this.sessionStore = sessionStore;
        this.root = Path.GetFullPath(root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "okkskin"));
    }

    public async Task<OperationResult<ExternalThemeDescriptor?>> DiscoverCurrentAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            if (!Directory.Exists(root))
            {
                return OperationResult<ExternalThemeDescriptor?>.SuccessOptional(null);
            }

            if (IsReparsePoint(root))
            {
                return Failure<ExternalThemeDescriptor?>(
                    OperationErrorCode.InvalidPath,
                    "OkkSkin 数据目录不能是符号链接或 Junction。",
                    "external.okkskin.root_reparse");
            }

            var statePath = TrustedFile(root, "state.json");
            var currentRoot = TrustedDirectory(root, "current");
            var themePath = TrustedFile(currentRoot, "theme.json");
            if (statePath is null || themePath is null)
            {
                return OperationResult<ExternalThemeDescriptor?>.SuccessOptional(null);
            }

            var state = JsonSerializer.Deserialize<OkkState>(
                await File.ReadAllBytesAsync(statePath, cancellationToken),
                JsonOptions);
            var theme = JsonSerializer.Deserialize<OkkTheme>(
                await File.ReadAllBytesAsync(themePath, cancellationToken),
                JsonOptions);
            if (state is null || theme is null || theme.SchemaVersion != 1 ||
                string.IsNullOrWhiteSpace(state.SkinId) ||
                !string.Equals(state.SkinId, theme.Id, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(theme.Name) ||
                theme.Colors is null ||
                string.IsNullOrWhiteSpace(theme.Image) ||
                theme.Image != Path.GetFileName(theme.Image))
            {
                return Invalid("external.okkskin.metadata_invalid");
            }

            var imagePath = TrustedFile(currentRoot, theme.Image);
            if (imagePath is null)
            {
                return Invalid("external.okkskin.image_invalid");
            }

            var imageInfo = new FileInfo(imagePath);
            if (imageInfo.Length is <= 0 or > MaximumImageBytes)
            {
                return Invalid("external.okkskin.image_size_invalid");
            }

            if (!TryColor(theme.Colors.Background, out var background) ||
                !TryColor(theme.Colors.Panel, out var panel) ||
                !TryColor(theme.Colors.Accent, out var accent) ||
                !TryColor(theme.Colors.Text, out var text) ||
                !TryColor(theme.Colors.Muted, out var muted) ||
                !TryColor(theme.Colors.Line, out var border))
            {
                return Invalid("external.okkskin.color_invalid");
            }

            var sourceIdentifier = $"okkskin:{theme.Id}";
            var themeId = CreateStableId(sourceIdentifier);
            var package = new ThemePackage(
                ThemePackageContractValidator.CurrentSchemaVersion,
                themeId,
                theme.Name.Trim(),
                string.Equals(theme.Variant, "light", StringComparison.OrdinalIgnoreCase)
                    ? ThemeVariant.Light
                    : ThemeVariant.Dark,
                new ThemePalette(background, panel, accent, text, muted, border),
                new ThemeArt(
                    theme.Image,
                    0.5,
                    0.5,
                    ThemeSafeArea.Center,
                    ThemeArtSize.Cover,
                    1,
                    0.6,
                    ThemeTaskMode.Hidden,
                    0,
                    1,
                    0));
            if (ThemePackageContractValidator.Validate(package).Count > 0)
            {
                return Invalid("external.okkskin.theme_invalid");
            }

            var hash = await Sha256Async(imagePath, cancellationToken);
            var applied = await IsAppliedToCurrentProcessAsync(
                state.Enabled,
                state.AppliedPid,
                state.StartedAt,
                cancellationToken);
            return OperationResult<ExternalThemeDescriptor?>.Success(
                new ExternalThemeDescriptor(
                    "OkkSkin",
                    sourceIdentifier,
                    package,
                    theme.Image,
                    hash,
                    state.Enabled,
                    applied,
                    state.AppliedPid,
                    applied
                        ? "Doro 当前由 OkkSkin 持久应用。"
                        : state.Enabled
                            ? "OkkSkin 已配置 Doro；当前 Codex PID 尚未确认。"
                            : "发现 Doro 缓存，但 OkkSkin 持久化未启用。"));
        }
        catch (OperationCanceledException)
        {
            return Failure<ExternalThemeDescriptor?>(
                OperationErrorCode.Cancelled,
                "外部主题发现已取消。",
                "external.okkskin.cancelled");
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return Invalid("external.okkskin.read_failed");
        }
    }

    public async Task<OperationResult<Stream>> OpenImageAsync(
        string sourceIdentifier,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        var descriptor = await DiscoverCurrentAsync(cancellationToken);
        if (!descriptor.IsSuccess)
        {
            return OperationResult<Stream>.Failure(descriptor.Error!);
        }

        if (descriptor.Value is null ||
            descriptor.Value.SourceIdentifier != sourceIdentifier ||
            !string.Equals(descriptor.Value.ImageSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            return Failure<Stream>(
                OperationErrorCode.Conflict,
                "OkkSkin 图片在读取前发生变化；已停止导入。",
                "external.okkskin.image_changed");
        }

        var path = TrustedFile(TrustedDirectory(root, "current"), descriptor.Value.ImageFileName);
        if (path is null)
        {
            return InvalidStream("external.okkskin.image_invalid");
        }

        return OperationResult<Stream>.Success(new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan));
    }

    private async Task<bool> IsAppliedToCurrentProcessAsync(
        bool enabled,
        int? appliedPid,
        DateTimeOffset? stateStartedAt,
        CancellationToken cancellationToken)
    {
        if (!enabled || appliedPid is null or <= 0)
        {
            return false;
        }

        var snapshot = await discovery.DiscoverAsync(cancellationToken);
        if (!snapshot.IsSuccess)
        {
            return false;
        }

        var process = snapshot.Value!.Processes
            .FirstOrDefault(candidate => candidate.ProcessId == appliedPid.Value);
        if (process is null)
        {
            return false;
        }

        if (sessionStore is not null && stateStartedAt is not null)
        {
            var session = await sessionStore.ReadAsync(cancellationToken);
            if (session.IsSuccess &&
                session.Value!.State == ThemeRuntimeState.Default &&
                session.Value.CodexProcessId == process.ProcessId &&
                session.Value.CodexStartedAtUtc == process.StartedAtUtc &&
                session.Value.UpdatedAtUtc >= stateStartedAt.Value)
            {
                return false;
            }
        }

        if (rendererClient is null)
        {
            return true;
        }

        var status = await rendererClient.GetStatusAsync(process, cancellationToken);
        return status.IsSuccess && status.Value!.KnownExternalThemeActive;
    }

    private static bool TryColor(string? value, out string normalized)
    {
        if (ThemeColor.TryNormalize(value, out normalized))
        {
            return true;
        }

        if (ThemeColor.TryParseRgba(value, out var rgba))
        {
            normalized = ThemeColor.Format(rgba);
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    private static Guid CreateStableId(string sourceIdentifier)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sourceIdentifier));
        Span<byte> id = bytes.AsSpan(0, 16);
        id[7] = (byte)((id[7] & 0x0F) | 0x50);
        id[8] = (byte)((id[8] & 0x3F) | 0x80);
        return new Guid(id);
    }

    private static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static string TrustedDirectory(string parent, string name)
    {
        var candidate = Path.GetFullPath(Path.Combine(parent, name));
        var prefix = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               Directory.Exists(candidate) && !IsReparsePoint(candidate)
            ? candidate
            : string.Empty;
    }

    private static string? TrustedFile(string parent, string name)
    {
        if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name) ||
            name != Path.GetFileName(name))
        {
            return null;
        }

        var candidate = Path.GetFullPath(Path.Combine(parent, name));
        var prefix = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               File.Exists(candidate) && !IsReparsePoint(candidate)
            ? candidate
            : null;
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static OperationResult<ExternalThemeDescriptor?> Invalid(string code) =>
        Failure<ExternalThemeDescriptor?>(
            OperationErrorCode.InvalidResponse,
            "OkkSkin 当前主题数据无效；Theme Studio 未导入。",
            code);

    private static OperationResult<Stream> InvalidStream(string code) =>
        Failure<Stream>(
            OperationErrorCode.InvalidResponse,
            "OkkSkin 当前主题图片无效；Theme Studio 未读取。",
            code);

    private static OperationResult<T> Failure<T>(
        OperationErrorCode code,
        string message,
        string diagnosticCode) =>
        OperationResult<T>.Failure(code, message, diagnosticCode);

    private sealed record OkkState(
        string SkinId,
        bool Enabled,
        int? AppliedPid,
        DateTimeOffset? StartedAt);
    private sealed record OkkTheme(
        int SchemaVersion,
        string Id,
        string Name,
        string Variant,
        string Image,
        OkkColors Colors);
    private sealed record OkkColors(
        string Background,
        string Panel,
        string Accent,
        string Text,
        string Muted,
        string Line);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };
}
