using System.Windows;
using CodexThemeStudio.Desktop.Controls;
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
        var dialog = AppDialogWindow.CreateTextInput(
            title,
            prompt,
            initialValue,
            owner);
        _ = ShowWithBackdrop(owner, dialog.ShowDialog);
        return Task.FromResult(dialog.Accepted ? dialog.InputText : null);
    }

    public Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = Application.Current.MainWindow;
        var dialog = AppDialogWindow.CreateConfirmation(
            title,
            message,
            confirmText,
            owner);
        _ = ShowWithBackdrop(owner, dialog.ShowDialog);
        return Task.FromResult(dialog.Accepted);
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
        var owner = Application.Current.MainWindow;
        return Task.FromResult(
            ShowWithBackdrop(owner, () => dialog.ShowDialog(owner)) == true
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
        var owner = Application.Current.MainWindow;
        return Task.FromResult(
            ShowWithBackdrop(owner, () => dialog.ShowDialog(owner)) == true
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
        var owner = Application.Current.MainWindow;
        return Task.FromResult(
            ShowWithBackdrop(owner, () => dialog.ShowDialog(owner)) == true
                ? dialog.FolderName
                : null);
    }

    public void ShowInformation(string title, string message)
    {
        var owner = Application.Current.MainWindow;
        var dialog = AppDialogWindow.CreateInformation(
            title,
            message,
            owner);
        _ = ShowWithBackdrop(owner, dialog.ShowDialog);
    }

    private static T ShowWithBackdrop<T>(Window? owner, Func<T> showDialog)
    {
        using var backdrop = (owner as MainWindow)?.EnterDialogBackdrop();
        return showDialog();
    }
}
