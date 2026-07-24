using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Contracts.Interfaces;

public interface ICodexDiscoveryService
{
    Task<OperationResult<CodexDiscoverySnapshot>> DiscoverAsync(
        CancellationToken cancellationToken);
}
