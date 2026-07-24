using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CodexThemeStudio.ThemeCore;

namespace CodexThemeStudio.Desktop.Controls;

public partial class ColorField : UserControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(ColorField), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(ColorField),
        new FrameworkPropertyMetadata(
            "#000000",
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnValueChanged));

    public static readonly DependencyProperty PaletteProperty = DependencyProperty.Register(
        nameof(Palette), typeof(IEnumerable), typeof(ColorField), new PropertyMetadata(null));

    private string originalValue = "#000000";
    private double hue;
    private double saturation;
    private double brightness;
    private byte alpha = byte.MaxValue;
    private bool synchronizing;
    private bool committed;
    private bool draggingSaturationValue;

    public ColorField()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshFromValue(Value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IEnumerable? Palette
    {
        get => (IEnumerable?)GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    private static void OnValueChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is ColorField field && !field.synchronizing && args.NewValue is string value)
        {
            field.RefreshFromValue(value);
        }
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        originalValue = ThemeColor.TryNormalize(Value, out var normalized) ? normalized : "#000000";
        committed = false;
        RefreshFromValue(originalValue);
        PickerPopup.IsOpen = true;
    }

    private void OnPopupOpened(object? sender, EventArgs e)
    {
        ThemePalette.ItemsSource = Palette ?? DefaultPalette;
        HexInput.Focus();
        HexInput.SelectAll();
        UpdateMarker();
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        if (!committed)
        {
            SetWorkingValue(originalValue);
        }
    }

    private void OnPopupPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Apply();
            e.Handled = true;
        }
    }

    private void OnSaturationValueMouse(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        draggingSaturationValue = true;
        SaturationValueSurface.CaptureMouse();
        var point = e.GetPosition(SaturationValueSurface);
        saturation = Math.Clamp(point.X / Math.Max(1, SaturationValueSurface.ActualWidth), 0, 1);
        brightness = 1 - Math.Clamp(point.Y / Math.Max(1, SaturationValueSurface.ActualHeight), 0, 1);
        UpdateFromHsv();
    }

    private void OnSaturationValueMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (draggingSaturationValue)
        {
            draggingSaturationValue = false;
            SaturationValueSurface.ReleaseMouseCapture();
        }
    }

    private void OnHueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (synchronizing)
        {
            return;
        }

        hue = e.NewValue;
        UpdateFromHsv();
    }

    private void OnAlphaChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (synchronizing)
        {
            return;
        }

        alpha = (byte)Math.Round(e.NewValue, MidpointRounding.AwayFromZero);
        UpdateFromHsv();
    }

    private void OnHexTextChanged(object sender, TextChangedEventArgs e)
    {
        if (synchronizing || !ThemeColor.TryNormalize(HexInput.Text, out var normalized))
        {
            return;
        }

        SetWorkingValue(normalized);
        RefreshFromValue(normalized, updateHex: false);
    }

    private void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value } && ThemeColor.TryNormalize(value, out var normalized))
        {
            SetWorkingValue(normalized);
            RefreshFromValue(normalized);
        }
    }

    private void OnApplyClick(object sender, RoutedEventArgs e) => Apply();

    private void OnCancelClick(object sender, RoutedEventArgs e) => Cancel();

    private void Apply()
    {
        if (!ThemeColor.TryNormalize(Value, out var normalized))
        {
            return;
        }

        committed = true;
        SetWorkingValue(normalized);
        PickerPopup.IsOpen = false;
    }

    private void Cancel()
    {
        committed = true;
        SetWorkingValue(originalValue);
        PickerPopup.IsOpen = false;
    }

    private void RefreshFromValue(string value, bool updateHex = true)
    {
        if (!ThemeColor.TryParse(value, out var color))
        {
            ValueText.Text = value;
            return;
        }

        synchronizing = true;
        try
        {
            ToHsv(color, out hue, out saturation, out brightness);
            alpha = color.A;
            HueSlider.Value = hue;
            AlphaSlider.Value = alpha;
            HueGradientStop.Color = FromHsv(hue, 1, 1, byte.MaxValue);
            var brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
            ColorChip.Background = brush;
            CurrentPreview.Background = brush;
            if (ThemeColor.TryParse(originalValue, out var original))
            {
                OriginalPreview.Background = new SolidColorBrush(
                    Color.FromArgb(original.A, original.R, original.G, original.B));
            }
            var normalized = ThemeColor.Format(color);
            ValueText.Text = normalized;
            AlphaText.Text = $"{color.A / 255d:P0}";
            AlphaPopupText.Text = $"{color.A / 255d:P0}";
            if (updateHex)
            {
                HexInput.Text = normalized;
            }
            UpdateMarker();
        }
        finally
        {
            synchronizing = false;
        }
    }

    private void UpdateFromHsv()
    {
        var color = FromHsv(hue, saturation, brightness, alpha);
        SetWorkingValue(ThemeColor.Format(new RgbaColor(color.R, color.G, color.B, color.A)));
        RefreshFromValue(Value);
    }

    private void SetWorkingValue(string value)
    {
        synchronizing = true;
        try
        {
            SetCurrentValue(ValueProperty, value);
        }
        finally
        {
            synchronizing = false;
        }
    }

    private void UpdateMarker()
    {
        if (SaturationValueSurface.ActualWidth <= 0 || SaturationValueSurface.ActualHeight <= 0)
        {
            return;
        }

        Canvas.SetLeft(SelectionMarker, (saturation * SaturationValueSurface.ActualWidth) - 8);
        Canvas.SetTop(SelectionMarker, ((1 - brightness) * SaturationValueSurface.ActualHeight) - 8);
    }

    private static void ToHsv(RgbaColor color, out double h, out double s, out double v)
    {
        var red = color.R / 255d;
        var green = color.G / 255d;
        var blue = color.B / 255d;
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var delta = max - min;
        h = delta == 0 ? 0 : max == red
            ? 60 * (((green - blue) / delta) % 6)
            : max == green
                ? 60 * (((blue - red) / delta) + 2)
                : 60 * (((red - green) / delta) + 4);
        if (h < 0)
        {
            h += 360;
        }
        s = max == 0 ? 0 : delta / max;
        v = max;
    }

    private static Color FromHsv(double h, double s, double v, byte a)
    {
        var chroma = v * s;
        var x = chroma * (1 - Math.Abs(((h / 60) % 2) - 1));
        var m = v - chroma;
        var (red, green, blue) = h switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x),
        };
        return Color.FromArgb(
            a,
            (byte)Math.Round((red + m) * 255),
            (byte)Math.Round((green + m) * 255),
            (byte)Math.Round((blue + m) * 255));
    }

    private static readonly string[] DefaultPalette =
    [
        "#080D18", "#0F172A", "#111827", "#243244", "#3B82F6", "#60A5FA",
        "#F8FAFC", "#94A3B8", "#34D399", "#FBBF24", "#F87171", "#B98AE933",
    ];
}
