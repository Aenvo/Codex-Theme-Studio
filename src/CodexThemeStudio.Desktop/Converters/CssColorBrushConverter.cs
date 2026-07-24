using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using CodexThemeStudio.ThemeCore;

namespace CodexThemeStudio.Desktop.Converters;

public sealed class CssColorBrushConverter : IValueConverter
{
    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        value is string text && ThemeColor.TryParse(text, out var color)
            ? new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B))
            : Brushes.Transparent;

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        Binding.DoNothing;
}
