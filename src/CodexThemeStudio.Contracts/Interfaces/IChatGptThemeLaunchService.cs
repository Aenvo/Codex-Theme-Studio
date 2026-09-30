using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Contracts.Interfaces;

public interface IChatGptThemeLaunchService
{
    Task<OperationResult<CodexProcessInfo>> RestartOrLaunchOfficialAsync(
        TimeSpan? maximumExistingProcessAge,
        CancellationToken cancellationToken);
}
