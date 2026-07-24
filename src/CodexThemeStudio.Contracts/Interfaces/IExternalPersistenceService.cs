using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Contracts.Interfaces;

public interface IExternalPersistenceService
{
    Task<OperationResult<ExternalPersistenceStatus>> GetStatusAsync(
        CancellationToken cancellationToken);

    Task<OperationResult<ExternalPersistenceDisableResult>> DisableAsync(
        CancellationToken cancellationToken);
}
