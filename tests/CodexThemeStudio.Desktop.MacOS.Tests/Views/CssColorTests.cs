using Avalonia.Media;
using CodexThemeStudio.Desktop.MacOS.Views;

namespace CodexThemeStudio.Desktop.MacOS.Tests.Views;

public sealed class CssColorTests
{
    [Theory]
    [InlineData("#112233", 255, 17, 34, 51)]
    [InlineData("#11223380", 128, 17, 34, 51)]
    [InlineData("30303080", 128, 48, 48, 48)]
    public void CssRgbaHexUsesAlphaAsFinalChannel(
        string value,
        byte alpha,
        byte red,
        byte green,
        byte blue)
    {
        Assert.True(CssColor.TryParse(value, out var parsed));

        Assert.Equal(Color.FromArgb(alpha, red, green, blue), parsed);
    }

    [Theory]
    [InlineData(255, 17, 34, 51, "#112233")]
    [InlineData(128, 17, 34, 51, "#11223380")]
    public void CssRgbaHexFormatsWithoutChangingChannelOrder(
        byte alpha,
        byte red,
        byte green,
        byte blue,
        string expected)
    {
        var formatted = CssColor.FormatCompact(
            Color.FromArgb(alpha, red, green, blue));

        Assert.Equal(expected, formatted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("url(https://example.test)")]
    [InlineData("rgba(1,2,3,1)")]
    public void ColorParserRejectsUnknownOrDangerousFormats(string value) =>
        Assert.False(CssColor.TryParse(value, out _));
}
