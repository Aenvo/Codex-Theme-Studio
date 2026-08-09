using System.Windows;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Desktop.Controls;

internal partial class UpdateDialogWindow : Window
{
    private readonly Action openGitHub;
    private readonly Func<IProgress<UpdateDownloadProgress>, CancellationToken,
        Task<OperationResult<UpdateInstallResult>>> downloadAndInstall;
    private readonly CancellationTokenSource cancellation;
    private bool installing;

    internal UpdateDialogWindow(
        string currentVersion,
        UpdateReleaseInfo release,
        Action openGitHub,
        Func<IProgress<UpdateDownloadProgress>, CancellationToken,
            Task<OperationResult<UpdateInstallResult>>> downloadAndInstall,
        CancellationToken cancellationToken)
    {
        InitializeComponent();
        this.openGitHub = openGitHub;
        this.downloadAndInstall = downloadAndInstall;
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        VersionText.Text = $"v{currentVersion}  →  v{release.Version}";
        PublishedText.Text = $"发布时间：{release.PublishedAt.ToLocalTime():yyyy-MM-dd HH:mm}";
        ReleaseNotesText.Text = release.ReleaseNotes;
        Closed += (_, _) => cancellation.Dispose();
    }

    private void GitHub_Click(object sender, RoutedEventArgs e) => openGitHub();

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (installing) return;
        installing = true;
        ProgressPanel.Visibility = Visibility.Visible;
        DownloadButton.IsEnabled = false;
        GitHubButton.IsEnabled = false;
        var progress = new Progress<UpdateDownloadProgress>(value =>
        {
            DownloadProgress.Value = value.Percentage;
            ProgressPercentText.Text = $"{value.Percentage}%";
            ProgressStageText.Text = value.Stage;
        });

        var result = await downloadAndInstall(progress, cancellation.Token);
        if (result.IsSuccess &&
            result.Value!.Outcome == UpdateInstallOutcome.Started)
        {
            ProgressStageText.Text = "下载完成，正在重启更新…";
            ProgressPercentText.Text = "100%";
            DownloadProgress.Value = 100;
            CloseButton.IsEnabled = false;
            return;
        }

        ResultText.Visibility = Visibility.Visible;
        ResultText.Text = result.IsSuccess
            ? result.Value!.UserMessage
            : $"{result.Error!.UserMessage}\n未修改任何程序文件。";
        ProgressStageText.Text = result.IsSuccess ? "更新包已就绪" : "更新失败";
        DownloadButton.Content = "重试";
        DownloadButton.IsEnabled = true;
        GitHubButton.IsEnabled = true;
        installing = false;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (installing) cancellation.Cancel();
        Close();
    }
}
