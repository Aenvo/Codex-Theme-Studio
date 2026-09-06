using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace CodexThemeStudio.Desktop.MacOS.Views;

public partial class EditorColorWindow : Window
{
    private static readonly Regex CssRgbaPattern = new(
        "^\\s*rgba\\(\\s*(?<r>\\d{1,3})\\s*,\\s*(?<g>\\d{1,3})\\s*,\\s*(?<b>\\d{1,3})\\s*,\\s*(?<a>(?:0|1)(?:\\.\\d+)?|\\.\\d+)\\s*\\)\\s*$",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnoreCase);

    private readonly EditorColorDialogViewModel viewModel;
    private bool synchronizingInputs;
    private bool draggingSaturationValue;

    public EditorColorWindow()
        : this("#000000", null, null)
    {
    }

    public EditorColorWindow(
        string value,
        IEnumerable<string>? palette = null,
        IEnumerable<string>? history = null)
    {
        InitializeComponent();
        viewModel = new EditorColorDialogViewModel(value, palette, history);
        DataContext = viewModel;
        viewModel.ColorChanged += OnColorChanged;
        Loaded += (_, _) =>
        {
            SynchronizeInputs();
            RefreshSurface();
        };
        SaturationValueSurface.SizeChanged += (_, _) => UpdateMarker();
    }

    private void OnColorChanged(object? sender, EventArgs args)
    {
        SynchronizeInputs();
        RefreshSurface();
    }

    private void FormatSelector_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs args)
    {
        if (HexInputs is null || RgbInputs is null || CssInput is null)
        {
            return;
        }

        HexInputs.IsVisible = FormatSelector.SelectedIndex == 0;
        RgbInputs.IsVisible = FormatSelector.SelectedIndex == 1;
        CssInput.IsVisible = FormatSelector.SelectedIndex == 2;
        SynchronizeInputs();
    }

    private void HexInput_OnTextChanged(
        object? sender,
        TextChangedEventArgs args)
    {
        if (synchronizingInputs ||
            !double.TryParse(
                HexOpacityInput.Text,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var opacity))
        {
            return;
        }

        var hex = HexInput.Text?.Trim() ?? string.Empty;
        if (hex.Length != 7 || !hex.StartsWith('#'))
        {
            return;
        }

        var alpha = (byte)Math.Round(
            Math.Clamp(opacity, 0, 100) / 100d * 255d,
            MidpointRounding.AwayFromZero);
        if (CssColor.TryParse($"{hex}{alpha:X2}", out var color))
        {
            viewModel.SetColor(color);
        }
    }

    private void RgbInput_OnTextChanged(
        object? sender,
        TextChangedEventArgs args)
    {
        if (synchronizingInputs ||
            !byte.TryParse(RedInput.Text, out var red) ||
            !byte.TryParse(GreenInput.Text, out var green) ||
            !byte.TryParse(BlueInput.Text, out var blue) ||
            !double.TryParse(
                AlphaPercentInput.Text,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var opacity))
        {
            return;
        }

        viewModel.SetColor(Color.FromArgb(
            (byte)Math.Round(
                Math.Clamp(opacity, 0, 100) / 100d * 255d,
                MidpointRounding.AwayFromZero),
            red,
            green,
            blue));
    }

    private void CssInput_OnTextChanged(
        object? sender,
        TextChangedEventArgs args)
    {
        if (synchronizingInputs ||
            !TryParseCss(CssInput.Text ?? string.Empty, out var color))
        {
            return;
        }

        viewModel.SetColor(color);
    }

    private void ColorSwatch_OnClick(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: string value } &&
            CssColor.TryParse(value, out var color))
        {
            viewModel.SetColor(color);
        }
    }

    private void SaturationValue_OnPointerPressed(
        object? sender,
        PointerPressedEventArgs args)
    {
        draggingSaturationValue = true;
        args.Pointer.Capture(SaturationValueSurface);
        SetSaturationValue(args.GetPosition(SaturationValueSurface));
        args.Handled = true;
    }

    private void SaturationValue_OnPointerMoved(
        object? sender,
        PointerEventArgs args)
    {
        if (!draggingSaturationValue ||
            !args.GetCurrentPoint(SaturationValueSurface)
                .Properties.IsLeftButtonPressed)
        {
            return;
        }

        SetSaturationValue(args.GetPosition(SaturationValueSurface));
        args.Handled = true;
    }

    private void SaturationValue_OnPointerReleased(
        object? sender,
        PointerReleasedEventArgs args)
    {
        if (!draggingSaturationValue)
        {
            return;
        }

        SetSaturationValue(args.GetPosition(SaturationValueSurface));
        draggingSaturationValue = false;
        args.Pointer.Capture(null);
        args.Handled = true;
    }

    private void SetSaturationValue(Avalonia.Point point)
    {
        var width = Math.Max(1, SaturationValueSurface.Bounds.Width);
        var height = Math.Max(1, SaturationValueSurface.Bounds.Height);
        viewModel.SetSaturationValue(
            Math.Clamp(point.X / width, 0, 1),
            1 - Math.Clamp(point.Y / height, 0, 1));
    }

    private void SynchronizeInputs()
    {
        if (HexInput is null)
        {
            return;
        }

        synchronizingInputs = true;
        try
        {
            var color = viewModel.CurrentColor;
            HexInput.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            HexOpacityInput.Text = viewModel.AlphaPercent.ToString(
                "0",
                CultureInfo.InvariantCulture);
            RedInput.Text = color.R.ToString(CultureInfo.InvariantCulture);
            GreenInput.Text = color.G.ToString(CultureInfo.InvariantCulture);
            BlueInput.Text = color.B.ToString(CultureInfo.InvariantCulture);
            AlphaPercentInput.Text = viewModel.AlphaPercent.ToString(
                "0",
                CultureInfo.InvariantCulture);
            CssInput.Text =
                $"rgba({color.R}, {color.G}, {color.B}, " +
                $"{(color.A / 255d).ToString("0.###", CultureInfo.InvariantCulture)})";
        }
        finally
        {
            synchronizingInputs = false;
        }
    }

    private void RefreshSurface()
    {
        if (HueSurface is null)
        {
            return;
        }

        HueSurface.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Colors.White, 0),
                new GradientStop(
                    FromHsv(viewModel.Hue, 1, 1, byte.MaxValue),
                    1),
            ],
        };
        UpdateMarker();
    }

    private void UpdateMarker()
    {
        if (SelectionMarker is null || SaturationValueSurface is null)
        {
            return;
        }

        Canvas.SetLeft(
            SelectionMarker,
            viewModel.Saturation * SaturationValueSurface.Bounds.Width - 8);
        Canvas.SetTop(
            SelectionMarker,
            (1 - viewModel.Brightness) *
            SaturationValueSurface.Bounds.Height - 8);
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs args) =>
        Close(null);

    private void Apply_OnClick(object? sender, RoutedEventArgs args) =>
        Close(viewModel.NormalizedValue);

    private static bool TryParseCss(string value, out Color color)
    {
        if (CssColor.TryParse(value, out color))
        {
            return true;
        }

        var match = CssRgbaPattern.Match(value);
        if (!match.Success ||
            !byte.TryParse(match.Groups["r"].Value, out var red) ||
            !byte.TryParse(match.Groups["g"].Value, out var green) ||
            !byte.TryParse(match.Groups["b"].Value, out var blue) ||
            !double.TryParse(
                match.Groups["a"].Value,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var opacity) ||
            opacity is < 0 or > 1)
        {
            color = Colors.Transparent;
            return false;
        }

        color = Color.FromArgb(
            (byte)Math.Round(opacity * 255, MidpointRounding.AwayFromZero),
            red,
            green,
            blue);
        return true;
    }

    private static void ToHsv(
        Color color,
        out double hue,
        out double saturation,
        out double brightness)
    {
        var red = color.R / 255d;
        var green = color.G / 255d;
        var blue = color.B / 255d;
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var delta = max - min;
        hue = delta == 0 ? 0 : max == red
            ? 60 * (((green - blue) / delta) % 6)
            : max == green
                ? 60 * (((blue - red) / delta) + 2)
                : 60 * (((red - green) / delta) + 4);
        if (hue < 0)
        {
            hue += 360;
        }

        saturation = max == 0 ? 0 : delta / max;
        brightness = max;
    }

    private static Color FromHsv(
        double hue,
        double saturation,
        double brightness,
        byte alpha)
    {
        var chroma = brightness * saturation;
        var x = chroma * (1 - Math.Abs(((hue / 60) % 2) - 1));
        var m = brightness - chroma;
        var (red, green, blue) = hue switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x),
        };
        return Color.FromArgb(
            alpha,
            (byte)Math.Round((red + m) * 255),
            (byte)Math.Round((green + m) * 255),
            (byte)Math.Round((blue + m) * 255));
    }

    private sealed class EditorColorDialogViewModel : INotifyPropertyChanged
    {
        private double hue;
        private double saturation;
        private double brightness;
        private byte alpha;

        public EditorColorDialogViewModel(
            string value,
            IEnumerable<string>? palette,
            IEnumerable<string>? history)
        {
            if (!CssColor.TryParse(value, out var color))
            {
                color = Colors.Black;
            }

            alpha = color.A;
            ToHsv(color, out hue, out saturation, out brightness);
            ColorOptions = (palette ?? [])
                .Concat(history ?? [])
                .Append(CssColor.FormatCompact(color))
                .Where(item => CssColor.TryParse(item, out _))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToArray();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public event EventHandler? ColorChanged;

        public IReadOnlyList<string> ColorOptions { get; }

        public double Hue
        {
            get => hue;
            set
            {
                var normalized = Math.Clamp(value, 0, 360);
                if (Math.Abs(hue - normalized) < 0.001)
                {
                    return;
                }

                hue = normalized;
                NotifyColorChanged();
            }
        }

        public double SaturationPercent
        {
            get => saturation * 100;
            set
            {
                var normalized = Math.Clamp(value, 0, 100) / 100d;
                if (Math.Abs(saturation - normalized) < 0.0001)
                {
                    return;
                }

                saturation = normalized;
                NotifyColorChanged();
            }
        }

        public double AlphaPercent
        {
            get => alpha / 255d * 100d;
            set
            {
                var normalized = (byte)Math.Round(
                    Math.Clamp(value, 0, 100) / 100d * 255d,
                    MidpointRounding.AwayFromZero);
                if (alpha == normalized)
                {
                    return;
                }

                alpha = normalized;
                NotifyColorChanged();
            }
        }

        public double Saturation => saturation;

        public double Brightness => brightness;

        public Color CurrentColor =>
            FromHsv(hue, saturation, brightness, alpha);

        public IBrush PreviewBrush => new SolidColorBrush(CurrentColor);

        public string NormalizedValue =>
            CssColor.FormatCompact(CurrentColor);

        public void SetColor(Color color)
        {
            alpha = color.A;
            ToHsv(color, out hue, out saturation, out brightness);
            NotifyColorChanged();
        }

        public void SetSaturationValue(
            double newSaturation,
            double newBrightness)
        {
            saturation = Math.Clamp(newSaturation, 0, 1);
            brightness = Math.Clamp(newBrightness, 0, 1);
            NotifyColorChanged();
        }

        private void NotifyColorChanged()
        {
            OnPropertyChanged(nameof(Hue));
            OnPropertyChanged(nameof(SaturationPercent));
            OnPropertyChanged(nameof(AlphaPercent));
            OnPropertyChanged(nameof(PreviewBrush));
            OnPropertyChanged(nameof(NormalizedValue));
            ColorChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnPropertyChanged(
            [CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
    }
}
