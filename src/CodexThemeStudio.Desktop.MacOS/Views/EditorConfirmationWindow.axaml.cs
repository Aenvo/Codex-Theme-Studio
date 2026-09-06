using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CodexThemeStudio.Desktop.MacOS.Views;

public sealed partial class EditorConfirmationWindow : Window
{
    public EditorConfirmationWindow() => InitializeComponent();

    private void KeepEditing_OnClick(object? sender, RoutedEventArgs args) =>
        Close(false);

    private void Discard_OnClick(object? sender, RoutedEventArgs args) =>
        Close(true);
}
