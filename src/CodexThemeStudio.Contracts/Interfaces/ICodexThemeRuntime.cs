using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Contracts.Interfaces;

public interface ICodexThemeRuntime
{
    Task<OperationResult<ThemeRuntimeStatus>> ApplyTemporaryAsync(
        ThemePackage theme,
        CancellationToken cancellationToken);

    Task<OperationResult<ThemeRuntimeStatus>> SwitchTemporaryAsync(
        ThemePackage theme,
        CancellationToken cancellationToken);

    Task<OperationResult<ThemeRuntimeStatus>> RestoreAsync(
        CancellationToken cancellationToken);

    Task<OperationResult<ThemeRuntimeStatus>> GetStatusAsync(
        CancellationToken cancellationToken);

    Task<OperationResult<ThemeRuntimeStatus>> GetStatusAsync(
        CodexStatusRefreshMode refreshMode,
        CancellationToken cancellationToken);

    Task<OperationResult<CodexCachedCompatibilityStatus?>> GetCachedCompatibilityAsync(
        CancellationToken cancellationToken);
}
