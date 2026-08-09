using System.Windows;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;
using CodexThemeStudio.Desktop.Controls;

namespace CodexThemeStudio.Desktop.Services;

public sealed class WpfUpdateDialogService : IUpdateDialogService
{
    public Task ShowReleaseAsync(
        string currentVersion,
        UpdateReleaseInfo release,
        Action openGitHub,
        Func<IProgress<UpdateDownloadProgress>, CancellationToken,
            Task<OperationResult<UpdateInstallResult>>> downloadAndInstall,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = Application.Current.MainWindow;
        var dialog = new UpdateDialogWindow(
            currentVersion,
            release,
            openGitHub,
            downloadAndInstall,
            cancellationToken)
        {
            Owner = owner,
        };
        using var backdrop = (owner as MainWindow)?.EnterDialogBackdrop();
        _ = dialog.ShowDialog();
        return Task.CompletedTask;
    }
}
