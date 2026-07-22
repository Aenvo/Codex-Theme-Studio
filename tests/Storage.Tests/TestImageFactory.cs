using System.Buffers.Binary;
using SkiaSharp;

namespace CodexThemeStudio.Storage.Tests;

internal static class TestImageFactory
{
    public static byte[] Create(
        SKEncodedImageFormat format,
        int width = 640,
        int height = 360)
    {
        using var bitmap = new SKBitmap(
            new SKImageInfo(
                width,
                height,
                SKColorType.Bgra8888,
                SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(32, 40, 64));
        using var paint = new SKPaint
        {
            Color = new SKColor(190, 110, 220),
            IsAntialias = false,
        };
        canvas.DrawRect(
            new SKRect(0, 0, width / 2f, height),
            paint);
        canvas.Flush();

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 92);
        return encoded.ToArray();
    }

    public static byte[] WithExifOrientationAndIcc(
        byte[] jpeg,
        ushort orientation)
    {
        Assert.True(
            jpeg.Length > 2 &&
            jpeg[0] == 0xFF &&
            jpeg[1] == 0xD8);

        var exifPayload = CreateExifPayload(orientation);
        var iccPayload = "ICC_PROFILE\0test-profile"u8.ToArray();
        var app1 = CreateJpegSegment(0xE1, exifPayload);
        var app2 = CreateJpegSegment(0xE2, iccPayload);

        var result = new byte[jpeg.Length + app1.Length + app2.Length];
        jpeg.AsSpan(0, 2).CopyTo(result);
        app1.CopyTo(result.AsSpan(2));
        app2.CopyTo(result.AsSpan(2 + app1.Length));
        jpeg.AsSpan(2).CopyTo(result.AsSpan(2 + app1.Length + app2.Length));
        return result;
    }

    public static byte[] PatchPngDimensions(
        byte[] png,
        int width,
        int height)
    {
        var patched = png.ToArray();
        BinaryPrimitives.WriteInt32BigEndian(
            patched.AsSpan(16, 4),
            width);
        BinaryPrimitives.WriteInt32BigEndian(
            patched.AsSpan(20, 4),
            height);

        var crc = ComputeCrc32(patched.AsSpan(12, 17));
        BinaryPrimitives.WriteUInt32BigEndian(
            patched.AsSpan(29, 4),
            crc);
        return patched;
    }

    private static byte[] CreateExifPayload(ushort orientation)
    {
        var payload = new byte[50];
        "Exif\0\0"u8.CopyTo(payload);
        var tiff = payload.AsSpan(6);

        tiff[0] = (byte)'I';
        tiff[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(tiff[2..4], 42);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff[4..8], 8);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff[8..10], 2);

        BinaryPrimitives.WriteUInt16LittleEndian(tiff[10..12], 0x0112);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff[12..14], 3);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff[14..18], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff[18..20], orientation);

        BinaryPrimitives.WriteUInt16LittleEndian(tiff[22..24], 0x8825);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff[24..26], 4);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff[26..30], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff[30..34], 38);

        BinaryPrimitives.WriteUInt32LittleEndian(tiff[34..38], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff[38..40], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff[40..44], 0);
        return payload;
    }

    private static byte[] CreateJpegSegment(
        byte marker,
        ReadOnlySpan<byte> payload)
    {
        var segment = new byte[payload.Length + 4];
        segment[0] = 0xFF;
        segment[1] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(
            segment.AsSpan(2, 2),
            checked((ushort)(payload.Length + 2)));
        payload.CopyTo(segment.AsSpan(4));
        return segment;
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 1
                    ? (crc >> 1) ^ 0xEDB88320
                    : crc >> 1;
            }
        }

        return ~crc;
    }
}
