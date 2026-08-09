using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Desktop.ViewModels;

namespace CodexThemeStudio.Desktop;

public partial class MainWindow : Window
{
    private const double TaskPreviewInputMaximumWidth = 760;
    private const double TaskPreviewEnvironmentMinimumWidth = 960;
    private const double TaskPreviewEnvironmentWidth = 260;
    private const double TaskPreviewWorkspaceMaximumWidth = 720;

    private int dialogBackdropDepth;

    public MainWindow()
        : this(null)
    {
    }

    public MainWindow(MainWindowViewModel? viewModel)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => EnableDarkTitleBar();
        Activated += (_, _) =>
            (DataContext as MainWindowViewModel)?.OnWindowActivated();
        Deactivated += (_, _) =>
            (DataContext as MainWindowViewModel)?.OnWindowDeactivated();
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        Loaded += (_, _) =>
        {
            UpdatePreviewImageLayout();
            UpdateTaskPreviewLayout();
            UpdateTaskPreviewResponsiveLayout();
        };
        PreviewArtSurface.SizeChanged += (_, _) =>
        {
            UpdatePreviewImageLayout();
            UpdateTaskPreviewResponsiveLayout();
        };
        PreviewImageViewport.SizeChanged += (_, _) => UpdatePreviewImageLayout();
        TaskPreviewContentHost.SizeChanged += (_, _) => UpdateTaskPreviewLayout();
        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is MainWindowViewModel oldViewModel &&
                oldViewModel.Editor is not null)
            {
                oldViewModel.Editor.PropertyChanged -= OnEditorPropertyChanged;
                oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (args.NewValue is MainWindowViewModel newViewModel &&
                newViewModel.Editor is not null)
            {
                newViewModel.Editor.PropertyChanged += OnEditorPropertyChanged;
                newViewModel.PropertyChanged += OnViewModelPropertyChanged;
            }
        };
        DataContext = viewModel;
        Closed += (_, _) =>
            (DataContext as MainWindowViewModel)?.Dispose();
    }

    private CustomPopupPlacement[] PlaceActionButtonToolTip(
        Size popupSize,
        Size targetSize,
        Point _)
    {
        var centeredX = (targetSize.Width - popupSize.Width) / 2;
        var aboveY = -popupSize.Height - 8;
        return
        [
            new CustomPopupPlacement(
                new Point(centeredX, aboveY),
                PopupPrimaryAxis.Horizontal),
        ];
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
            nameof(ThemeEditorViewModel.CropScale) or
            nameof(ThemeEditorViewModel.ArtSize))
        {
            UpdatePreviewImageLayout();
        }
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.CurrentPage) ||
            sender is not MainWindowViewModel
            {
                CurrentPage: LibraryPage.Editor,
                Editor: { IsNew: true },
            })
        {
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(EditorScrollViewer.ScrollToTop));
    }

    private void OnPreviewImageTargetUpdated(
        object sender,
        DataTransferEventArgs e)
    {
        // The previous image can have an explicit size from the preview layout.
        // Clear it before WPF measures the newly assigned source; otherwise a wide
        // or tall image may retain the upload thumbnail's old dimensions.
        PreviewBackgroundImage.Width = double.NaN;
        PreviewBackgroundImage.Height = double.NaN;
        PreviewBackgroundImage.RenderTransform = Transform.Identity;

        // TargetUpdated may occur before the preview surface has been measured.
        // Re-run after that layout pass so the cover/contain calculation uses the
        // real preview dimensions instead of leaving the image at its natural size.
        Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(UpdatePreviewImageLayout));
    }

    internal void UpdatePreviewImageLayout()
    {
        if (DataContext is not MainWindowViewModel { Editor: { } editor } ||
            PreviewBackgroundImage.Source is not { } imageSource ||
            imageSource.Width <= 0 ||
            imageSource.Height <= 0 ||
            PreviewImageViewport.ActualWidth <= 0 ||
            PreviewImageViewport.ActualHeight <= 0)
        {
            return;
        }

        var widthScale = PreviewImageViewport.ActualWidth / imageSource.Width;
        var heightScale = PreviewImageViewport.ActualHeight / imageSource.Height;
        var scale = editor.ArtSize == ThemeArtSize.Contain
            ? Math.Min(widthScale, heightScale)
            : Math.Max(widthScale, heightScale);
        if (editor.IsCropMode)
        {
            scale *= editor.CropScale;
        }
        var renderedWidth = imageSource.Width * scale;
        var renderedHeight = imageSource.Height * scale;
        var focusX = editor.IsCropMode ? editor.FocusX : 0.5;
        var focusY = editor.IsCropMode ? editor.FocusY : 0.5;

        PreviewBackgroundImage.Width = renderedWidth;
        PreviewBackgroundImage.Height = renderedHeight;
        PreviewBackgroundImage.RenderTransform = new TranslateTransform(
            (PreviewImageViewport.ActualWidth - renderedWidth) * focusX,
            (PreviewImageViewport.ActualHeight - renderedHeight) * focusY);
    }

    private void UpdateTaskPreviewLayout()
    {
        var availableWidth = TaskPreviewContentHost.ActualWidth;
        if (availableWidth <= 0)
        {
            return;
        }

        TaskPreviewWorkspace.Width = Math.Min(
            availableWidth,
            TaskPreviewWorkspaceMaximumWidth);
        TaskPreviewInput.Width = Math.Min(
            availableWidth,
            TaskPreviewInputMaximumWidth);
    }

    private void UpdateTaskPreviewResponsiveLayout()
    {
        if (PreviewArtSurface.ActualWidth <= 0)
        {
            return;
        }

        var showEnvironment =
            PreviewArtSurface.ActualWidth >= TaskPreviewEnvironmentMinimumWidth;
        TaskPreviewEnvironmentColumn.Width = new GridLength(
            showEnvironment ? TaskPreviewEnvironmentWidth : 0);
        TaskPreviewEnvironmentPanel.Visibility = showEnvironment
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateTaskPreviewLayout();
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
