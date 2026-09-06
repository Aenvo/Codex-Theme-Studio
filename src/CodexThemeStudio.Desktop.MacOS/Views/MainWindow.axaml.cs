using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CodexThemeStudio.Desktop.MacOS.ViewModels;

namespace CodexThemeStudio.Desktop.MacOS.Views;

public sealed partial class MainWindow : Window
{
    private bool closeAfterSafeCompletion;
    private MainWindowViewModel? boundViewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged += OnSizeChanged;
        Closing += OnClosing;
        KeyDown += OnKeyDown;
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (boundViewModel is not null)
        {
            boundViewModel.SearchFocusRequested -= OnSearchFocusRequested;
            boundViewModel.SettingsRequested -= OnSettingsRequested;
            boundViewModel.EditorImageRequested -= OnEditorImageRequested;
            boundViewModel.EditorCancelConfirmationRequested -=
                OnEditorCancelConfirmationRequested;
            boundViewModel.EditorCopyNameRequested -= OnEditorCopyNameRequested;
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            boundViewModel = viewModel;
            viewModel.SearchFocusRequested += OnSearchFocusRequested;
            viewModel.SettingsRequested += OnSettingsRequested;
            viewModel.EditorImageRequested += OnEditorImageRequested;
            viewModel.EditorCancelConfirmationRequested +=
                OnEditorCancelConfirmationRequested;
            viewModel.EditorCopyNameRequested += OnEditorCopyNameRequested;
            UpdateCardColumnCount(viewModel, Bounds.Width);
        }
        else
        {
            boundViewModel = null;
        }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            UpdateCardColumnCount(viewModel, args.NewSize.Width);
        }

        UpdateEditorPreviewResponsiveLayout(args.NewSize.Width);
    }

    private void OnSearchFocusRequested(object? sender, EventArgs args)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private static void OnSettingsRequested(object? sender, EventArgs args)
    {
        if (Avalonia.Application.Current is App app)
        {
            app.ShowSettingsWindow();
        }
    }

    private async void OnEditorImageRequested(
        object? sender,
        EditorDialogRequestEventArgs<Uri?> args)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "选择主题背景",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("支持的图片")
                        {
                            Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"],
                            MimeTypes = ["image/png", "image/jpeg", "image/webp"],
                        },
                    ],
                });
            args.Complete(files.Count == 1 ? files[0].Path : null);
        }
        catch
        {
            args.Complete(null);
        }
    }

    private async void OnEditorCancelConfirmationRequested(
        object? sender,
        EditorDialogRequestEventArgs<bool> args)
    {
        var dialog = new EditorConfirmationWindow();
        args.Complete(await dialog.ShowDialog<bool>(this));
    }

    private async void OnEditorCopyNameRequested(
        object? sender,
        EditorDialogRequestEventArgs<string?> args)
    {
        var proposedName = boundViewModel?.Editor is { } editor
            ? $"{editor.Name} 副本"
            : "主题副本";
        var dialog = new EditorCopyNameWindow(proposedName);
        args.Complete(await dialog.ShowDialog<string?>(this));
    }

    private void EditorArtSize_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs args)
    {
        if (sender is ComboBox { SelectedItem: string value } &&
            DataContext is MainWindowViewModel { Editor: { } editor })
        {
            editor.ArtSize = value;
        }
    }

    private void EditorTaskMode_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs args)
    {
        if (sender is ComboBox { SelectedItem: string value } &&
            DataContext is MainWindowViewModel { Editor: { } editor })
        {
            editor.TaskMode = value;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.KeyModifiers.HasFlag(KeyModifiers.Meta) &&
            args.Key == Key.F &&
            DataContext is MainWindowViewModel viewModel)
        {
            viewModel.FocusSearchCommand.Execute(null);
            args.Handled = true;
        }
        else if (args.KeyModifiers.HasFlag(KeyModifiers.Meta) &&
                 args.Key == Key.W)
        {
            Close();
            args.Handled = true;
        }
    }

    private void EditorImage_OnDragOver(
        object? sender,
        DragEventArgs args)
    {
        args.DragEffects = HasSingleSupportedImage(args.DataTransfer)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        args.Handled = true;
    }

    private void EditorImage_OnDrop(object? sender, DragEventArgs args)
    {
        var item = args.DataTransfer.Items.OfType<IDataTransferItem>().SingleOrDefault();
        var file = item?.TryGetFile();
        if (file is not null &&
            IsSupportedImage(file.Name) &&
            DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetEditorPreviewImage(file.Path);
        }

        args.Handled = true;
    }

    private static bool HasSingleSupportedImage(IDataTransfer data)
    {
        var items = data.Items.OfType<IDataTransferItem>().ToArray();
        return items.Length == 1 &&
            items[0].TryGetFile() is { } file &&
            IsSupportedImage(file.Name);
    }

    private static bool IsSupportedImage(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() is
            ".png" or ".jpg" or ".jpeg" or ".webp";

    private void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (closeAfterSafeCompletion ||
            DataContext is not MainWindowViewModel { IsOperationActive: true } viewModel)
        {
            return;
        }

        args.Cancel = true;
        viewModel.NotifyCloseDeferred();
        Activate();
        _ = CloseWhenSafeAsync(viewModel);
    }

    private async Task CloseWhenSafeAsync(MainWindowViewModel viewModel)
    {
        while (!viewModel.IsSafeToClose)
        {
            await Task.Delay(80);
        }

        closeAfterSafeCompletion = true;
        Close();
    }

    private static void UpdateCardColumnCount(
        MainWindowViewModel viewModel,
        double width)
    {
        viewModel.SetCardColumnCount(
            width >= 1180
                ? 3
                : width >= 900
                    ? 2
                    : 1);
    }

    private void UpdateEditorPreviewResponsiveLayout(double windowWidth)
    {
        if (EditorTaskPreviewGrid is null ||
            EditorTaskEnvironmentPanel is null ||
            EditorTaskPreviewGrid.ColumnDefinitions.Count < 2)
        {
            return;
        }

        var showEnvironment = windowWidth >= 1560;
        EditorTaskPreviewGrid.ColumnDefinitions[1].Width =
            new GridLength(showEnvironment ? 220 : 0);
        EditorTaskEnvironmentPanel.IsVisible = showEnvironment;
    }
}
