using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Contracts.Interfaces;

public interface IUpdateInstaller
{
    Task<OperationResult<UpdateInstallResult>> StartAsync(
        StagedUpdate stagedUpdate,
        CancellationToken cancellationToken);

    Task<OperationResult<UpdateInstallResult>> RetryCleanupAsync(
        string token,
        CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<UpdateInstallResult>.Failure(
            OperationErrorCode.NotImplemented,
            "当前更新器不支持重试清理。",
            "update.cleanup.not_supported"));
}
