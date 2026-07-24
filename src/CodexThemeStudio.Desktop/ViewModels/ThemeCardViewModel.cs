using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Desktop.Infrastructure;

namespace CodexThemeStudio.Desktop.ViewModels;

public sealed class ThemeCardViewModel : ObservableObject
{
    private bool isTemporary;
    private bool isPersistent;
    private bool isExternalPersistent;
    private bool isExternalActive;

    public ThemeCardViewModel(
        ThemeSummary summary,
        string? thumbnailPath,
        ThemePalette? palette = null)
    {
        Summary = summary;
        ThumbnailPath = thumbnailPath;
        PaletteColors = palette is null
            ? []
            :
            [
                palette.Background,
                palette.Panel,
                palette.Accent,
                palette.Text,
                palette.Muted,
                palette.Border,
            ];
    }

    public ThemeSummary Summary { get; private set; }

    public Guid ThemeId => Summary.ThemeId;

    public string DisplayName => Summary.DisplayName;

    public string? ThumbnailPath { get; }

    public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailPath);

    public IReadOnlyList<string> PaletteColors { get; }

    public bool HasPalette => PaletteColors.Count > 0;

    public string SourceText => Summary.SourceType switch
    {
        ThemeSourceType.RemoteSnapshot => "OkkSkin 外部主题",
        ThemeSourceType.Imported => "导入主题",
        _ => "本地主题",
    };

    public bool IsFavorite => Summary.IsFavorite;

    public IReadOnlyList<string> Tags => Summary.Tags;

    public string TagsText => Tags.Count == 0 ? "未添加标签" : string.Join(" · ", Tags);

    public string CardSubtitleText => Summary.SourceType == ThemeSourceType.RemoteSnapshot
        ? TagsText
        : "本地主题";

    public bool IsPersistent
    {
        get => isPersistent;
        set
        {
            if (SetProperty(ref isPersistent, value))
            {
                OnPropertyChanged(nameof(ShowPersistentStatus));
            }
        }
    }

    public bool IsTemporary
    {
        get => isTemporary;
        set
        {
            if (SetProperty(ref isTemporary, value))
            {
                OnPropertyChanged(nameof(ShowTemporaryStatus));
            }
        }
    }

    public bool IsExternalPersistent
    {
        get => isExternalPersistent;
        set
        {
            if (SetProperty(ref isExternalPersistent, value))
            {
                OnPropertyChanged(nameof(ExternalRuntimeText));
                OnPropertyChanged(nameof(ShowPersistentStatus));
                OnPropertyChanged(nameof(ShowTemporaryStatus));
            }
        }
    }

    public bool IsExternalActive
    {
        get => isExternalActive;
        set
        {
            if (SetProperty(ref isExternalActive, value))
            {
                OnPropertyChanged(nameof(ExternalRuntimeText));
            }
        }
    }

    public string ExternalRuntimeText => IsExternalActive
        ? "当前外部生效"
        : "重启后由 OkkSkin 应用";

    public bool ShowPersistentStatus => IsPersistent && !IsExternalPersistent;

    public bool ShowTemporaryStatus => IsTemporary && !IsExternalPersistent && !IsPersistent;

    public string LastUsedText => Summary.LastUsedAtUtc is { } value
        ? $"上次使用 {value.ToLocalTime():yyyy-MM-dd HH:mm}"
        : "尚未应用";
}
