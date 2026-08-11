using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexThemeStudio.Contracts.Models;

namespace CodexThemeStudio.Update.Tests;

public sealed class GitHubUpdateServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "cts-update-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CheckAsync_ReturnsNewStableReleaseAndSanitizesNotes()
    {
        var handler = new RouteHandler(_ => JsonResponse(CreateReleaseJson("v1.3.1", "notes\u0001")));
        using var service = CreateService(handler, "1.3.0");

        var result = await service.CheckAsync(true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsUpdateAvailable);
        Assert.Equal("notes", result.Value.Release!.ReleaseNotes);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task CheckAsync_CachesSuccessfulResult()
    {
        var handler = new RouteHandler(_ => JsonResponse(CreateReleaseJson("v1.3.1", "notes")));
        using var service = CreateService(handler, "1.3.0");

        Assert.True((await service.CheckAsync(false, CancellationToken.None)).IsSuccess);
        var cached = await service.CheckAsync(false, CancellationToken.None);

        Assert.True(cached.Value!.WasCached);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData("v1.3.1")]
    [InlineData("v1.3.0")]
    public async Task CheckAsync_DoesNotOfferSameOrOlderRelease(string tag)
    {
        var handler = new RouteHandler(_ => JsonResponse(CreateReleaseJson(tag, "notes")));
        using var service = CreateService(handler, "1.3.1");

        var result = await service.CheckAsync(true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsUpdateAvailable);
        Assert.Null(result.Value.Release);
    }

    [Fact]
    public async Task CheckAsync_RejectsPrerelease()
    {
        var json = CreateReleaseJson("v1.3.1-rc.1", "notes", prerelease: true);
        using var service = CreateService(new RouteHandler(_ => JsonResponse(json)), "1.3.0");

        var result = await service.CheckAsync(true, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task CheckAsync_UsesStableReleaseFeedWhenApiIsRateLimited(
        HttpStatusCode statusCode)
    {
        var handler = new RouteHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/repos/Aenvo/Codex-Theme-Studio/releases/latest" =>
                new HttpResponseMessage(statusCode),
            "/Aenvo/Codex-Theme-Studio/releases.atom" => AtomResponse(
                ("v1.4.0-rc.1", "preview"),
                ("v1.3.1", "<p>stable &amp; safe</p>")),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        using var service = CreateService(handler, "1.3.0");

        var result = await service.CheckAsync(true, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.DiagnosticCode);
        Assert.True(result.Value!.IsUpdateAvailable);
        Assert.Equal("1.3.1", result.Value.Release!.Version);
        Assert.Equal("stable & safe", result.Value.Release.ReleaseNotes);
        Assert.Empty(result.Value.Release.Assets);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task CheckAsync_AcceptsMissingAssetDigestForDiscovery()
    {
        var json = JsonSerializer.Serialize(new
        {
            draft = false,
            prerelease = false,
            tag_name = "v1.3.1",
            html_url = "https://github.com/Aenvo/Codex-Theme-Studio/releases/tag/v1.3.1",
            published_at = DateTimeOffset.UtcNow,
            body = "notes",
            assets = new[]
            {
                new
                {
                    name = "release-manifest.json",
                    browser_download_url = "https://github.com/manifest",
                    size = 100,
                    digest = (string?)null,
                },
            },
        });
        using var service = CreateService(new RouteHandler(_ => JsonResponse(json)), "1.3.0");

        var result = await service.CheckAsync(true, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.DiagnosticCode);
        Assert.Equal(string.Empty, Assert.Single(result.Value!.Release!.Assets).Sha256Digest);
    }

    [Fact]
    public async Task DownloadAndStageAsync_VerifiesThreeHashesAndExtracts()
    {
        var installManifest = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"files\":[]}");
        var zip = CreateZip(("package/app-install-manifest.json", installManifest), ("package/CodexThemeManager.exe", [1, 2, 3]));
        var zipHash = Hash(zip);
        var installHash = Hash(installManifest);
        var zipName = "Codex-Theme-Studio-1.3.1-win-x64-portable.zip";
        var manifest = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            schemaVersion = 3,
            version = "1.3.1",
            packageName = "Codex-Theme-Studio-1.3.1-win-x64-portable",
            zipSha256 = zipHash,
            installManifestSha256 = installHash,
            uncompressedBytes = installManifest.Length + 3,
        }));
        var sums = Encoding.UTF8.GetBytes($"{zipHash} *{zipName}\n");
        var handler = new RouteHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/zip" => BytesResponse(zip),
            "/manifest" => BytesResponse(manifest),
            "/sums" => BytesResponse(sums),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        using var service = CreateService(handler, "1.3.0");
        var release = CreateRelease(zipName, zip.Length, zipHash);

        var result = await service.DownloadAndStageAsync(release, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.DiagnosticCode);
        Assert.True(File.Exists(Path.Combine(result.Value!.StagingRoot, "CodexThemeManager.exe")));
        Assert.Equal(zipHash, result.Value.ZipSha256);
    }

    [Fact]
    public async Task DownloadAndStageAsync_FailsClosedWhenHashesDisagree()
    {
        var zipName = "Codex-Theme-Studio-1.3.1-win-x64-portable.zip";
        var zip = CreateZip(("package/app-install-manifest.json", Encoding.UTF8.GetBytes("{}")));
        var zipHash = Hash(zip);
        var wrongHash = new string('0', 64);
        var manifest = Encoding.UTF8.GetBytes($"{{\"schemaVersion\":3,\"version\":\"1.3.1\",\"packageName\":\"Codex-Theme-Studio-1.3.1-win-x64-portable\",\"zipSha256\":\"{wrongHash}\",\"installManifestSha256\":\"{wrongHash}\",\"uncompressedBytes\":2}}");
        var handler = new RouteHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/manifest" => BytesResponse(manifest),
            "/sums" => BytesResponse(Encoding.UTF8.GetBytes($"{zipHash} *{zipName}\n")),
            _ => BytesResponse(zip),
        });
        using var service = CreateService(handler, "1.3.0");

        var result = await service.DownloadAndStageAsync(
            CreateRelease(zipName, zip.Length, zipHash),
            null,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.False(Directory.Exists(root) && Directory.EnumerateFiles(root, "*.partial", SearchOption.AllDirectories).Any());
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private GitHubUpdateService CreateService(HttpMessageHandler handler, string currentVersion) =>
        new(
            new GitHubUpdateServiceOptions
            {
                CurrentVersion = currentVersion,
                StagingBaseDirectory = root,
            },
            new HttpClient(handler));

    private static UpdateReleaseInfo CreateRelease(string zipName, long zipSize, string zipHash) =>
        new(
            "1.3.1",
            "v1.3.1",
            new Uri("https://github.com/Aenvo/Codex-Theme-Studio/releases/tag/v1.3.1"),
            DateTimeOffset.UtcNow,
            "notes",
            [
                new(zipName, new Uri("https://github.com/zip"), zipSize, $"sha256:{zipHash}"),
                new("release-manifest.json", new Uri("https://github.com/manifest"), 200, $"sha256:{new string('1', 64)}"),
                new("SHA256SUMS.txt", new Uri("https://github.com/sums"), 100, $"sha256:{new string('2', 64)}"),
            ]);

    private static string CreateReleaseJson(string tag, string body, bool prerelease = false) =>
        JsonSerializer.Serialize(new
        {
            draft = false,
            prerelease,
            tag_name = tag,
            html_url = $"https://github.com/Aenvo/Codex-Theme-Studio/releases/tag/{tag}",
            published_at = DateTimeOffset.UtcNow,
            body,
            assets = Array.Empty<object>(),
        });

    private static byte[] CreateZip(params (string Path, byte[] Content)[] files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Path);
                using var target = entry.Open();
                target.Write(file.Content);
            }
        }

        return stream.ToArray();
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage AtomResponse(params (string Tag, string Notes)[] releases)
    {
        var entries = string.Join(string.Empty, releases.Select(release => $"""
          <entry>
            <id>tag:github.com,2008:Repository/1/{release.Tag}</id>
            <updated>2026-08-07T00:00:00Z</updated>
            <link rel="alternate" type="text/html" href="https://github.com/Aenvo/Codex-Theme-Studio/releases/tag/{release.Tag}" />
            <title>{release.Tag}</title>
            <content type="html"><![CDATA[{release.Notes}]]></content>
          </entry>
        """));
        var xml = $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          {entries}
        </feed>
        """;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(xml, Encoding.UTF8, "application/atom+xml"),
        };
    }

    private static HttpResponseMessage BytesResponse(byte[] bytes)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentLength = bytes.Length;
        return response;
    }

    private sealed class RouteHandler(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(route(request));
        }
    }
}
