using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CodexThemeStudio.Desktop.MacOS.Views;

public sealed partial class EditorCopyNameWindow : Window
{
    public EditorCopyNameWindow() : this("主题副本")
    {
    }

    public EditorCopyNameWindow(string proposedName)
    {
        InitializeComponent();
        CopyNameBox.Text = proposedName;
        Opened += (_, _) =>
        {
            CopyNameBox.Focus();
            CopyNameBox.SelectAll();
        };
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs args) =>
        Close(null);

    private void Save_OnClick(object? sender, RoutedEventArgs args) =>
        Close(string.IsNullOrWhiteSpace(CopyNameBox.Text)
            ? null
            : CopyNameBox.Text.Trim());
}
