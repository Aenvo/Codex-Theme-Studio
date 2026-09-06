using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CodexThemeStudio.Desktop.MacOS.ViewModels;

namespace CodexThemeStudio.Desktop.MacOS.Views;

public sealed class EditorArtStretchConverter : IValueConverter
{
    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) => value switch
        {
            ThemeEditorViewModel.ArtContain => Stretch.Uniform,
            _ => Stretch.UniformToFill,
        };

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class EditorPreviewScaleConverter : IValueConverter
{
    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) => value is double scale ? scale : 1d;

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class EditorFocusPointConverter : IMultiValueConverter
{
    public object Convert(
        IList<object?> values,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        if (values.Count < 2 ||
            values[0] is not double x ||
            values[1] is not double y)
        {
            return new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        }

        return new RelativePoint(
            Math.Clamp(x, 0, 1),
            Math.Clamp(y, 0, 1),
            RelativeUnit.Relative);
    }
}
