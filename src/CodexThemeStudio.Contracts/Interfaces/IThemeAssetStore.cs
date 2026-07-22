using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Contracts.Interfaces;

public interface IThemeAssetStore
{
    Task<OperationResult<string>> SaveAsync(
        Guid themeId,
        string fileName,
        Stream content,
        CancellationToken cancellationToken);

    Task<OperationResult<Stream>> OpenReadAsync(
        string relativePath,
        CancellationToken cancellationToken);
}

