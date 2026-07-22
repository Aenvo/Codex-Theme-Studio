using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Contracts.Interfaces;

public interface IStorageConsistencyService
{
    Task<OperationResult<StorageConsistencyReport>> ScanAsync(
        CancellationToken cancellationToken);
}
