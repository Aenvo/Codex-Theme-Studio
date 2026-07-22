using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace CodexThemeStudio.Desktop.Services;

public sealed class WpfUserDialogService : IUserDialogService
{
    public Task<string?> RequestTextAsync(
        string title,
        string prompt,
        string initialValue,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var owner = Application.Current.MainWindow;
        var textBox = new TextBox
        {
            Text = initialValue,
            Margin = new Thickness(0, 10, 0, 18),
            MaxLength = 120,
        };
        var accepted = false;
        var dialog = CreateDialog(title, owner);
        var confirm = new Button
        {
            Content = "确定",
            IsDefault = true,
            MinWidth = 82,
        };
        confirm.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButtonStyle");
        confirm.Click += (_, _) =>
        {
            accepted = true;
            dialog.Close();
        };
        var cancel = new Button
        {
            Content = "取消",
            IsCancel = true,
            MinWidth = 82,
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        var content = new Grid { Margin = new Thickness(24) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.Children.Add(new TextBlock
        {
            Text = prompt,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
        });
        content.Children.Add(textBox);
        content.Children.Add(buttons);
        Grid.SetRow(textBox, 1);
        Grid.SetRow(buttons, 2);
        dialog.Content = content;

        dialog.Loaded += (_, _) =>
        {
            textBox.Focus();
            textBox.SelectAll();
        };
        dialog.ShowDialog();
        return Task.FromResult(accepted ? textBox.Text.Trim() : null);
    }

    public Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = MessageBox.Show(
            Application.Current.MainWindow,
            $"{message}\n\n{confirmText}",
            title,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        return Task.FromResult(result == MessageBoxResult.OK);
    }

    public Task<string?> PickOpenFileAsync(
        string title,
        string filter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false,
        };
        return Task.FromResult(
            dialog.ShowDialog(Application.Current.MainWindow) == true
                ? dialog.FileName
                : null);
    }

    public Task<string?> PickSaveFileAsync(
        string title,
        string filter,
        string defaultExtension,
        string suggestedFileName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            DefaultExt = defaultExtension,
            AddExtension = true,
            FileName = suggestedFileName,
            OverwritePrompt = true,
        };
        return Task.FromResult(
            dialog.ShowDialog(Application.Current.MainWindow) == true
                ? dialog.FileName
                : null);
    }

    public Task<string?> PickFolderAsync(
        string title,
        string? initialDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new OpenFolderDialog
        {
            Title = title,
            InitialDirectory = initialDirectory ?? string.Empty,
            Multiselect = false,
        };
        return Task.FromResult(
            dialog.ShowDialog(Application.Current.MainWindow) == true
                ? dialog.FolderName
                : null);
    }

    public void ShowInformation(string title, string message) =>
        MessageBox.Show(
            Application.Current.MainWindow,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    private static Window CreateDialog(string title, Window? owner) =>
        new()
        {
            Title = title,
            Owner = owner,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            MinHeight = 210,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
}
