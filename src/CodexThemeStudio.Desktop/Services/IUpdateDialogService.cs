using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Desktop.Services;

public interface IUpdateDialogService
{
    Task ShowReleaseAsync(
        string currentVersion,
        UpdateReleaseInfo release,
        Action openGitHub,
        Func<IProgress<UpdateDownloadProgress>, CancellationToken,
            Task<OperationResult<UpdateInstallResult>>> downloadAndInstall,
        CancellationToken cancellationToken);
}
