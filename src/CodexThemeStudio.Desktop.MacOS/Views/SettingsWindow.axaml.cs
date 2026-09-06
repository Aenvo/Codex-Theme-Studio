using Avalonia.Controls;
using CodexThemeStudio.Desktop.MacOS.ViewModels;

namespace CodexThemeStudio.Desktop.MacOS.Views;

public sealed partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = new SettingsWindowViewModel();
    }
}
