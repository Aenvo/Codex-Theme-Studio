using CodexThemeStudio.Contracts.Models;

namespace CodexThemeStudio.ThemeCore;

public sealed class ThemeDraft
{
    public ThemeDraft(ThemePackage source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Original = source;
        Id = source.Id;
        Name = source.Name;
        Variant = source.Variant;
        Palette = source.Palette;
        Art = source.Art;
    }

    public ThemePackage Original { get; }

    public Guid Id { get; set; }

    public string Name { get; set; }

    public ThemeVariant Variant { get; set; }

    public ThemePalette Palette { get; set; }

    public ThemeArt Art { get; set; }

    public ThemePackage Build() =>
        new(ThemePackageContractValidator.CurrentSchemaVersion, Id, Name, Variant, Palette, Art);

    public void Reset()
    {
        Id = Original.Id;
        Name = Original.Name;
        Variant = Original.Variant;
        Palette = Original.Palette;
        Art = Original.Art;
    }

    public ThemePackage BuildCopy(Guid id, string name) =>
        Build() with { Id = id, Name = name };
}
