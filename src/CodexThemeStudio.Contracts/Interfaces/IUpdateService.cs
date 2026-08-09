using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Contracts.Interfaces;

public interface IUpdateService
{
    Task<OperationResult<UpdateCheckResult>> CheckAsync(
        bool forceRefresh,
        CancellationToken cancellationToken);

    Task<OperationResult<StagedUpdate>> DownloadAndStageAsync(
        UpdateReleaseInfo release,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken);
}
