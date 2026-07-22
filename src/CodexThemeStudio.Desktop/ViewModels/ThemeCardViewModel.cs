using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Desktop.Infrastructure;

namespace CodexThemeStudio.Desktop.ViewModels;

public sealed class ThemeCardViewModel : ObservableObject
{
    private bool isTemporary;
    private bool isPersistent;

    public ThemeCardViewModel(ThemeSummary summary, string? thumbnailPath)
    {
        Summary = summary;
        ThumbnailPath = thumbnailPath;
    }

    public ThemeSummary Summary { get; private set; }

    public Guid ThemeId => Summary.ThemeId;

    public string DisplayName => Summary.DisplayName;

    public string? ThumbnailPath { get; }

    public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailPath);

    public bool IsFavorite => Summary.IsFavorite;

    public IReadOnlyList<string> Tags => Summary.Tags;

    public string TagsText => Tags.Count == 0 ? "未添加标签" : string.Join(" · ", Tags);

    public bool IsPersistent
    {
        get => isPersistent;
        set => SetProperty(ref isPersistent, value);
    }

    public bool IsTemporary
    {
        get => isTemporary;
        set => SetProperty(ref isTemporary, value);
    }

    public string CompatibilityText => Summary.CompatibilityStatus switch
    {
        ThemeCompatibilityStatus.Compatible => "兼容",
        ThemeCompatibilityStatus.Incompatible => "不兼容",
        _ => "待验证",
    };

    public string LastUsedText => Summary.LastUsedAtUtc is { } value
        ? $"上次使用 {value.ToLocalTime():yyyy-MM-dd HH:mm}"
        : "尚未应用";
}
