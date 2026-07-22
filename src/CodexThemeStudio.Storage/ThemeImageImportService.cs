using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;
using CodexThemeStudio.ThemeCore;

namespace CodexThemeStudio.Storage;

public sealed class ThemeImageImportService : IThemeImageImportService
{
    private readonly IImagePipeline imagePipeline;
    private readonly IThemeRepository themeRepository;
    private readonly TrustedPathResolver pathResolver;

    public ThemeImageImportService(
        string dataRoot,
        IImagePipeline imagePipeline,
        IThemeRepository themeRepository)
    {
        ArgumentNullException.ThrowIfNull(imagePipeline);
        ArgumentNullException.ThrowIfNull(themeRepository);

        pathResolver = new TrustedPathResolver(dataRoot);
        this.imagePipeline = imagePipeline;
        this.themeRepository = themeRepository;
    }

    public async Task<OperationResult<ThemeImageImportResult>> ImportNewAsync(
        ThemePackage theme,
        Stream source,
        string sourceFileName,
        ThemeCreateOptions? createOptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(source);

        var issues = ThemePackageContractValidator.Validate(theme);
        if (issues.Count > 0)
        {
            return OperationResult<ThemeImageImportResult>.Failure(
                OperationErrorCode.ValidationFailed,
                issues[0].UserMessage,
                issues[0].Code);
        }

        var themeDirectoryRelative = StorageLayout.GetThemeDirectory(theme.Id);
        var themeDirectory = pathResolver.Resolve(themeDirectoryRelative);
        if (!themeDirectory.IsSuccess)
        {
            return OperationResult<ThemeImageImportResult>.Failure(
                themeDirectory.Error!);
        }

        if (Directory.Exists(themeDirectory.Value))
        {
            return OperationResult<ThemeImageImportResult>.Failure(
                OperationErrorCode.Conflict,
                "目标主题目录已存在，不能作为新主题导入。",
                "image_import.theme_directory.exists");
        }

        var processed = await imagePipeline.ProcessAsync(
            source,
            sourceFileName,
            theme.Id,
            cancellationToken);
        if (!processed.IsSuccess)
        {
            return OperationResult<ThemeImageImportResult>.Failure(
                processed.Error!);
        }

        var managedTheme = theme with
        {
            Art = theme.Art with
            {
                File = processed.Value!.ThemeArtFileName,
            },
        };
        var options = (createOptions ?? new ThemeCreateOptions()) with
        {
            ThumbnailRelativePath =
                processed.Value.CardThumbnail.RelativePath,
        };

        var saved = await themeRepository.SaveAsync(
            managedTheme,
            options,
            cancellationToken);
        if (!saved.IsSuccess)
        {
            CleanupFailedThemeImport(theme.Id, processed.Value);
            return OperationResult<ThemeImageImportResult>.Failure(saved.Error!);
        }

        return OperationResult<ThemeImageImportResult>.Success(
            new ThemeImageImportResult(managedTheme, processed.Value));
    }

    private void CleanupFailedThemeImport(
        Guid themeId,
        ProcessedImageSet images)
    {
        TryDelete(StorageLayout.GetThemeDocument(themeId));
        if (!images.RuntimeBackground.WasReused)
        {
            TryDelete(images.RuntimeBackground.RelativePath);
        }

        var themeDirectory = pathResolver.Resolve(
            StorageLayout.GetThemeDirectory(themeId));
        if (!themeDirectory.IsSuccess ||
            !Directory.Exists(themeDirectory.Value))
        {
            return;
        }

        try
        {
            if (!Directory.EnumerateFileSystemEntries(themeDirectory.Value!).Any())
            {
                Directory.Delete(themeDirectory.Value!);
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }

    private void TryDelete(string relativePath)
    {
        var resolved = pathResolver.Resolve(relativePath);
        if (!resolved.IsSuccess)
        {
            return;
        }

        try
        {
            if (File.Exists(resolved.Value))
            {
                File.Delete(resolved.Value!);
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }
}
