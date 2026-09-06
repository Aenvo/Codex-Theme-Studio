using Avalonia.Media;

namespace CodexThemeStudio.Desktop.MacOS.Views;

public static class CssColor
{
    public static bool TryParse(string? value, out Color color)
    {
        color = Colors.Transparent;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (text.StartsWith('#'))
        {
            text = text[1..];
        }

        if (text.Length is not (6 or 8) ||
            !uint.TryParse(
                text,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var packed))
        {
            return false;
        }

        var hasAlpha = text.Length == 8;
        var red = (byte)(packed >> (hasAlpha ? 24 : 16));
        var green = (byte)(packed >> (hasAlpha ? 16 : 8));
        var blue = (byte)(packed >> (hasAlpha ? 8 : 0));
        var alpha = hasAlpha ? (byte)packed : byte.MaxValue;
        color = Color.FromArgb(alpha, red, green, blue);
        return true;
    }

    public static string Format(Color color, bool includeAlpha = true) =>
        includeAlpha || color.A != byte.MaxValue
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}"
            : $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static string FormatCompact(Color color) =>
        color.A == byte.MaxValue
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : Format(color);
}
