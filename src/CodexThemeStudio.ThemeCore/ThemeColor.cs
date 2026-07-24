namespace CodexThemeStudio.ThemeCore;

public readonly record struct RgbaColor(byte R, byte G, byte B, byte A = byte.MaxValue);

public static class ThemeColor
{
    public static bool TryNormalize(string? value, out string normalized)
    {
        if (!TryParse(value, out var color))
        {
            normalized = string.Empty;
            return false;
        }

        normalized = Format(color);
        return true;
    }

    public static bool TryParse(string? value, out RgbaColor color)
    {
        color = default;
        if (value is null || (value.Length != 7 && value.Length != 9) || value[0] != '#')
        {
            return false;
        }

        var alpha = byte.MaxValue;
        return TryByte(value.AsSpan(1, 2), out var red) &&
               TryByte(value.AsSpan(3, 2), out var green) &&
               TryByte(value.AsSpan(5, 2), out var blue) &&
               (value.Length == 7 || TryByte(value.AsSpan(7, 2), out alpha)) &&
               Assign(red, green, blue, alpha, out color);
    }

    public static string Format(RgbaColor color) =>
        color.A == byte.MaxValue
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";

    public static bool TryParseRgba(string? value, out RgbaColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value) ||
            !value.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) ||
            !value.EndsWith(')'))
        {
            return false;
        }

        var parts = value[5..^1].Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4 ||
            !byte.TryParse(parts[0], out var red) ||
            !byte.TryParse(parts[1], out var green) ||
            !byte.TryParse(parts[2], out var blue) ||
            !double.TryParse(
                parts[3],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var alpha) ||
            !double.IsFinite(alpha) || alpha < 0 || alpha > 1)
        {
            return false;
        }

        color = new RgbaColor(
            red,
            green,
            blue,
            (byte)Math.Round(alpha * byte.MaxValue, MidpointRounding.AwayFromZero));
        return true;
    }

    public static RgbaColor Composite(RgbaColor foreground, RgbaColor background)
    {
        var foregroundAlpha = foreground.A / 255d;
        var backgroundAlpha = background.A / 255d;
        var outputAlpha = foregroundAlpha + (backgroundAlpha * (1 - foregroundAlpha));
        if (outputAlpha <= 0)
        {
            return new RgbaColor(0, 0, 0, 0);
        }

        byte Blend(byte foregroundChannel, byte backgroundChannel) =>
            (byte)Math.Round(
                ((foregroundChannel * foregroundAlpha) +
                 (backgroundChannel * backgroundAlpha * (1 - foregroundAlpha))) /
                outputAlpha,
                MidpointRounding.AwayFromZero);

        return new RgbaColor(
            Blend(foreground.R, background.R),
            Blend(foreground.G, background.G),
            Blend(foreground.B, background.B),
            (byte)Math.Round(outputAlpha * 255, MidpointRounding.AwayFromZero));
    }

    private static bool TryByte(ReadOnlySpan<char> value, out byte parsed) =>
        byte.TryParse(
            value,
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture,
            out parsed);

    private static bool Assign(
        byte red,
        byte green,
        byte blue,
        byte alpha,
        out RgbaColor color)
    {
        color = new RgbaColor(red, green, blue, alpha);
        return true;
    }
}
