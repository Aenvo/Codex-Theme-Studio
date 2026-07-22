namespace CodexThemeStudio.Contracts.Models;

public enum ImageSourceFormat
{
    Png = 0,
    Jpeg,
    WebP,
}

public enum ImageAspectCategory
{
    UltraWide = 0,
    Widescreen,
    Landscape,
    Square,
    Portrait,
}

public enum ImageTaskModeSuggestion
{
    Ambient = 0,
    Banner,
    Off,
}

public enum ProcessedImageKind
{
    RuntimeBackground = 0,
    EditorPreview,
    CardThumbnail,
}

public sealed record ProcessedImageAsset(
    ProcessedImageKind Kind,
    string RelativePath,
    string ContentType,
    long Length,
    int PixelWidth,
    int PixelHeight,
    string Sha256,
    bool WasReused);

public sealed record ProcessedImageSet(
    Guid ThemeId,
    ImageSourceFormat SourceFormat,
    string SourceSha256,
    bool OrientationWasApplied,
    ImageAspectCategory AspectCategory,
    ImageTaskModeSuggestion SuggestedTaskMode,
    string ThemeArtFileName,
    ProcessedImageAsset RuntimeBackground,
    ProcessedImageAsset EditorPreview,
    ProcessedImageAsset CardThumbnail,
    bool IsDuplicateCandidate);

public sealed record ThemeImageImportResult(
    ThemePackage Theme,
    ProcessedImageSet Images);
