using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Desktop.MacOS.Infrastructure;
using CodexThemeStudio.Desktop.MacOS.Services;

namespace CodexThemeStudio.Desktop.MacOS.ViewModels;

public sealed class ThemeCardViewModel : ObservableObject
{
    private bool isFavorite;
    private bool isSelected;
    private string displayName;
    private ThemePalette palette;
    private Uri? localPreviewAssetUri;
    private ThemeEditorState? editorState;

    public ThemeCardViewModel(
        MacThemeLibraryItem source,
        Action<ThemeCardViewModel> select,
        Action<ThemeCardViewModel> toggleFavorite)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(select);
        ArgumentNullException.ThrowIfNull(toggleFavorite);
        ThemeId = source.ThemeId;
        displayName = source.DisplayName;
        Subtitle = "本地主题";
        TagItems = source.Tags;
        Tags = string.Join(" · ", source.Tags);
        palette = source.Palette;
        isFavorite = source.IsFavorite;
        LastUsedAtUtc = source.LastUsedAtUtc;
        HasLocalBackgroundPreview = source.HasLocalBackgroundPreview;
        localPreviewAssetUri = source.LocalPreviewAssetUri;
        BackgroundCapabilityLabel = source.HasLocalBackgroundPreview
            ? "仅本地预览 · macOS 当前不应用背景"
            : "纯调色板主题";
        LastUsedLabel = source.LastUsedAtUtc is null
            ? "尚未使用"
            : $"最近使用 · {FormatRelativeTime(source.LastUsedAtUtc.Value)}";
        SelectCommand = new RelayCommand(() => select(this));
        ToggleFavoriteCommand = new RelayCommand(
            () => toggleFavorite(this));
    }

    public Guid ThemeId { get; }

    public string DisplayName => displayName;

    public string Subtitle { get; }

    public string Tags { get; }

    public IReadOnlyList<string> TagItems { get; }

    public ThemePalette Palette => palette;

    public IReadOnlyList<string> PaletteColors =>
        [Palette.Background, Palette.Panel, Palette.Accent,
         Palette.Text, Palette.Muted, Palette.Border];

    public bool IsFavorite
    {
        get => isFavorite;
        internal set
        {
            if (SetProperty(ref isFavorite, value))
            {
                OnPropertyChanged(nameof(ShowFavoriteButton));
                OnPropertyChanged(nameof(IsNotFavorite));
            }
        }
    }

    public bool IsNotFavorite => !IsFavorite;

    public DateTimeOffset? LastUsedAtUtc { get; }

    public bool HasLocalBackgroundPreview { get; }

    public Uri? LocalPreviewAssetUri => localPreviewAssetUri;

    public ThemeEditorState? EditorState => editorState;

    public string BackgroundCapabilityLabel { get; }

    public string LastUsedLabel { get; }

    public RelayCommand SelectCommand { get; }

    public RelayCommand ToggleFavoriteCommand { get; }

    public bool ShowFavoriteButton => IsFavorite || IsSelected;

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (SetProperty(ref isSelected, value))
            {
                OnPropertyChanged(nameof(ShowFavoriteButton));
            }
        }
    }

    public void ApplyEditor(
        ThemeEditorViewModel editor,
        string? displayNameOverride = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        var requestedName = displayNameOverride ?? editor.Name;
        displayName = string.IsNullOrWhiteSpace(requestedName)
            ? "未命名主题"
            : requestedName.Trim();
        palette = editor.BuildPalette();
        localPreviewAssetUri = editor.LocalPreviewAssetUri;
        editorState = editor.CreateState();
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Palette));
        OnPropertyChanged(nameof(PaletteColors));
        OnPropertyChanged(nameof(LocalPreviewAssetUri));
    }

    public MacThemeLibraryItem CreateCopy(
        string copyName,
        ThemeEditorViewModel editor) => new(
        Guid.NewGuid(),
        string.IsNullOrWhiteSpace(copyName) ? $"{DisplayName} 副本" : copyName.Trim(),
        Subtitle,
        TagItems,
        editor.BuildPalette(),
        false,
        DateTimeOffset.UtcNow,
        editor.LocalPreviewAssetUri is not null,
        editor.LocalPreviewAssetUri);

    private static string FormatRelativeTime(DateTimeOffset value)
    {
        var days = Math.Max(
            0,
            (int)Math.Floor((DateTimeOffset.UtcNow - value).TotalDays));
        return days switch
        {
            0 => "今天",
            1 => "昨天",
            _ => $"{days} 天前",
        };
    }

}
