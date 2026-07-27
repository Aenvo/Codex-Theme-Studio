using System.Text.RegularExpressions;
using CodexThemeStudio.Contracts.Models;

namespace CodexThemeStudio.Application.MacOS;

public sealed record MacPaletteInput(
    string Background,
    string Surface,
    string Foreground,
    string Muted,
    string Accent,
    string Border);

public sealed record MacThemeInput(
    int SchemaVersion,
    Guid ThemeId,
    string Variant,
    MacPaletteInput Palette)
{
    private static readonly Regex ColorPattern = new(
        "^#[0-9A-F]{6}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public bool IsValid =>
        SchemaVersion == 1 &&
        ThemeId != Guid.Empty &&
        Palette is not null &&
        TryParseVariant(Variant, out _) &&
        Colors.All(IsColor);

    internal bool TryCreatePackage(out ThemePackage? package)
    {
        package = null;
        if (!IsValid ||
            !TryParseVariant(Variant, out var variant))
        {
            return false;
        }

        package = new ThemePackage(
            1,
            ThemeId,
            "macOS temporary theme",
            variant,
            new ThemePalette(
                Palette.Background,
                Palette.Surface,
                Palette.Accent,
                Palette.Foreground,
                Palette.Muted,
                Palette.Border),
            new ThemeArt(
                "art/not-applied.png",
                0.5,
                0.5,
                ThemeSafeArea.Auto,
                ThemeArtSize.Cover,
                0,
                0,
                ThemeTaskMode.Hidden,
                0,
                0,
                0));
        return true;
    }

    private IEnumerable<string> Colors =>
    [
        Palette.Background,
        Palette.Surface,
        Palette.Foreground,
        Palette.Muted,
        Palette.Accent,
        Palette.Border,
    ];

    private static bool IsColor(string value) =>
        value is not null && ColorPattern.IsMatch(value);

    private static bool TryParseVariant(
        string value,
        out ThemeVariant variant)
    {
        variant = value switch
        {
            "dark" => ThemeVariant.Dark,
            "light" => ThemeVariant.Light,
            _ => ThemeVariant.Auto,
        };
        return value is "dark" or "light";
    }
}
