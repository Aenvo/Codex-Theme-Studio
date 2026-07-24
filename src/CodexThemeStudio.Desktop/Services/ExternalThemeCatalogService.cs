using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;

namespace CodexThemeStudio.Desktop.Services;

public sealed class ExternalThemeCatalogService
{
    private readonly IExternalThemeSource source;
    private readonly IThemeRepository repository;
    private readonly IThemeImageImportService imageImport;

    public ExternalThemeCatalogService(
        IExternalThemeSource source,
        IThemeRepository repository,
        IThemeImageImportService imageImport)
    {
        this.source = source;
        this.repository = repository;
        this.imageImport = imageImport;
    }

    public async Task<ExternalThemeDescriptor?> SynchronizeAsync(
        CancellationToken cancellationToken)
    {
        var discovered = await source.DiscoverCurrentAsync(cancellationToken);
        if (!discovered.IsSuccess || discovered.Value is null)
        {
            return null;
        }

        var descriptor = discovered.Value;
        var themes = await repository.ListAsync(cancellationToken);
        if (!themes.IsSuccess)
        {
            return null;
        }

        var existing = themes.Value!.FirstOrDefault(theme =>
            string.Equals(
                theme.SourceIdentifier,
                descriptor.SourceIdentifier,
                StringComparison.Ordinal));
        if (existing is not null)
        {
            return descriptor with
            {
                Theme = descriptor.Theme with { Id = existing.ThemeId },
            };
        }

        var opened = await source.OpenImageAsync(
            descriptor.SourceIdentifier,
            descriptor.ImageSha256,
            cancellationToken);
        if (!opened.IsSuccess)
        {
            return null;
        }

        await using var image = opened.Value!;
        {
            var imported = await imageImport.ImportNewAsync(
                descriptor.Theme,
                image,
                descriptor.ImageFileName,
                new ThemeCreateOptions(
                    ThemeSourceType.RemoteSnapshot,
                    descriptor.SourceIdentifier,
                    IsSourceReadOnly: true,
                    Tags: ["Doro", "OkkSkin", "外部主题"]),
                cancellationToken);
            return imported.IsSuccess ? descriptor : null;
        }
    }
}
