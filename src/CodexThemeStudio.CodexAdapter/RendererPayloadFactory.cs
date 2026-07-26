using System.Text.Json;
using System.Text.Json.Serialization;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;
using CodexThemeStudio.ThemeCore;

namespace CodexThemeStudio.CodexAdapter;

public static class RendererPayloadFactory
{
    public const int MaximumImageBytes =
        ImageSizeLimits.MaximumManagedImageBytes;

    public static OperationResult<byte[]> Create(
        ThemePackage theme,
        ReadOnlySpan<byte> image)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var issues = ThemePackageContractValidator.Validate(theme);
        if (issues.Count > 0)
        {
            return OperationResult<byte[]>.Failure(
                OperationErrorCode.ValidationFailed,
                issues[0].UserMessage,
                issues[0].Code);
        }

        if (image.Length is <= 0 or > MaximumImageBytes)
        {
            return OperationResult<byte[]>.Failure(
                OperationErrorCode.ValidationFailed,
                "主题图片大小无效。",
                "runtime.image_size_invalid");
        }

        var contentType = DetectContentType(image);
        if (contentType is null)
        {
            return OperationResult<byte[]>.Failure(
                OperationErrorCode.ValidationFailed,
                "主题图片内容不是受支持的 PNG、JPEG 或 WebP。",
                "runtime.image_signature_invalid");
        }

        return OperationResult<byte[]>.Success(
            JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    theme,
                    image = new
                    {
                        contentType,
                        base64 = Convert.ToBase64String(image),
                    },
                },
                JsonOptions));
    }

    private static string? DetectContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 &&
            bytes[..8].SequenceEqual(
                new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }))
        {
            return "image/png";
        }

        if (bytes.Length >= 4 &&
            bytes[0] == 0xff &&
            bytes[1] == 0xd8 &&
            bytes[^2] == 0xff &&
            bytes[^1] == 0xd9)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 12 &&
            bytes[..4].SequenceEqual("RIFF"u8) &&
            bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
