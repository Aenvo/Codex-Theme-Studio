using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.ThemeCore;

namespace CodexThemeStudio.ThemeCore.Tests;

public sealed class ThemeColorTests
{
    [Theory]
    [InlineData("#112233", 0x11, 0x22, 0x33, 0xFF, "#112233")]
    [InlineData("#11223344", 0x11, 0x22, 0x33, 0x44, "#11223344")]
    [InlineData("#aabbccff", 0xAA, 0xBB, 0xCC, 0xFF, "#AABBCC")]
    public void ParseAndFormat_UsesCssRgbaOrder(
        string input,
        byte red,
        byte green,
        byte blue,
        byte alpha,
        string expected)
    {
        Assert.True(ThemeColor.TryParse(input, out var color));
        Assert.Equal(new RgbaColor(red, green, blue, alpha), color);
        Assert.Equal(expected, ThemeColor.Format(color));
    }

    [Fact]
    public void ParseRgba_ConvertsDoroLineToEightDigitHex()
    {
        Assert.True(ThemeColor.TryParseRgba("rgba(185,138,233,.20)", out var color));
        Assert.Equal("#B98AE933", ThemeColor.Format(color));
    }

    [Fact]
    public void Contrast_CompositesTransparentPanelAndText()
    {
        var result = ThemeContrast.Assess(
            new ThemePalette(
                "#000000",
                "#FFFFFF80",
                "#3B82F6",
                "#FFFFFFFF",
                "#FFFFFF99",
                "#B98AE933"),
            ThemeVariant.Dark);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.TextRatio > 1);
        Assert.True(result.Value.MutedTextRatio > 1);
    }
}
