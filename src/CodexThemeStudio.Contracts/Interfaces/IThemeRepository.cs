using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Contracts.Interfaces;

public interface IThemeRepository
{
    Task<OperationResult<IReadOnlyList<ThemeSummary>>> ListAsync(
        CancellationToken cancellationToken);

    Task<OperationResult<IReadOnlyList<ThemeSummary>>> ListDeletedAsync(
        CancellationToken cancellationToken);

    Task<OperationResult<ThemePackage>> GetAsync(
        Guid themeId,
        CancellationToken cancellationToken);

    Task<OperationResult> SaveAsync(
        ThemePackage theme,
        ThemeCreateOptions? createOptions,
        CancellationToken cancellationToken);

    Task<OperationResult<ThemePackage>> CopyAsync(
        Guid sourceThemeId,
        Guid newThemeId,
        string newName,
        CancellationToken cancellationToken);

    Task<OperationResult> RenameAsync(
        Guid themeId,
        string newName,
        CancellationToken cancellationToken);

    Task<OperationResult> SetFavoriteAsync(
        Guid themeId,
        bool isFavorite,
        CancellationToken cancellationToken);

    Task<OperationResult> SetSortOrderAsync(
        Guid themeId,
        int sortOrder,
        CancellationToken cancellationToken);

    Task<OperationResult> SetTagsAsync(
        Guid themeId,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken);

    Task<OperationResult> SetCurrentPersistentAsync(
        Guid? themeId,
        CancellationToken cancellationToken);

    Task<OperationResult> RecordApplyResultAsync(
        Guid themeId,
        ThemeApplyResult result,
        string? userMessage,
        CancellationToken cancellationToken);

    Task<OperationResult> DeleteAsync(
        Guid themeId,
        CancellationToken cancellationToken);

    Task<OperationResult> RestoreDeletedAsync(
        Guid themeId,
        CancellationToken cancellationToken);

    Task<OperationResult> PermanentlyDeleteAsync(
        Guid themeId,
        CancellationToken cancellationToken);
}
