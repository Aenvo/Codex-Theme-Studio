using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.ThemeCore;

public sealed class ThemeDraft
{
    public ThemeDraft(ThemePackage source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Original = source;
        Id = source.Id;
        Name = source.Name;
        Variant = source.Variant;
        Palette = source.Palette;
        Art = source.Art;
    }

    public ThemePackage Original { get; }

    public Guid Id { get; set; }

    public string Name { get; set; }

    public ThemeVariant Variant { get; set; }

    public ThemePalette Palette { get; set; }

    public ThemeArt Art { get; set; }

    public ThemePackage Build() =>
        new(ThemePackageContractValidator.CurrentSchemaVersion, Id, Name, Variant, Palette, Art);

    public void Reset()
    {
        Id = Original.Id;
        Name = Original.Name;
        Variant = Original.Variant;
        Palette = Original.Palette;
        Art = Original.Art;
    }

    public ThemePackage BuildCopy(Guid id, string name) =>
        Build() with { Id = id, Name = name };
}

public sealed record ContrastAssessment(
    double TextRatio,
    double MutedTextRatio,
    bool HasWarning,
    string UserMessage);

public static class ThemeContrast
{
    public static OperationResult<ContrastAssessment> Assess(ThemePalette palette)
    {
        var panel = Parse(palette.Panel);
        var text = Parse(palette.Text);
        var muted = Parse(palette.Muted);
        if (panel is null || text is null || muted is null)
        {
            return OperationResult<ContrastAssessment>.Failure(
                OperationErrorCode.ValidationFailed,
                "颜色必须使用 #RRGGBB 或 #RRGGBBAA 格式。",
                "theme.contrast.color_invalid");
        }

        var textRatio = Ratio(panel.Value, text.Value);
        var mutedRatio = Ratio(panel.Value, muted.Value);
        var warning = textRatio < 4.5 || mutedRatio < 3;
        return OperationResult<ContrastAssessment>.Success(
            new ContrastAssessment(
                textRatio,
                mutedRatio,
                warning,
                warning
                    ? "部分文字与面板对比不足；正文建议至少 4.5:1，辅助文字至少 3:1。"
                    : "文字对比度达到常用可读性建议。"));
    }

    private static (byte R, byte G, byte B)? Parse(string value)
    {
        if (value.Length is not (7 or 9) || value[0] != '#')
        {
            return null;
        }

        return byte.TryParse(value.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
               byte.TryParse(value.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
               byte.TryParse(value.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var b)
            ? (r, g, b)
            : null;
    }

    private static double Ratio((byte R, byte G, byte B) first, (byte R, byte G, byte B) second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance((byte R, byte G, byte B) color)
    {
        static double Channel(byte value)
        {
            var normalized = value / 255d;
            return normalized <= 0.04045
                ? normalized / 12.92
                : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) +
               (0.7152 * Channel(color.G)) +
               (0.0722 * Channel(color.B));
    }
}
