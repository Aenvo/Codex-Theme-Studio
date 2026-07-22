using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Contracts.Interfaces;

public interface IImagePipeline
{
    Task<OperationResult<ProcessedImageSet>> ProcessAsync(
        Stream source,
        string sourceFileName,
        Guid themeId,
        CancellationToken cancellationToken);
}
