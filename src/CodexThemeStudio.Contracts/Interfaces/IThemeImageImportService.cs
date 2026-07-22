using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Contracts.Interfaces;

public interface IThemeImageImportService
{
    Task<OperationResult<ThemeImageImportResult>> ImportNewAsync(
        ThemePackage theme,
        Stream source,
        string sourceFileName,
        ThemeCreateOptions? createOptions,
        CancellationToken cancellationToken);
}
