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
    public static OperationResult<ContrastAssessment> Assess(
        ThemePalette palette,
        ThemeVariant variant = ThemeVariant.Auto)
    {
        if (!ThemeColor.TryParse(palette.Background, out var background) ||
            !ThemeColor.TryParse(palette.Panel, out var panel) ||
            !ThemeColor.TryParse(palette.Text, out var text) ||
            !ThemeColor.TryParse(palette.Muted, out var muted))
        {
            return OperationResult<ContrastAssessment>.Failure(
                OperationErrorCode.ValidationFailed,
                "颜色必须使用 #RRGGBB 或 #RRGGBBAA 格式。",
                "theme.contrast.color_invalid");
        }

        var bases = variant switch
        {
            ThemeVariant.Dark => new[] { new RgbaColor(0, 0, 0) },
            ThemeVariant.Light => new[] { new RgbaColor(255, 255, 255) },
            _ => new[] { new RgbaColor(0, 0, 0), new RgbaColor(255, 255, 255) },
        };
        var ratios = bases.Select(baseColor =>
        {
            var opaqueBackground = ThemeColor.Composite(background, baseColor);
            var opaquePanel = ThemeColor.Composite(panel, opaqueBackground);
            return (
                Text: Ratio(opaquePanel, ThemeColor.Composite(text, opaquePanel)),
                Muted: Ratio(opaquePanel, ThemeColor.Composite(muted, opaquePanel)));
        }).ToArray();
        var textRatio = ratios.Min(value => value.Text);
        var mutedRatio = ratios.Min(value => value.Muted);
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

    private static double Ratio(RgbaColor first, RgbaColor second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(RgbaColor color)
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
