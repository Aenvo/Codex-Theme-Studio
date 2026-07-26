using System.Security.Cryptography;
using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;
using SkiaSharp;

namespace CodexThemeStudio.Storage;

public sealed class ImagePipeline : IImagePipeline
{
    public const long MaximumInputBytes = ImageSizeLimits.MaximumSourceBytes;
    public const int MaximumManagedImageBytes =
        ImageSizeLimits.MaximumManagedImageBytes;
    public const int MaximumDimension = 16_384;
    public const long MaximumPixels = 50_000_000;
    public const int EditorPreviewMaximumWidth = 1_600;
    public const int EditorPreviewMaximumHeight = 1_000;
    public const int CardThumbnailMaximumWidth = 480;
    public const int CardThumbnailMaximumHeight = 320;
    public const int WebPQuality = 90;

    private const string WebPContentType = "image/webp";

    private readonly TrustedPathResolver pathResolver;

    public ImagePipeline(string dataRoot)
    {
        pathResolver = new TrustedPathResolver(dataRoot);
    }

    public async Task<OperationResult<ProcessedImageSet>> ProcessAsync(
        Stream source,
        string sourceFileName,
        Guid themeId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.CanRead)
        {
            return Failure(
                OperationErrorCode.ValidationFailed,
                "所选图片流不可读取。",
                "image.source.unreadable");
        }

        if (string.IsNullOrWhiteSpace(sourceFileName) ||
            sourceFileName.Any(char.IsControl))
        {
            return Failure(
                OperationErrorCode.ValidationFailed,
                "图片文件名无效。",
                "image.source_name.invalid");
        }

        if (themeId == Guid.Empty)
        {
            return Failure(
                OperationErrorCode.ValidationFailed,
                "主题 ID 不能为空。",
                "image.theme_id.empty");
        }

        var rootCheck = pathResolver.EnsureRootIsTrusted();
        if (!rootCheck.IsSuccess)
        {
            return OperationResult<ProcessedImageSet>.Failure(rootCheck.Error!);
        }

        var operationId = Guid.NewGuid();
        var stagingRelative =
            $"{StorageLayout.ImageImportStagingDirectory}/{operationId:N}";
        var stagingPathResult = pathResolver.Resolve(stagingRelative);
        if (!stagingPathResult.IsSuccess)
        {
            return OperationResult<ProcessedImageSet>.Failure(
                stagingPathResult.Error!);
        }

        try
        {
            var sourceBytesResult = await ReadBoundedAsync(
                source,
                cancellationToken);
            if (!sourceBytesResult.IsSuccess)
            {
                return OperationResult<ProcessedImageSet>.Failure(
                    sourceBytesResult.Error!);
            }

            var sourceBytes = sourceBytesResult.Value!;
            var detectedFormat = DetectFormat(sourceBytes);
            if (detectedFormat is null)
            {
                return Failure(
                    OperationErrorCode.ValidationFailed,
                    "只支持内容有效的 PNG、JPEG 或 WebP 图片。",
                    "image.format.unsupported");
            }

            var sourceSha256 = Hash(sourceBytes);
            var decodedResult = await Task.Run(
                () => DecodeAndEncode(
                    sourceBytes,
                    detectedFormat.Value,
                    cancellationToken),
                cancellationToken);
            if (!decodedResult.IsSuccess)
            {
                return OperationResult<ProcessedImageSet>.Failure(
                    decodedResult.Error!);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(stagingPathResult.Value!);

            var encoded = decodedResult.Value!;
            var runtimeStage = Path.Combine(
                stagingPathResult.Value!,
                "runtime.webp");
            var editorStage = Path.Combine(
                stagingPathResult.Value!,
                "editor.webp");
            var cardStage = Path.Combine(
                stagingPathResult.Value!,
                "card.webp");

            await File.WriteAllBytesAsync(
                runtimeStage,
                encoded.Runtime.Bytes,
                cancellationToken);
            await File.WriteAllBytesAsync(
                editorStage,
                encoded.Editor.Bytes,
                cancellationToken);
            await File.WriteAllBytesAsync(
                cardStage,
                encoded.Card.Bytes,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var runtimeRelative =
                $"{StorageLayout.GetThemeDirectory(themeId)}/background-{encoded.Runtime.Sha256}.webp";
            var editorRelative =
                $"{StorageLayout.PreviewCacheDirectory}/{encoded.Editor.Sha256}.webp";
            var cardRelative =
                $"{StorageLayout.ThumbnailCacheDirectory}/{encoded.Card.Sha256}.webp";

            var editorCommit = await CommitAsync(
                editorStage,
                editorRelative,
                encoded.Editor,
                ProcessedImageKind.EditorPreview,
                cancellationToken);
            if (!editorCommit.IsSuccess)
            {
                return OperationResult<ProcessedImageSet>.Failure(
                    editorCommit.Error!);
            }

            var cardCommit = await CommitAsync(
                cardStage,
                cardRelative,
                encoded.Card,
                ProcessedImageKind.CardThumbnail,
                cancellationToken);
            if (!cardCommit.IsSuccess)
            {
                return OperationResult<ProcessedImageSet>.Failure(
                    cardCommit.Error!);
            }

            var runtimeCommit = await CommitAsync(
                runtimeStage,
                runtimeRelative,
                encoded.Runtime,
                ProcessedImageKind.RuntimeBackground,
                cancellationToken);
            if (!runtimeCommit.IsSuccess)
            {
                return OperationResult<ProcessedImageSet>.Failure(
                    runtimeCommit.Error!);
            }

            var result = new ProcessedImageSet(
                themeId,
                detectedFormat.Value,
                sourceSha256,
                encoded.OrientationWasApplied,
                ClassifyAspect(encoded.Runtime.Width, encoded.Runtime.Height),
                SuggestTaskMode(encoded.Runtime.Width, encoded.Runtime.Height),
                Path.GetFileName(runtimeRelative),
                runtimeCommit.Value!,
                editorCommit.Value!,
                cardCommit.Value!,
                editorCommit.Value!.WasReused || cardCommit.Value!.WasReused);
            return OperationResult<ProcessedImageSet>.Success(result);
        }
        catch (OperationCanceledException)
        {
            return Failure(
                OperationErrorCode.Cancelled,
                "图片处理已取消。",
                "image.processing.cancelled");
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(
                OperationErrorCode.AccessDenied,
                "没有权限写入图片资源目录。",
                "image.storage.access_denied");
        }
        catch (IOException exception) when (IsDiskFull(exception))
        {
            return Failure(
                OperationErrorCode.StorageUnavailable,
                "磁盘空间不足，无法保存图片资源。",
                "image.storage.disk_full");
        }
        catch (IOException)
        {
            return Failure(
                OperationErrorCode.StorageUnavailable,
                "图片资源存储不可用。",
                "image.storage.io_failure");
        }
        catch (OutOfMemoryException)
        {
            return Failure(
                OperationErrorCode.ValidationFailed,
                "图片解码需要的内存超过安全范围。",
                "image.decode.memory_limit");
        }
        catch (ArgumentException)
        {
            return Failure(
                OperationErrorCode.ValidationFailed,
                "图片数据无效，无法安全解码。",
                "image.decode.invalid_data");
        }
        catch (InvalidOperationException)
        {
            return Failure(
                OperationErrorCode.ValidationFailed,
                "图片解码或重新编码失败。",
                "image.processing.failed");
        }
        finally
        {
            CleanupStagingDirectory(stagingPathResult.Value!);
        }
    }

    private static async Task<OperationResult<byte[]>> ReadBoundedAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        long total = 0;

        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > MaximumInputBytes)
            {
                return OperationResult<byte[]>.Failure(
                    OperationErrorCode.ValidationFailed,
                    "图片文件超过 100 MiB 上限。",
                    "image.input.too_large");
            }

            await buffer.WriteAsync(
                chunk.AsMemory(0, read),
                cancellationToken);
        }

        if (total == 0)
        {
            return OperationResult<byte[]>.Failure(
                OperationErrorCode.ValidationFailed,
                "图片文件为空。",
                "image.input.empty");
        }

        return OperationResult<byte[]>.Success(buffer.ToArray());
    }

    private static OperationResult<EncodedImageSet> DecodeAndEncode(
        byte[] sourceBytes,
        ImageSourceFormat detectedFormat,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var data = SKData.CreateCopy(sourceBytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || !MatchesCodecFormat(detectedFormat, codec.EncodedFormat))
        {
            return OperationResult<EncodedImageSet>.Failure(
                OperationErrorCode.ValidationFailed,
                "图片内容无法按检测到的格式解码。",
                "image.decode.format_mismatch");
        }

        if (codec.FrameCount > 1)
        {
            return OperationResult<EncodedImageSet>.Failure(
                OperationErrorCode.ValidationFailed,
                "不支持动画图片。",
                "image.decode.animated");
        }

        var width = codec.Info.Width;
        var height = codec.Info.Height;
        if (width <= 0 || height <= 0)
        {
            return OperationResult<EncodedImageSet>.Failure(
                OperationErrorCode.ValidationFailed,
                "图片宽高无效。",
                "image.dimension.invalid");
        }

        if (width > MaximumDimension || height > MaximumDimension)
        {
            return OperationResult<EncodedImageSet>.Failure(
                OperationErrorCode.ValidationFailed,
                $"图片单边不能超过 {MaximumDimension} 像素。",
                "image.dimension.too_large");
        }

        var pixels = checked((long)width * height);
        if (pixels > MaximumPixels)
        {
            return OperationResult<EncodedImageSet>.Failure(
                OperationErrorCode.ValidationFailed,
                "图片总像素不能超过 5000 万。",
                "image.pixels.too_many");
        }

        var decodeInfo = new SKImageInfo(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul);
        using var decoded = new SKBitmap(decodeInfo);
        var decodeResult = codec.GetPixels(
            decodeInfo,
            decoded.GetPixels());
        if (decodeResult is not SKCodecResult.Success)
        {
            return OperationResult<EncodedImageSet>.Failure(
                OperationErrorCode.ValidationFailed,
                "图片数据不完整或已损坏。",
                "image.decode.failed");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var orientationWasApplied =
            codec.EncodedOrigin is not SKEncodedOrigin.TopLeft;
        using var oriented = ApplyOrientation(decoded, codec.EncodedOrigin);
        var runtime = EncodeWebP(oriented);
        if (runtime.Bytes.Length > MaximumManagedImageBytes)
        {
            return OperationResult<EncodedImageSet>.Failure(
                OperationErrorCode.ValidationFailed,
                "图片重新编码后超过 32 MiB 上限，请降低图片复杂度或尺寸。",
                "image.output.too_large");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var editorBitmap = ResizeContained(
            oriented,
            EditorPreviewMaximumWidth,
            EditorPreviewMaximumHeight);
        var editor = EncodeWebP(editorBitmap);
        if (editor.Bytes.Length > MaximumManagedImageBytes)
        {
            return OperationResult<EncodedImageSet>.Failure(
                OperationErrorCode.ValidationFailed,
                "图片预览重新编码后超过 32 MiB 上限。",
                "image.preview_output.too_large");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var cardBitmap = ResizeContained(
            oriented,
            CardThumbnailMaximumWidth,
            CardThumbnailMaximumHeight);
        var card = EncodeWebP(cardBitmap);
        if (card.Bytes.Length > MaximumManagedImageBytes)
        {
            return OperationResult<EncodedImageSet>.Failure(
                OperationErrorCode.ValidationFailed,
                "图片缩略图重新编码后超过 32 MiB 上限。",
                "image.thumbnail_output.too_large");
        }

        return OperationResult<EncodedImageSet>.Success(
            new EncodedImageSet(
                runtime,
                editor,
                card,
                orientationWasApplied));
    }

    private static SKBitmap ApplyOrientation(
        SKBitmap source,
        SKEncodedOrigin origin)
    {
        var swapsDimensions = origin is
            SKEncodedOrigin.LeftTop or
            SKEncodedOrigin.RightTop or
            SKEncodedOrigin.RightBottom or
            SKEncodedOrigin.LeftBottom;
        var width = swapsDimensions ? source.Height : source.Width;
        var height = swapsDimensions ? source.Width : source.Height;
        var destination = new SKBitmap(
            new SKImageInfo(
                width,
                height,
                SKColorType.Bgra8888,
                SKAlphaType.Premul));

        using var canvas = new SKCanvas(destination);
        canvas.Clear(SKColors.Transparent);
        canvas.SetMatrix(CreateOrientationMatrix(origin, source.Width, source.Height));
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(
            image,
            0,
            0,
            new SKSamplingOptions(
                SKFilterMode.Linear,
                SKMipmapMode.None));
        canvas.Flush();
        return destination;
    }

    private static SKMatrix CreateOrientationMatrix(
        SKEncodedOrigin origin,
        int width,
        int height) =>
        origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix
            {
                ScaleX = -1,
                ScaleY = 1,
                TransX = width,
                Persp2 = 1,
            },
            SKEncodedOrigin.BottomRight => new SKMatrix
            {
                ScaleX = -1,
                ScaleY = -1,
                TransX = width,
                TransY = height,
                Persp2 = 1,
            },
            SKEncodedOrigin.BottomLeft => new SKMatrix
            {
                ScaleX = 1,
                ScaleY = -1,
                TransY = height,
                Persp2 = 1,
            },
            SKEncodedOrigin.LeftTop => new SKMatrix
            {
                SkewX = 1,
                SkewY = 1,
                Persp2 = 1,
            },
            SKEncodedOrigin.RightTop => new SKMatrix
            {
                SkewX = -1,
                SkewY = 1,
                TransX = height,
                Persp2 = 1,
            },
            SKEncodedOrigin.RightBottom => new SKMatrix
            {
                SkewX = -1,
                SkewY = -1,
                TransX = height,
                TransY = width,
                Persp2 = 1,
            },
            SKEncodedOrigin.LeftBottom => new SKMatrix
            {
                SkewX = 1,
                SkewY = -1,
                TransY = width,
                Persp2 = 1,
            },
            _ => SKMatrix.Identity,
        };

    private static SKBitmap ResizeContained(
        SKBitmap source,
        int maximumWidth,
        int maximumHeight)
    {
        var scale = Math.Min(
            1d,
            Math.Min(
                (double)maximumWidth / source.Width,
                (double)maximumHeight / source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var resized = new SKBitmap(
            new SKImageInfo(
                width,
                height,
                SKColorType.Bgra8888,
                SKAlphaType.Premul));

        using var canvas = new SKCanvas(resized);
        canvas.Clear(SKColors.Transparent);
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(
            image,
            new SKRect(0, 0, width, height),
            new SKSamplingOptions(
                SKFilterMode.Linear,
                SKMipmapMode.Linear));
        canvas.Flush();
        return resized;
    }

    private static EncodedImage EncodeWebP(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(
            SKEncodedImageFormat.Webp,
            WebPQuality);
        if (encoded is null)
        {
            throw new InvalidOperationException("SkiaSharp WebP encoding failed.");
        }

        var bytes = encoded.ToArray();
        return new EncodedImage(
            bytes,
            bitmap.Width,
            bitmap.Height,
            Hash(bytes));
    }

    private async Task<OperationResult<ProcessedImageAsset>> CommitAsync(
        string stagedPath,
        string destinationRelativePath,
        EncodedImage encoded,
        ProcessedImageKind kind,
        CancellationToken cancellationToken)
    {
        var destination = pathResolver.Resolve(destinationRelativePath);
        if (!destination.IsSuccess)
        {
            return OperationResult<ProcessedImageAsset>.Failure(
                destination.Error!);
        }

        var destinationDirectory = Path.GetDirectoryName(destination.Value!);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            return OperationResult<ProcessedImageAsset>.Failure(
                OperationErrorCode.InvalidPath,
                "图片目标路径缺少父目录。",
                "image.destination.parent_missing");
        }

        Directory.CreateDirectory(destinationDirectory);
        var trustedDestination = pathResolver.Resolve(destinationRelativePath);
        if (!trustedDestination.IsSuccess)
        {
            return OperationResult<ProcessedImageAsset>.Failure(
                trustedDestination.Error!);
        }

        var existing = await TryReuseExistingAsync(
            trustedDestination.Value!,
            encoded.Sha256,
            retryWhenMissing: false,
            "image.cache.hash_conflict",
            cancellationToken);
        if (!existing.IsSuccess)
        {
            return OperationResult<ProcessedImageAsset>.Failure(existing.Error!);
        }

        var wasReused = existing.Value;
        if (!wasReused)
        {
            try
            {
                File.Move(stagedPath, trustedDestination.Value!);
            }
            catch (IOException)
            {
                existing = await TryReuseExistingAsync(
                    trustedDestination.Value!,
                    encoded.Sha256,
                    retryWhenMissing: true,
                    "image.cache.concurrent_conflict",
                    cancellationToken);
                if (!existing.IsSuccess)
                {
                    return OperationResult<ProcessedImageAsset>.Failure(existing.Error!);
                }

                if (!existing.Value)
                {
                    throw;
                }

                wasReused = existing.Value;
            }
        }

        return OperationResult<ProcessedImageAsset>.Success(
            new ProcessedImageAsset(
                kind,
                destinationRelativePath,
                WebPContentType,
                encoded.Bytes.LongLength,
                encoded.Width,
                encoded.Height,
                encoded.Sha256,
                wasReused));
    }

    private static async Task<OperationResult<bool>> TryReuseExistingAsync(
        string destinationPath,
        string expectedSha256,
        bool retryWhenMissing,
        string conflictDiagnosticCode,
        CancellationToken cancellationToken)
    {
        const int maximumAttempts = 5;

        for (var attempt = 0; attempt < maximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(destinationPath))
            {
                try
                {
                    var existingHash = await HashFileAsync(
                        destinationPath,
                        cancellationToken);
                    if (!string.Equals(
                            existingHash,
                            expectedSha256,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return OperationResult<bool>.Failure(
                            OperationErrorCode.Conflict,
                            "并发写入的图片缓存与内容指纹不一致。",
                            conflictDiagnosticCode);
                    }

                    return OperationResult<bool>.Success(true);
                }
                catch (IOException) when (attempt + 1 < maximumAttempts)
                {
                }
            }

            if (!retryWhenMissing || attempt + 1 >= maximumAttempts)
            {
                return OperationResult<bool>.Success(false);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }

        return OperationResult<bool>.Success(false);
    }

    private static ImageSourceFormat? DetectFormat(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 &&
            bytes[..8].SequenceEqual(
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return ImageSourceFormat.Png;
        }

        if (bytes.Length >= 3 &&
            bytes[0] == 0xFF &&
            bytes[1] == 0xD8 &&
            bytes[2] == 0xFF)
        {
            return ImageSourceFormat.Jpeg;
        }

        if (bytes.Length >= 12 &&
            bytes[..4].SequenceEqual("RIFF"u8) &&
            bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return ImageSourceFormat.WebP;
        }

        return null;
    }

    private static bool MatchesCodecFormat(
        ImageSourceFormat detectedFormat,
        SKEncodedImageFormat codecFormat) =>
        detectedFormat switch
        {
            ImageSourceFormat.Png => codecFormat is SKEncodedImageFormat.Png,
            ImageSourceFormat.Jpeg => codecFormat is SKEncodedImageFormat.Jpeg,
            ImageSourceFormat.WebP => codecFormat is SKEncodedImageFormat.Webp,
            _ => false,
        };

    private static ImageAspectCategory ClassifyAspect(int width, int height)
    {
        var ratio = (double)width / height;
        return ratio switch
        {
            >= 2.1 => ImageAspectCategory.UltraWide,
            >= 1.6 => ImageAspectCategory.Widescreen,
            > 1.1 => ImageAspectCategory.Landscape,
            >= 0.9 => ImageAspectCategory.Square,
            _ => ImageAspectCategory.Portrait,
        };
    }

    private static ImageTaskModeSuggestion SuggestTaskMode(int width, int height) =>
        ClassifyAspect(width, height) switch
        {
            ImageAspectCategory.UltraWide or ImageAspectCategory.Widescreen =>
                ImageTaskModeSuggestion.Banner,
            ImageAspectCategory.Portrait => ImageTaskModeSuggestion.Off,
            _ => ImageTaskModeSuggestion.Ambient,
        };

    private static async Task<string> HashFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool IsDiskFull(IOException exception)
    {
        var errorCode = exception.HResult & 0xFFFF;
        return errorCode is 39 or 112;
    }

    private static void CleanupStagingDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static OperationResult<ProcessedImageSet> Failure(
        OperationErrorCode code,
        string userMessage,
        string diagnosticCode) =>
        OperationResult<ProcessedImageSet>.Failure(
            code,
            userMessage,
            diagnosticCode);

    private sealed record EncodedImage(
        byte[] Bytes,
        int Width,
        int Height,
        string Sha256);

    private sealed record EncodedImageSet(
        EncodedImage Runtime,
        EncodedImage Editor,
        EncodedImage Card,
        bool OrientationWasApplied);
}
