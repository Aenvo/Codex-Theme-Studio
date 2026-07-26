using System.Text;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;
using SkiaSharp;
using Xunit.Abstractions;

namespace CodexThemeStudio.Storage.Tests;

public class ImagePipelineTests
{
    private readonly ITestOutputHelper output;

    public ImagePipelineTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png, "photo.jpg", ImageSourceFormat.Png)]
    [InlineData(SKEncodedImageFormat.Jpeg, "photo.webp", ImageSourceFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Webp, "photo.png", ImageSourceFormat.WebP)]
    public async Task Process_AcceptsSupportedContentRegardlessOfExtension(
        SKEncodedImageFormat encodedFormat,
        string sourceFileName,
        ImageSourceFormat expectedFormat)
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var source = TestImageFactory.Create(encodedFormat);
        var pipeline = new ImagePipeline(environment.DataRoot);

        var result = await pipeline.ProcessAsync(
            new MemoryStream(source),
            sourceFileName,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.DiagnosticCode);
        Assert.Equal(expectedFormat, result.Value!.SourceFormat);
        Assert.Equal("image/webp", result.Value.RuntimeBackground.ContentType);
        Assert.Equal(640, result.Value.RuntimeBackground.PixelWidth);
        Assert.Equal(360, result.Value.RuntimeBackground.PixelHeight);
        Assert.Equal(480, result.Value.CardThumbnail.PixelWidth);
        Assert.Equal(270, result.Value.CardThumbnail.PixelHeight);
        Assert.Equal(ImageAspectCategory.Widescreen, result.Value.AspectCategory);
        Assert.Equal(
            ImageTaskModeSuggestion.Banner,
            result.Value.SuggestedTaskMode);

        AssertManagedWebP(environment.DataRoot, result.Value.RuntimeBackground);
        AssertManagedWebP(environment.DataRoot, result.Value.EditorPreview);
        AssertManagedWebP(environment.DataRoot, result.Value.CardThumbnail);
    }

    [Fact]
    public async Task Process_ResizesPreviewsWithoutChangingAspectRatio()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var source = TestImageFactory.Create(
            SKEncodedImageFormat.Png,
            2_400,
            1_200);
        var pipeline = new ImagePipeline(environment.DataRoot);

        var result = await pipeline.ProcessAsync(
            new MemoryStream(source),
            "宽屏 图片.png",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.DiagnosticCode);
        Assert.Equal((2_400, 1_200), Dimensions(result.Value!.RuntimeBackground));
        Assert.Equal((1_600, 800), Dimensions(result.Value.EditorPreview));
        Assert.Equal((480, 240), Dimensions(result.Value.CardThumbnail));
    }

    [Fact]
    public async Task Process_PerformanceSmoke_ReportsManagedImportTime()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var source = TestImageFactory.Create(
            SKEncodedImageFormat.Jpeg,
            2_400,
            1_200);
        var pipeline = new ImagePipeline(environment.DataRoot);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var result = await pipeline.ProcessAsync(
            new MemoryStream(source),
            "performance.jpg",
            Guid.NewGuid(),
            CancellationToken.None);
        stopwatch.Stop();

        Assert.True(result.IsSuccess, result.Error?.DiagnosticCode);
        output.WriteLine(
            "2400x1200 JPEG managed import: {0:F3} ms; source: {1} bytes",
            stopwatch.Elapsed.TotalMilliseconds,
            source.Length);
    }

    [Fact]
    public async Task Process_AppliesExifOrientationAndRemovesMetadata()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var jpeg = TestImageFactory.Create(SKEncodedImageFormat.Jpeg, 60, 40);
        var source = TestImageFactory.WithExifOrientationAndIcc(jpeg, 6);
        var pipeline = new ImagePipeline(environment.DataRoot);

        var result = await pipeline.ProcessAsync(
            new MemoryStream(source),
            "oriented.jpg",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.DiagnosticCode);
        Assert.True(result.Value!.OrientationWasApplied);
        Assert.Equal((40, 60), Dimensions(result.Value.RuntimeBackground));

        var outputPath = Resolve(
            environment.DataRoot,
            result.Value.RuntimeBackground.RelativePath);
        var output = await File.ReadAllBytesAsync(outputPath);
        Assert.DoesNotContain("Exif", Encoding.ASCII.GetString(output));
        Assert.DoesNotContain("ICC_PROFILE", Encoding.ASCII.GetString(output));

        using var oriented = SKBitmap.Decode(output);
        var top = oriented.GetPixel(oriented.Width / 2, 5);
        var bottom = oriented.GetPixel(
            oriented.Width / 2,
            oriented.Height - 6);
        Assert.True(top.Red > bottom.Red + 60);
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>")]
    [InlineData("GIF89a")]
    [InlineData("<html>not an image</html>")]
    public async Task Process_RejectsUnsupportedActiveOrAnimatedFormats(string content)
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var pipeline = new ImagePipeline(environment.DataRoot);

        var result = await pipeline.ProcessAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(content)),
            "payload.png",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "image.format.unsupported",
            result.Error!.DiagnosticCode);
        AssertThemeDirectoryIsEmpty(environment.DataRoot);
    }

    [Fact]
    public async Task Process_RejectsMalformedSupportedHeader()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var pipeline = new ImagePipeline(environment.DataRoot);
        var malformed = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 1, 2,
        };

        var result = await pipeline.ProcessAsync(
            new MemoryStream(malformed),
            "broken.png",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "image.decode.format_mismatch",
            result.Error!.DiagnosticCode);
    }

    [Fact]
    public async Task Process_RejectsEmptyFileWithoutCreatingThemeAssets()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var pipeline = new ImagePipeline(environment.DataRoot);

        var result = await pipeline.ProcessAsync(
            new MemoryStream(),
            "empty.png",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("image.input.empty", result.Error!.DiagnosticCode);
        AssertThemeDirectoryIsEmpty(environment.DataRoot);
    }

    [Fact]
    public async Task Process_RejectsInputLargerThanOneHundredMebibytes()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var pipeline = new ImagePipeline(environment.DataRoot);
        var source = new byte[ImagePipeline.MaximumInputBytes + 1];

        var result = await pipeline.ProcessAsync(
            new MemoryStream(source),
            "large.png",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("image.input.too_large", result.Error!.DiagnosticCode);
    }

    [Theory]
    [InlineData(16_385, 1, "image.dimension.too_large")]
    [InlineData(8_000, 8_000, "image.pixels.too_many")]
    public async Task Process_RejectsUnsafeEncodedDimensionsBeforeDecode(
        int width,
        int height,
        string diagnosticCode)
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var valid = TestImageFactory.Create(SKEncodedImageFormat.Png, 2, 2);
        var source = TestImageFactory.PatchPngDimensions(valid, width, height);
        var pipeline = new ImagePipeline(environment.DataRoot);

        var result = await pipeline.ProcessAsync(
            new MemoryStream(source),
            "dimensions.png",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(diagnosticCode, result.Error!.DiagnosticCode);
    }

    [Fact]
    public async Task Process_ReusesFingerprintCachesAndReportsDuplicateCandidate()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var source = TestImageFactory.Create(SKEncodedImageFormat.Webp);
        var pipeline = new ImagePipeline(environment.DataRoot);

        var first = await pipeline.ProcessAsync(
            new MemoryStream(source),
            "first.webp",
            Guid.NewGuid(),
            CancellationToken.None);
        var second = await pipeline.ProcessAsync(
            new MemoryStream(source),
            "second.webp",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.False(first.Value!.IsDuplicateCandidate);
        Assert.True(second.Value!.IsDuplicateCandidate);
        Assert.True(second.Value.EditorPreview.WasReused);
        Assert.True(second.Value.CardThumbnail.WasReused);
        Assert.Equal(first.Value.SourceSha256, second.Value.SourceSha256);
    }

    [Fact]
    public async Task Process_ConcurrentSameImageSafelySharesPreviewCaches()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var source = TestImageFactory.Create(SKEncodedImageFormat.Png);
        var pipeline = new ImagePipeline(environment.DataRoot);
        var themeIds = Enumerable.Range(0, 16)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        var results = await Task.WhenAll(themeIds.Select((themeId, index) =>
            pipeline.ProcessAsync(
                new MemoryStream(source),
                $"concurrent-{index:D2}.png",
                themeId,
                CancellationToken.None)));

        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Single(results.Select(result => result.Value!.EditorPreview.RelativePath).Distinct());
        Assert.Single(results.Select(result => result.Value!.CardThumbnail.RelativePath).Distinct());
        Assert.Equal(
            themeIds.Length,
            results.Select(result => result.Value!.RuntimeBackground.RelativePath).Distinct().Count());
        Assert.InRange(
            results.Count(result => result.Value!.IsDuplicateCandidate),
            themeIds.Length - 1,
            themeIds.Length);
    }

    [Fact]
    public async Task Process_FailedRuntimeCommitLeavesContentAddressedCachesReusable()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var source = TestImageFactory.Create(SKEncodedImageFormat.Png, 37, 23);
        var pipeline = new ImagePipeline(environment.DataRoot);
        var blockedThemeId = Guid.NewGuid();
        var blockedThemePath = Path.Combine(
            environment.DataRoot,
            StorageLayout.GetThemeDirectory(blockedThemeId));
        await File.WriteAllTextAsync(blockedThemePath, "blocked");

        var failed = await pipeline.ProcessAsync(
            new MemoryStream(source),
            "blocked-runtime.png",
            blockedThemeId,
            CancellationToken.None);

        Assert.False(failed.IsSuccess);
        Assert.Equal("image.storage.io_failure", failed.Error!.DiagnosticCode);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(
            environment.DataRoot,
            StorageLayout.PreviewCacheDirectory)));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(
            environment.DataRoot,
            StorageLayout.ThumbnailCacheDirectory)));

        var retry = await pipeline.ProcessAsync(
            new MemoryStream(source),
            "retry.png",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(retry.IsSuccess, retry.Error?.DiagnosticCode);
        Assert.True(retry.Value!.EditorPreview.WasReused);
        Assert.True(retry.Value.CardThumbnail.WasReused);
    }

    [Fact]
    public async Task Process_CancellationCleansCurrentStagingAndFinalFiles()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var pipeline = new ImagePipeline(environment.DataRoot);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await pipeline.ProcessAsync(
            new MemoryStream(
                TestImageFactory.Create(SKEncodedImageFormat.Png)),
            "cancel.png",
            Guid.NewGuid(),
            cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.Cancelled, result.Error!.Code);
        AssertThemeDirectoryIsEmpty(environment.DataRoot);
        var staging = Path.Combine(
            environment.DataRoot,
            StorageLayout.ImageImportStagingDirectory.Replace(
                '/',
                Path.DirectorySeparatorChar));
        Assert.False(Directory.Exists(staging) &&
                     Directory.EnumerateFileSystemEntries(staging).Any());
    }

    [Fact]
    public async Task Process_ReadFailureReturnsStructuredStorageError()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var pipeline = new ImagePipeline(environment.DataRoot);

        var result = await pipeline.ProcessAsync(
            new ThrowingReadStream(),
            "io.png",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "image.storage.io_failure",
            result.Error!.DiagnosticCode);
    }

    private static void AssertManagedWebP(
        string dataRoot,
        ProcessedImageAsset asset)
    {
        var path = Resolve(dataRoot, asset.RelativePath);
        Assert.True(File.Exists(path));
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length >= 12);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("WEBP", Encoding.ASCII.GetString(bytes, 8, 4));
        Assert.Equal(asset.Length, bytes.LongLength);
        Assert.Equal(64, asset.Sha256.Length);
    }

    private static (int Width, int Height) Dimensions(
        ProcessedImageAsset asset) =>
        (asset.PixelWidth, asset.PixelHeight);

    private static string Resolve(string dataRoot, string relativePath) =>
        Path.Combine(
            dataRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static void AssertThemeDirectoryIsEmpty(string dataRoot)
    {
        var themes = Path.Combine(dataRoot, StorageLayout.ThemesDirectory);
        Assert.False(Directory.Exists(themes) &&
                     Directory.EnumerateFileSystemEntries(themes).Any());
    }

    private sealed class ThrowingReadStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException("Synthetic read failure.");

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
