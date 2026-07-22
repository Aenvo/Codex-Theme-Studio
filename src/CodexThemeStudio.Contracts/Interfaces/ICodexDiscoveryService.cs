using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Contracts.Interfaces;

public interface ICodexDiscoveryService
{
    Task<OperationResult<CodexInstallationInfo>> FindInstallationAsync(
        CancellationToken cancellationToken);

    Task<OperationResult<IReadOnlyList<CodexProcessInfo>>> FindProcessesAsync(
        CodexInstallationInfo installation,
        CancellationToken cancellationToken);
}

