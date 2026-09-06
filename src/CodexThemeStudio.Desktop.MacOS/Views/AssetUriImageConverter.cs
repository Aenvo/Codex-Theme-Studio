using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CodexThemeStudio.Desktop.MacOS.Views;

public sealed class AssetUriImageConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, IImage>
        Images = new(StringComparer.Ordinal);

    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        if (value is not Uri assetUri || !assetUri.IsAbsoluteUri)
        {
            return null;
        }

        if (assetUri.IsFile)
        {
            var path = assetUri.LocalPath;
            if (!File.Exists(path))
            {
                return null;
            }

            return TryLoad(
                path,
                () => new Bitmap(path));
        }

        if (assetUri.Scheme != "avares")
        {
            return null;
        }

        return TryLoad(
            assetUri.AbsoluteUri,
            () =>
            {
                using var stream = AssetLoader.Open(assetUri);
                return new Bitmap(stream);
            });
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();

    private static IImage? TryLoad(string key, Func<IImage> loader)
    {
        try
        {
            return Images.GetOrAdd(key, _ => loader());
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            InvalidOperationException)
        {
            return null;
        }
    }
}
