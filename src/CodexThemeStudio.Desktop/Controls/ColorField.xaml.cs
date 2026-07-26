using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CodexThemeStudio.Desktop.Services;
using CodexThemeStudio.ThemeCore;

namespace CodexThemeStudio.Desktop.Controls;

public partial class ColorField : UserControl
{
    private static readonly Regex CssRgbaPattern = new(
        "^\\s*rgba\\(\\s*(?<r>\\d{1,3})\\s*,\\s*(?<g>\\d{1,3})\\s*,\\s*(?<b>\\d{1,3})\\s*,\\s*(?<a>(?:0|1)(?:\\.\\d+)?|\\.\\d+)\\s*\\)\\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

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

    public static readonly DependencyProperty ColorHistoryProperty = DependencyProperty.Register(
        nameof(ColorHistory), typeof(IEnumerable), typeof(ColorField), new PropertyMetadata(null));

    public static readonly DependencyProperty ColorHistoryServiceProperty = DependencyProperty.Register(
        nameof(ColorHistoryService), typeof(IColorHistoryService), typeof(ColorField), new PropertyMetadata(null));

    public static readonly DependencyProperty IsPickerOpenProperty = DependencyProperty.Register(
        nameof(IsPickerOpen), typeof(bool), typeof(ColorField),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private double hue;
    private double saturation;
    private double brightness;
    private byte alpha = byte.MaxValue;
    private bool synchronizing;
    private bool draggingSaturationValue;
    private bool isEditing;

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

    public IEnumerable? ColorHistory
    {
        get => (IEnumerable?)GetValue(ColorHistoryProperty);
        set => SetValue(ColorHistoryProperty, value);
    }

    public IColorHistoryService? ColorHistoryService
    {
        get => (IColorHistoryService?)GetValue(ColorHistoryServiceProperty);
        set => SetValue(ColorHistoryServiceProperty, value);
    }

    public bool IsPickerOpen
    {
        get => (bool)GetValue(IsPickerOpenProperty);
        set => SetValue(IsPickerOpenProperty, value);
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
        if (!ThemeColor.TryNormalize(Value, out var normalized))
        {
            normalized = "#000000";
        }

        RefreshFromValue(normalized);
        PickerPopup.IsOpen = true;
    }

    private void OnPopupOpened(object? sender, EventArgs e)
    {
        isEditing = true;
        SetCurrentValue(IsPickerOpenProperty, true);
        FocusActiveInput();
        UpdateMarker();
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        isEditing = false;
        SetCurrentValue(IsPickerOpenProperty, false);
    }

    private void OnPopupPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter)
        {
            if (e.Key == Key.Enter)
            {
                CommitPendingOpacityInput();
            }

            PickerPopup.IsOpen = false;
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
            RecordCurrentColor();
        }
    }

    private void OnHueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!synchronizing)
        {
            hue = e.NewValue;
            UpdateFromHsv();
        }
    }

    private void OnSaturationChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!synchronizing)
        {
            saturation = e.NewValue;
            UpdateFromHsv();
        }
    }

    private void OnAlphaChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!synchronizing)
        {
            alpha = (byte)Math.Round(
                e.NewValue / 100d * byte.MaxValue,
                MidpointRounding.AwayFromZero);
            UpdateFromHsv();
        }
    }

    private void OnChannelMouseUp(object sender, MouseButtonEventArgs e) => RecordCurrentColor();

    private void OnFormatSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        var format = SelectedFormat;
        HexInputs.Visibility = format == ColorFormat.Hex ? Visibility.Visible : Visibility.Collapsed;
        RgbInputs.Visibility = format == ColorFormat.Rgb ? Visibility.Visible : Visibility.Collapsed;
        CssInput.Visibility = format == ColorFormat.Css ? Visibility.Visible : Visibility.Collapsed;
        RefreshFromValue(Value);
        FocusActiveInput();
    }

    private void OnHexTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!synchronizing && TryParseHexWithOpacity(HexInput.Text, HexOpacityInput.Text, out var normalized))
        {
            SetWorkingValue(normalized);
            RefreshFromValue(normalized);
        }
    }

    private void OnHexOpacityTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!synchronizing && !HexOpacityInput.IsKeyboardFocusWithin)
        {
            CommitHexOpacityInput();
        }
    }

    private void OnRgbTextChanged(object sender, TextChangedEventArgs e)
    {
        if (ReferenceEquals(sender, AlphaPercentInput) && AlphaPercentInput.IsKeyboardFocusWithin)
        {
            return;
        }

        if (synchronizing ||
            !byte.TryParse(RedInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(GreenInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(BlueInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var blue) ||
            !double.TryParse(AlphaPercentInput.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var opacity))
        {
            return;
        }

        SetWorkingValue(ThemeColor.Format(new RgbaColor(
            red,
            green,
            blue,
            (byte)Math.Round(Math.Clamp(opacity, 0, 100) / 100 * 255, MidpointRounding.AwayFromZero))));
        RefreshFromValue(Value, refreshInputs: false);
    }

    private void OnCssTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!synchronizing && TryParseCss(CssInput.Text, out var color))
        {
            SetWorkingValue(ThemeColor.Format(color));
            RefreshFromValue(Value, refreshInputs: false);
        }
    }

    private void OnValueInputGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        ((TextBox)sender).SelectAll();

    private void OnValueInputPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin)
        {
            textBox.Focus();
            textBox.SelectAll();
            e.Handled = true;
        }
    }

    private void OnValueInputLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(sender, HexOpacityInput))
        {
            CommitHexOpacityInput();
        }
        else if (ReferenceEquals(sender, AlphaPercentInput))
        {
            CommitRgbOpacityInput();
        }
        else
        {
            RefreshFromValue(Value);
        }
    }

    private void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value } && ThemeColor.TryNormalize(value, out var normalized))
        {
            SetWorkingValue(normalized);
            RefreshFromValue(normalized);
        }
    }

    private void RefreshFromValue(string value, bool refreshInputs = true)
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
            SaturationSlider.Value = saturation;
            AlphaSlider.Value = AlphaToPercentage(alpha);
            RefreshColorPresentation(color, refreshInputs);
        }
        finally
        {
            synchronizing = false;
        }
    }

    private void UpdateFromHsv()
    {
        var color = FromHsv(hue, saturation, brightness, alpha);
        var rgba = new RgbaColor(color.R, color.G, color.B, alpha);
        SetWorkingValue(ThemeColor.Format(rgba), refreshFromValue: false);

        synchronizing = true;
        try
        {
            HueSlider.Value = hue;
            SaturationSlider.Value = saturation;
            AlphaSlider.Value = AlphaToPercentage(alpha);
            RefreshColorPresentation(rgba, refreshInputs: true);
        }
        finally
        {
            synchronizing = false;
        }
    }

    private void SetWorkingValue(string value, bool refreshFromValue = true)
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

        if (refreshFromValue)
        {
            RefreshFromValue(value);
        }
    }

    private void RefreshColorPresentation(RgbaColor color, bool refreshInputs)
    {
        HueGradientStop.Color = FromHsv(hue, 1, 1, byte.MaxValue);
        SaturationStartGradientStop.Color = FromHsv(hue, 0, brightness, byte.MaxValue);
        SaturationEndGradientStop.Color = FromHsv(hue, 1, brightness, byte.MaxValue);
        ColorChip.Background = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        ValueText.Text = ThemeColor.Format(color);
        AlphaText.Text = $"{color.A / 255d:P0}";
        if (refreshInputs)
        {
            HexInput.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            HexOpacityInput.Text = AlphaToPercentage(color.A).ToString("0", CultureInfo.InvariantCulture);
            RedInput.Text = color.R.ToString(CultureInfo.InvariantCulture);
            GreenInput.Text = color.G.ToString(CultureInfo.InvariantCulture);
            BlueInput.Text = color.B.ToString(CultureInfo.InvariantCulture);
            AlphaPercentInput.Text = AlphaToPercentage(color.A).ToString("0", CultureInfo.InvariantCulture);
            CssInput.Text = $"rgba({color.R}, {color.G}, {color.B}, {(color.A / 255d).ToString("0.###", CultureInfo.InvariantCulture)})";
        }

        UpdateMarker();
    }

    private void RecordCurrentColor()
    {
        if (isEditing && ThemeColor.TryNormalize(Value, out var normalized))
        {
            ColorHistoryService?.Record(normalized);
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

    private ColorFormat SelectedFormat => FormatSelector.SelectedIndex switch
    {
        1 => ColorFormat.Rgb,
        2 => ColorFormat.Css,
        _ => ColorFormat.Hex,
    };

    private void FocusActiveInput()
    {
        TextBox input = SelectedFormat switch
        {
            ColorFormat.Rgb => RedInput,
            ColorFormat.Css => CssInput,
            _ => HexInput,
        };
        input.Focus();
        input.SelectAll();
    }

    private static double AlphaToPercentage(byte value) =>
        Math.Round(value / (double)byte.MaxValue * 100, MidpointRounding.AwayFromZero);

    private void CommitPendingOpacityInput()
    {
        if (HexOpacityInput.IsKeyboardFocusWithin)
        {
            CommitHexOpacityInput();
        }
        else if (AlphaPercentInput.IsKeyboardFocusWithin)
        {
            CommitRgbOpacityInput();
        }
    }

    private void CommitHexOpacityInput()
    {
        if (TryParseHexWithOpacity(HexInput.Text, HexOpacityInput.Text, out var normalized))
        {
            SetWorkingValue(normalized);
            RefreshFromValue(normalized);
            return;
        }

        RefreshFromValue(Value);
    }

    private void CommitRgbOpacityInput()
    {
        if (byte.TryParse(RedInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var red) &&
            byte.TryParse(GreenInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var green) &&
            byte.TryParse(BlueInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var blue) &&
            double.TryParse(AlphaPercentInput.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var opacity))
        {
            SetWorkingValue(ThemeColor.Format(new RgbaColor(
                red,
                green,
                blue,
                (byte)Math.Round(Math.Clamp(opacity, 0, 100) / 100 * 255, MidpointRounding.AwayFromZero))));
        }

        RefreshFromValue(Value);
    }

    private static bool TryParseCss(string value, out RgbaColor color)
    {
        color = default;
        var match = CssRgbaPattern.Match(value);
        if (!match.Success ||
            !byte.TryParse(match.Groups["r"].Value, out var red) ||
            !byte.TryParse(match.Groups["g"].Value, out var green) ||
            !byte.TryParse(match.Groups["b"].Value, out var blue) ||
            !double.TryParse(match.Groups["a"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var opacity) ||
            opacity is < 0 or > 1)
        {
            return false;
        }

        color = new RgbaColor(red, green, blue,
            (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255, MidpointRounding.AwayFromZero));
        return true;
    }

    private static bool TryParseHexWithOpacity(string hex, string opacityText, out string normalized)
    {
        normalized = string.Empty;
        if (!double.TryParse(opacityText, NumberStyles.Number, CultureInfo.InvariantCulture, out var opacity))
        {
            return false;
        }

        opacity = Math.Clamp(opacity, 0, 100);

        var trimmed = hex.Trim();
        if (trimmed.Length == 7 && ThemeColor.TryNormalize($"{trimmed}{(byte)Math.Round(opacity / 100 * 255, MidpointRounding.AwayFromZero):X2}", out normalized))
        {
            return true;
        }

        return ThemeColor.TryNormalize(trimmed, out normalized);
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

    private enum ColorFormat
    {
        Hex,
        Rgb,
        Css,
    }

}
