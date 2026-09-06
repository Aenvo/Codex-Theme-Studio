using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace CodexThemeStudio.Desktop.MacOS.Views;

public sealed class CssColorBrushConverter : IValueConverter
{
    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        if (value is string color &&
            CssColor.TryParse(color, out var parsed))
        {
            return new SolidColorBrush(parsed);
        }

        return Brushes.Transparent;
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}
