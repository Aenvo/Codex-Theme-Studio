using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Contracts.Interfaces;

public interface IPersistenceService
{
    Task<OperationResult<ThemeRuntimeStatus>> EnableAsync(
        ThemePackage theme,
        CancellationToken cancellationToken);

    Task<OperationResult<ThemeRuntimeStatus>> EnableAsync(
        ThemePackage theme,
        PersistenceOptions options,
        CancellationToken cancellationToken);

    Task<OperationResult<ThemeRuntimeStatus>> SwitchAsync(
        ThemePackage theme,
        CancellationToken cancellationToken);

    Task<OperationResult<ThemeRuntimeStatus>> DisableAsync(
        CancellationToken cancellationToken);

    Task<OperationResult<ThemeRuntimeStatus>> GetStatusAsync(
        CancellationToken cancellationToken);

    Task<OperationResult<AgentUpgradeResult>> UpgradeAgentAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<AgentUpgradeResult>.Failure(
            OperationErrorCode.NotImplemented,
            "当前持久化服务不支持升级。",
            "persistence.upgrade.not_supported"));
}
