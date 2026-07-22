using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Contracts.Interfaces;

public interface IApplicationStatusService
{
    Task<OperationResult<ApplicationStatus>> GetStatusAsync(
        CancellationToken cancellationToken);
}

