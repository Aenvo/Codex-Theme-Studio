using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Contracts.Interfaces;

public interface ICodexTargetSelectionService
{
    Task<OperationResult<CodexTargetStatus>> GetStatusAsync(
        CancellationToken cancellationToken);

    Task<OperationResult<CodexTargetStatus>> SelectAsync(
        string executablePath,
        bool acknowledgeUnverifiedSource,
        CancellationToken cancellationToken);

    Task<OperationResult<CodexTargetStatus>> ResetAsync(
        CancellationToken cancellationToken);
}
