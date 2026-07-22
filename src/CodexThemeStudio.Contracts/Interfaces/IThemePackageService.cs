using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Contracts.Interfaces;

public interface IThemePackageService
{
    Task<OperationResult<string>> ExportAsync(
        Guid themeId,
        string destinationFile,
        CancellationToken cancellationToken);

    Task<OperationResult<ThemePackageImportResult>> ImportAsync(
        string packageFile,
        CancellationToken cancellationToken);
}
