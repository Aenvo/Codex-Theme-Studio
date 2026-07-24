using System.Globalization;
using System.Windows.Media;
using CodexThemeStudio.Desktop.Converters;

namespace CodexThemeStudio.Desktop.Tests;

public sealed class CssColorBrushConverterTests
{
    [Fact]
    public void Convert_UsesCssRgbaChannelOrder()
    {
        var converter = new CssColorBrushConverter();
        var brush = Assert.IsType<SolidColorBrush>(converter.Convert(
            "#11223344",
            typeof(Brush),
            null,
            CultureInfo.InvariantCulture));

        Assert.Equal(0x44, brush.Color.A);
        Assert.Equal(0x11, brush.Color.R);
        Assert.Equal(0x22, brush.Color.G);
        Assert.Equal(0x33, brush.Color.B);
    }
}
