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
}
