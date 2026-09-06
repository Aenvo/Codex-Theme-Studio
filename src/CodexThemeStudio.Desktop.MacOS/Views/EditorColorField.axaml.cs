using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using CodexThemeStudio.Desktop.MacOS.ViewModels;

namespace CodexThemeStudio.Desktop.MacOS.Views;

public partial class EditorColorField : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<EditorColorField, string>(
            nameof(Label),
            string.Empty);

    public static readonly StyledProperty<string> ValueProperty =
        AvaloniaProperty.Register<EditorColorField, string>(
            nameof(Value),
            "#000000",
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<ThemeEditorViewModel?> EditorProperty =
        AvaloniaProperty.Register<EditorColorField, ThemeEditorViewModel?>(
            nameof(Editor));

    public static readonly DirectProperty<EditorColorField, string>
        AlphaLabelProperty = AvaloniaProperty.RegisterDirect<
            EditorColorField,
            string>(nameof(AlphaLabel), field => field.AlphaLabel);

    private string alphaLabel = "100%";

    public EditorColorField() => InitializeComponent();

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public ThemeEditorViewModel? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    public string AlphaLabel
    {
        get => alphaLabel;
        private set => SetAndRaise(AlphaLabelProperty, ref alphaLabel, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty)
        {
            AlphaLabel = CssColor.TryParse(change.NewValue as string, out var color)
                ? $"{color.A / 255d:P0}"
                : "—";
        }
    }

    private async void OpenButton_OnClick(object? sender, RoutedEventArgs args)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var selected = await new EditorColorWindow(
                Value,
                Editor?.ThemePaletteColors,
                Editor?.ColorHistory)
            .ShowDialog<string?>(owner);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            SetCurrentValue(ValueProperty, selected);
            Editor?.RecordColor(selected);
        }
    }
}
