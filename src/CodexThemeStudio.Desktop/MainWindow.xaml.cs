using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CodexThemeStudio.Desktop.ViewModels;

namespace CodexThemeStudio.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
        : this(null)
    {
    }

    public MainWindow(MainWindowViewModel? viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += (_, _) => UpdateFocusMarker();
        PreviewArtSurface.SizeChanged += (_, _) => UpdateFocusMarker();
        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is MainWindowViewModel oldViewModel &&
                oldViewModel.Editor is not null)
            {
                oldViewModel.Editor.PropertyChanged -= OnEditorPropertyChanged;
            }

            if (args.NewValue is MainWindowViewModel newViewModel &&
                newViewModel.Editor is not null)
            {
                newViewModel.Editor.PropertyChanged += OnEditorPropertyChanged;
            }
        };
        Closed += (_, _) => viewModel?.Dispose();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.F5 &&
                 DataContext is MainWindowViewModel viewModel &&
                 viewModel.RefreshCommand.CanExecute(null))
        {
            viewModel.RefreshCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnPreviewFocusClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { Editor: { } editor } ||
            sender is not FrameworkElement surface ||
            surface.ActualWidth <= 0 ||
            surface.ActualHeight <= 0)
        {
            return;
        }

        var point = e.GetPosition(surface);
        editor.FocusX = Math.Clamp(point.X / surface.ActualWidth, 0, 1);
        editor.FocusY = Math.Clamp(point.Y / surface.ActualHeight, 0, 1);
        UpdateFocusMarker();
        e.Handled = true;
    }

    private void OnEditorPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ThemeEditorViewModel.FocusX) or
            nameof(ThemeEditorViewModel.FocusY))
        {
            UpdateFocusMarker();
        }
    }

    private void UpdateFocusMarker()
    {
        if (DataContext is not MainWindowViewModel { Editor: { } editor } ||
            PreviewArtSurface.ActualWidth <= 0 ||
            PreviewArtSurface.ActualHeight <= 0)
        {
            return;
        }

        Canvas.SetLeft(
            FocusMarker,
            (editor.FocusX * PreviewArtSurface.ActualWidth) - (FocusMarker.Width / 2));
        Canvas.SetTop(
            FocusMarker,
            (editor.FocusY * PreviewArtSurface.ActualHeight) - (FocusMarker.Height / 2));
    }

    private void OnEditorImageDragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasSingleImageFile(e.Data)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnEditorImageDrop(object sender, DragEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files &&
            HasSupportedImageExtension(files[0]))
        {
            await viewModel.ImportEditorImageAsync(files[0]);
        }

        e.Handled = true;
    }

    private static bool HasSingleImageFile(IDataObject data) =>
        data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files &&
        HasSupportedImageExtension(files[0]);

    private static bool HasSupportedImageExtension(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp";
}
