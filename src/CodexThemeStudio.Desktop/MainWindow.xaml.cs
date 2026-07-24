using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Desktop.ViewModels;

namespace CodexThemeStudio.Desktop;

public partial class MainWindow : Window
{
    private int dialogBackdropDepth;

    public MainWindow()
        : this(null)
    {
    }

    public MainWindow(MainWindowViewModel? viewModel)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => EnableDarkTitleBar();
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        Loaded += (_, _) => UpdatePreviewImageLayout();
        PreviewArtSurface.SizeChanged += (_, _) => UpdatePreviewImageLayout();
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
        DataContext = viewModel;
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

    private void OnPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject clickSource)
        {
            ClearSearchFocusOnOutsideClick(SearchBox, clickSource);
        }
    }

    internal static bool ClearSearchFocusOnOutsideClick(
        TextBox searchBox,
        DependencyObject clickSource)
    {
        if (!searchBox.IsKeyboardFocusWithin ||
            ReferenceEquals(FindVisualAncestor<TextBox>(clickSource), searchBox))
        {
            return false;
        }

        Keyboard.ClearFocus();
        return true;
    }

    private void OnThemeListPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is ListBox listBox &&
            e.OriginalSource is DependencyObject clickSource)
        {
            ClearThemeSelectionOnBackgroundClick(listBox, clickSource);
        }
    }

    internal static bool ClearThemeSelectionOnBackgroundClick(
        ListBox listBox,
        DependencyObject clickSource)
    {
        if (FindVisualAncestor<ListBoxItem>(clickSource) is not null ||
            FindVisualAncestor<ScrollBar>(clickSource) is not null)
        {
            return false;
        }

        listBox.UnselectAll();
        listBox.Focus();
        return true;
    }

    private static T? FindVisualAncestor<T>(DependencyObject element)
        where T : DependencyObject
    {
        for (DependencyObject? current = element;
             current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private void OnEditorPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ThemeEditorViewModel.FocusX) or
            nameof(ThemeEditorViewModel.FocusY) or
            nameof(ThemeEditorViewModel.ArtSize))
        {
            UpdatePreviewImageLayout();
        }
    }

    private void OnPreviewImageTargetUpdated(
        object sender,
        DataTransferEventArgs e) =>
        UpdatePreviewImageLayout();

    private void UpdatePreviewImageLayout()
    {
        if (DataContext is not MainWindowViewModel { Editor: { } editor } ||
            PreviewBackgroundImage.Source is not { } imageSource ||
            imageSource.Width <= 0 ||
            imageSource.Height <= 0 ||
            PreviewArtSurface.ActualWidth <= 0 ||
            PreviewArtSurface.ActualHeight <= 0)
        {
            return;
        }

        var widthScale = PreviewArtSurface.ActualWidth / imageSource.Width;
        var heightScale = PreviewArtSurface.ActualHeight / imageSource.Height;
        var scale = editor.ArtSize == ThemeArtSize.Contain
            ? Math.Min(widthScale, heightScale)
            : Math.Max(widthScale, heightScale);
        var renderedWidth = imageSource.Width * scale;
        var renderedHeight = imageSource.Height * scale;
        var focusX = editor.IsCropMode ? editor.FocusX : 0.5;
        var focusY = editor.IsCropMode ? editor.FocusY : 0.5;

        PreviewBackgroundImage.Width = renderedWidth;
        PreviewBackgroundImage.Height = renderedHeight;
        PreviewBackgroundImage.RenderTransform = new TranslateTransform(
            (PreviewArtSurface.ActualWidth - renderedWidth) * focusX,
            (PreviewArtSurface.ActualHeight - renderedHeight) * focusY);
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

    private void EnableDarkTitleBar()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var enabled = 1;
        _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
    }

    internal IDisposable EnterDialogBackdrop()
    {
        dialogBackdropDepth++;
        if (dialogBackdropDepth == 1)
        {
            ApplicationSurface.Effect = new BlurEffect
            {
                Radius = 4,
                RenderingBias = RenderingBias.Performance,
            };
            DialogLightbox.Visibility = Visibility.Visible;
        }

        return new DialogBackdropLease(this);
    }

    private void ExitDialogBackdrop()
    {
        if (dialogBackdropDepth == 0 || --dialogBackdropDepth != 0)
        {
            return;
        }

        DialogLightbox.Visibility = Visibility.Collapsed;
        ApplicationSurface.Effect = null;
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int value,
        int valueSize);

    private sealed class DialogBackdropLease(MainWindow owner) : IDisposable
    {
        private MainWindow? owner = owner;

        public void Dispose()
        {
            var current = owner;
            if (current is null)
            {
                return;
            }

            owner = null;
            current.ExitDialogBackdrop();
        }
    }
}
