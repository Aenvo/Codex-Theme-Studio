using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Contracts.Interfaces;

public interface IExternalThemeSource
{
    Task<OperationResult<ExternalThemeDescriptor?>> DiscoverCurrentAsync(
        CancellationToken cancellationToken);

    Task<OperationResult<Stream>> OpenImageAsync(
        string sourceIdentifier,
        string expectedSha256,
        CancellationToken cancellationToken);
}
