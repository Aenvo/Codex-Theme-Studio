using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Update;

public sealed partial class GitHubUpdateService : IUpdateService, IDisposable
{
    private const long MaximumZipBytes = 120_000_000;
    private const long MaximumExpandedBytes = 1_073_741_824;
    private const int MaximumEntries = 5_000;
    private const int MaximumReleaseNotesCharacters = 65_536;
    private const long MaximumMetadataBytes = 1_048_576;
    private const int MaximumRedirects = 5;
    private static readonly HashSet<string> AllowedDownloadHosts = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "api.github.com",
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
    };

    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly GitHubUpdateServiceOptions options;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim checkLock = new(1, 1);
    private UpdateCheckResult? cachedResult;
    private DateTimeOffset cachedAt;

    public GitHubUpdateService(
        GitHubUpdateServiceOptions options,
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!SemanticVersion.TryParse(options.CurrentVersion, out _))
        {
            throw new ArgumentException("CurrentVersion must be valid SemVer.", nameof(options));
        }

        this.options = options;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        ownsHttpClient = httpClient is null;
        this.httpClient = httpClient ?? new HttpClient(
            new SocketsHttpHandler { AllowAutoRedirect = false });
        this.httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("CodexThemeStudio", options.CurrentVersion));
        this.httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        this.httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<OperationResult<UpdateCheckResult>> CheckAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        await checkLock.WaitAsync(cancellationToken);
        try
        {
            var now = timeProvider.GetUtcNow();
            if (!forceRefresh && cachedResult is not null &&
                now - cachedAt < options.CacheDuration)
            {
                return OperationResult<UpdateCheckResult>.Success(
                    cachedResult with { WasCached = true });
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.CheckTimeout);
            try
            {
                using var response = await SendFollowingRedirectsAsync(
                    options.LatestReleaseApi,
                    timeout.Token);
                if (response.StatusCode is HttpStatusCode.Forbidden or
                    HttpStatusCode.TooManyRequests)
                {
                    var fallbackRelease = await FetchLatestStableReleaseFromFeedAsync(timeout.Token);
                    var fallbackResult = CreateCheckResult(fallbackRelease);
                    cachedResult = fallbackResult;
                    cachedAt = now;
                    return OperationResult<UpdateCheckResult>.Success(fallbackResult);
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return Failure<UpdateCheckResult>(
                        OperationErrorCode.NotFound,
                        "未找到可用的稳定版本。",
                        "update.github.404");
                }

                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var document = await JsonDocument.ParseAsync(
                    stream,
                    new JsonDocumentOptions { MaxDepth = 32 },
                    timeout.Token);
                var release = ParseRelease(document.RootElement);
                var result = CreateCheckResult(release);
                cachedResult = result;
                cachedAt = now;
                return OperationResult<UpdateCheckResult>.Success(result);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Failure<UpdateCheckResult>(
                    OperationErrorCode.Timeout,
                    "检查更新超时，请重试。",
                    "update.check.timeout");
            }
            catch (OperationCanceledException)
            {
                return Failure<UpdateCheckResult>(
                    OperationErrorCode.Cancelled,
                    "已取消检查更新。",
                    "update.check.cancelled");
            }
            catch (Exception exception) when (
                exception is HttpRequestException or JsonException or
                    InvalidDataException or XmlException)
            {
                return Failure<UpdateCheckResult>(
                    OperationErrorCode.InvalidResponse,
                    "无法验证 GitHub 更新信息，请稍后重试。",
                    $"update.check.{exception.GetType().Name}");
            }
        }
        finally
        {
            checkLock.Release();
        }
    }

    public async Task<OperationResult<StagedUpdate>> DownloadAndStageAsync(
        UpdateReleaseInfo release,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        var token = Guid.NewGuid().ToString("N");
        var workRoot = Path.Combine(options.StagingBaseDirectory, token);
        var partialPath = Path.Combine(workRoot, "package.zip.partial");
        try
        {
            release = await ResolveReleaseAssetsAsync(release, cancellationToken);
            var expectedZipName =
                $"{options.ProductAssetPrefix}-{release.Version}-win-x64-portable.zip";
            var zipAsset = GetUniqueAsset(release.Assets, expectedZipName);
            var manifestAsset = GetUniqueAsset(release.Assets, "release-manifest.json");
            var sumsAsset = GetUniqueAsset(release.Assets, "SHA256SUMS.txt");
            if (release.Assets.Count != 3)
            {
                throw new InvalidDataException("Release contains unexpected assets.");
            }

            ValidateAsset(zipAsset, MaximumZipBytes);
            ValidateAsset(manifestAsset, MaximumMetadataBytes);
            ValidateAsset(sumsAsset, MaximumMetadataBytes);
            Directory.CreateDirectory(workRoot);

            var manifestBytes = await DownloadBytesAsync(
                manifestAsset.DownloadUri,
                MaximumMetadataBytes,
                cancellationToken);
            var sumsBytes = await DownloadBytesAsync(
                sumsAsset.DownloadUri,
                MaximumMetadataBytes,
                cancellationToken);
            var manifest = ParseReleaseManifest(manifestBytes, release.Version, expectedZipName);
            var sumsHash = ParseChecksum(sumsBytes, expectedZipName);
            var apiHash = NormalizeDigest(zipAsset.Sha256Digest);
            if (!FixedEquals(apiHash, manifest.ZipSha256) ||
                !FixedEquals(apiHash, sumsHash))
            {
                throw new InvalidDataException("Release hashes do not agree.");
            }

            progress?.Report(new UpdateDownloadProgress(0, zipAsset.Size, 0, "正在下载更新"));
            await DownloadFileAsync(
                zipAsset.DownloadUri,
                partialPath,
                zipAsset.Size,
                MaximumZipBytes,
                progress,
                cancellationToken);
            var actualHash = await ComputeSha256Async(partialPath, cancellationToken);
            if (!FixedEquals(actualHash, apiHash))
            {
                throw new InvalidDataException("Downloaded ZIP hash is invalid.");
            }

            var zipPath = Path.Combine(workRoot, expectedZipName);
            File.Move(partialPath, zipPath);
            progress?.Report(new UpdateDownloadProgress(
                zipAsset.Size,
                zipAsset.Size,
                100,
                "正在校验并展开更新"));
            var extraction = ExtractVerifiedArchive(zipPath, workRoot);
            var installManifestPath = Path.Combine(
                extraction.ApplicationRoot,
                "app-install-manifest.json");
            if (!File.Exists(installManifestPath))
            {
                throw new InvalidDataException("Install manifest is missing.");
            }

            var installManifestHash = await ComputeSha256Async(
                installManifestPath,
                cancellationToken);
            if (!FixedEquals(installManifestHash, manifest.InstallManifestSha256))
            {
                throw new InvalidDataException("Install manifest hash is invalid.");
            }

            return OperationResult<StagedUpdate>.Success(new StagedUpdate(
                release,
                extraction.ApplicationRoot,
                zipPath,
                actualHash,
                extraction.ExpandedBytes,
                installManifestPath));
        }
        catch (OperationCanceledException)
        {
            TryDeleteDirectory(workRoot);
            return Failure<StagedUpdate>(
                OperationErrorCode.Cancelled,
                "已取消下载更新；未修改任何程序文件。",
                "update.download.cancelled");
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or
            InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            TryDeleteDirectory(workRoot);
            return Failure<StagedUpdate>(
                OperationErrorCode.ValidationFailed,
                "更新包下载或校验失败；未修改任何程序文件。",
                $"update.download.{exception.GetType().Name}");
        }
    }

    public void Dispose()
    {
        checkLock.Dispose();
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }

    private UpdateReleaseInfo ParseRelease(JsonElement root)
    {
        if (root.GetProperty("draft").GetBoolean() ||
            root.GetProperty("prerelease").GetBoolean())
        {
            throw new InvalidDataException("Latest release is not stable.");
        }

        var tag = RequiredString(root, "tag_name");
        var version = ParseVersion(tag, "tag");
        if (version.PreRelease is not null)
        {
            throw new InvalidDataException("Prerelease tag is not allowed.");
        }

        var releaseUri = ParseHttpsUri(RequiredString(root, "html_url"));
        if (!string.Equals(releaseUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Release URL is not hosted by GitHub.");
        }

        var assets = new List<UpdateAssetInfo>();
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            assets.Add(new UpdateAssetInfo(
                RequiredString(asset, "name"),
                ParseHttpsUri(RequiredString(asset, "browser_download_url")),
                asset.GetProperty("size").GetInt64(),
                OptionalString(asset, "digest")));
        }

        var notes = SanitizeReleaseNotes(root.TryGetProperty("body", out var body)
            ? body.GetString()
            : null);
        return new UpdateReleaseInfo(
            version.ToString(),
            tag,
            releaseUri,
            root.GetProperty("published_at").GetDateTimeOffset(),
            notes,
            assets);
    }

    private UpdateCheckResult CreateCheckResult(UpdateReleaseInfo release)
    {
        var current = ParseVersion(options.CurrentVersion, "current version");
        var latest = ParseVersion(release.Version, "release version");
        var isUpdateAvailable = latest.CompareTo(current) > 0;
        return new UpdateCheckResult(
            current.ToString(),
            isUpdateAvailable,
            isUpdateAvailable ? release : null,
            false);
    }

    private async Task<UpdateReleaseInfo> FetchLatestStableReleaseFromFeedAsync(
        CancellationToken cancellationToken)
    {
        var bytes = await DownloadBytesAsync(
            options.ReleasesFeed,
            MaximumMetadataBytes,
            cancellationToken);
        using var stream = new MemoryStream(bytes, writable: false);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumMetadataBytes,
        };
        using var reader = XmlReader.Create(stream, settings);
        var document = XDocument.Load(reader, LoadOptions.None);
        XNamespace atom = "http://www.w3.org/2005/Atom";
        foreach (var entry in document.Root?.Elements(atom + "entry") ?? [])
        {
            var link = entry.Elements(atom + "link")
                .FirstOrDefault(element =>
                    string.Equals((string?)element.Attribute("rel"), "alternate", StringComparison.Ordinal));
            var href = (string?)link?.Attribute("href");
            if (!Uri.TryCreate(href, UriKind.Absolute, out var releaseUri) ||
                releaseUri.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(releaseUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            const string releasePathPrefix = "/Aenvo/Codex-Theme-Studio/releases/tag/";
            if (!releaseUri.AbsolutePath.StartsWith(releasePathPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var tag = Uri.UnescapeDataString(releaseUri.AbsolutePath[releasePathPrefix.Length..]);
            if (!SemanticVersion.TryParse(tag, out var version) || version.PreRelease is not null)
            {
                continue;
            }

            if (!DateTimeOffset.TryParse(
                    entry.Element(atom + "updated")?.Value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var publishedAt))
            {
                throw new InvalidDataException("Release feed date is invalid.");
            }

            var htmlNotes = entry.Element(atom + "content")?.Value;
            var textNotes = WebUtility.HtmlDecode(
                HtmlTagRegex().Replace(HtmlBreakRegex().Replace(htmlNotes ?? string.Empty, "\n"), " "));
            return new UpdateReleaseInfo(
                version.ToString(),
                tag,
                releaseUri,
                publishedAt,
                SanitizeReleaseNotes(textNotes),
                []);
        }

        throw new InvalidDataException("Release feed has no stable release.");
    }

    private async Task<UpdateReleaseInfo> ResolveReleaseAssetsAsync(
        UpdateReleaseInfo release,
        CancellationToken cancellationToken)
    {
        if (release.Assets.Count > 0) return release;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.CheckTimeout);
        using var response = await SendFollowingRedirectsAsync(options.LatestReleaseApi, timeout.Token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var document = await JsonDocument.ParseAsync(
            stream,
            new JsonDocumentOptions { MaxDepth = 32 },
            timeout.Token);
        var resolved = ParseRelease(document.RootElement);
        if (!string.Equals(resolved.Version, release.Version, StringComparison.Ordinal) ||
            !string.Equals(resolved.TagName, release.TagName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Latest release changed before download.");
        }

        return resolved;
    }

    private static string SanitizeReleaseNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return "此版本未提供更新说明。";
        }

        var builder = new StringBuilder(Math.Min(notes.Length, MaximumReleaseNotesCharacters));
        foreach (var character in notes)
        {
            if (builder.Length >= MaximumReleaseNotesCharacters) break;
            if (character is '\r' or '\n' or '\t' || !char.IsControl(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Trim();
    }

    private async Task<HttpResponseMessage> SendFollowingRedirectsAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        ValidateDownloadUri(uri);
        var current = uri;
        for (var count = 0; count <= MaximumRedirects; count++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, current);
            var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!IsRedirect(response.StatusCode)) return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (location is null) throw new InvalidDataException("Redirect has no location.");
            current = location.IsAbsoluteUri ? location : new Uri(current, location);
            ValidateDownloadUri(current);
        }

        throw new InvalidDataException("Too many redirects.");
    }

    private async Task<byte[]> DownloadBytesAsync(
        Uri uri,
        long limit,
        CancellationToken cancellationToken)
    {
        using var response = await SendFollowingRedirectsAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit)
        {
            throw new InvalidDataException("Metadata exceeds size limit.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        await CopyWithLimitAsync(source, destination, limit, null, 0, cancellationToken);
        return destination.ToArray();
    }

    private async Task DownloadFileAsync(
        Uri uri,
        string destinationPath,
        long expectedSize,
        long limit,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await SendFollowingRedirectsAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long contentLength &&
            (contentLength != expectedSize || contentLength > limit))
        {
            throw new InvalidDataException("ZIP size is invalid.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var copied = await CopyWithLimitAsync(
            source,
            destination,
            limit,
            progress,
            expectedSize,
            cancellationToken);
        if (copied != expectedSize)
        {
            throw new InvalidDataException("ZIP size does not match GitHub metadata.");
        }
    }

    private static async Task<long> CopyWithLimitAsync(
        Stream source,
        Stream destination,
        long limit,
        IProgress<UpdateDownloadProgress>? progress,
        long total,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        long copied = 0;
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0) return copied;
                copied += read;
                if (copied > limit) throw new InvalidDataException("Download exceeds size limit.");
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                if (total > 0)
                {
                    progress?.Report(new UpdateDownloadProgress(
                        copied,
                        total,
                        (int)Math.Clamp(copied * 100 / total, 0, 100),
                        "正在下载更新"));
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static (string ApplicationRoot, long ExpandedBytes) ExtractVerifiedArchive(
        string zipPath,
        string workRoot)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count == 0 || archive.Entries.Count > MaximumEntries)
        {
            throw new InvalidDataException("ZIP entry count is invalid.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? packageRoot = null;
        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            var path = entry.FullName.Replace('\\', '/');
            ValidateArchivePath(path);
            var firstSlash = path.IndexOf('/');
            var root = firstSlash < 0 ? path : path[..firstSlash];
            packageRoot ??= root;
            if (!string.Equals(packageRoot, root, StringComparison.Ordinal))
            {
                throw new InvalidDataException("ZIP must have one package root.");
            }

            if (!seen.Add(path)) throw new InvalidDataException("ZIP has duplicate paths.");
            if (IsLink(entry)) throw new InvalidDataException("ZIP links are not allowed.");
            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > MaximumExpandedBytes)
            {
                throw new InvalidDataException("Expanded ZIP exceeds size limit.");
            }
        }

        var applicationRoot = Path.Combine(workRoot, "app");
        Directory.CreateDirectory(applicationRoot);
        var fullApplicationRoot = Path.GetFullPath(applicationRoot) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            var path = entry.FullName.Replace('\\', '/');
            var relative = path[(packageRoot!.Length)..].TrimStart('/');
            if (relative.Length == 0) continue;
            var destination = Path.GetFullPath(Path.Combine(applicationRoot, relative));
            if (!destination.StartsWith(fullApplicationRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("ZIP entry escapes staging root.");
            }

            if (path.EndsWith('/'))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: false);
        }

        return (applicationRoot, expandedBytes);
    }

    private static void ValidateArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') ||
            Path.IsPathRooted(path) || path.Contains(':'))
        {
            throw new InvalidDataException("ZIP path is absolute or contains ADS.");
        }

        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or "..")
            {
                throw new InvalidDataException("ZIP path traversal is not allowed.");
            }
        }
    }

    private static bool IsLink(ZipArchiveEntry entry)
    {
        var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
        return unixType == 0xA000 ||
            (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0;
    }

    private static ReleaseManifest ParseReleaseManifest(
        byte[] bytes,
        string expectedVersion,
        string expectedZipName)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 3 ||
            !string.Equals(RequiredString(root, "version"), expectedVersion, StringComparison.Ordinal) ||
            !string.Equals(RequiredString(root, "packageName") + ".zip", expectedZipName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Release manifest identity is invalid.");
        }

        return new ReleaseManifest(
            NormalizeDigest(RequiredString(root, "zipSha256")),
            NormalizeDigest(RequiredString(root, "installManifestSha256")));
    }

    private static string ParseChecksum(byte[] bytes, string expectedZipName)
    {
        var text = Encoding.UTF8.GetString(bytes);
        var matches = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => ChecksumLineRegex().Match(line))
            .Where(static match => match.Success)
            .Where(match => string.Equals(match.Groups[2].Value, expectedZipName, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1) throw new InvalidDataException("ZIP checksum is missing or duplicated.");
        return NormalizeDigest(matches[0].Groups[1].Value);
    }

    private static UpdateAssetInfo GetUniqueAsset(
        IReadOnlyList<UpdateAssetInfo> assets,
        string name)
    {
        var matches = assets.Where(asset => string.Equals(asset.Name, name, StringComparison.Ordinal)).ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidDataException($"Required asset {name} is missing or duplicated.");
    }

    private static void ValidateAsset(UpdateAssetInfo asset, long maximumSize)
    {
        ValidateDownloadUri(asset.DownloadUri);
        if (asset.Size <= 0 || asset.Size > maximumSize) throw new InvalidDataException("Asset size is invalid.");
        _ = NormalizeDigest(asset.Sha256Digest);
    }

    private static void ValidateDownloadUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps ||
            !AllowedDownloadHosts.Contains(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidDataException("Download URL is not an allowed GitHub HTTPS URL.");
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
        HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;

    private static SemanticVersion ParseVersion(string value, string name) =>
        SemanticVersion.TryParse(value, out var version)
            ? version
            : throw new InvalidDataException($"Invalid {name}.");

    private static Uri ParseHttpsUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri
            : throw new InvalidDataException("Expected an HTTPS URL.");

    private static string RequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidDataException($"Missing {propertyName}.");
        }

        return property.GetString()!;
    }

    private static string OptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static string NormalizeDigest(string value)
    {
        var digest = value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? value[7..]
            : value;
        if (digest.Length != 64 || digest.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("SHA-256 digest is invalid.");
        }

        return digest.ToLowerInvariant();
    }

    private static bool FixedEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(left),
            Convert.FromHexString(right));

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static OperationResult<T> Failure<T>(
        OperationErrorCode code,
        string message,
        string diagnosticCode) =>
        OperationResult<T>.Failure(code, message, diagnosticCode);

    [GeneratedRegex("^([0-9a-fA-F]{64})\\s+\\*?(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ChecksumLineRegex();

    [GeneratedRegex(
        "<(?:br\\s*/?|/p|/li|/h[1-6])\\s*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex HtmlBreakRegex();

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex HtmlTagRegex();

    private sealed record ReleaseManifest(string ZipSha256, string InstallManifestSha256);
}
