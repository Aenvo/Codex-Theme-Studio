namespace CodexThemeStudio.Desktop.MacOS.ViewModels;

public sealed record ThemeCardRowViewModel(
    IReadOnlyList<ThemeCardViewModel> Items,
    int ColumnCount);
