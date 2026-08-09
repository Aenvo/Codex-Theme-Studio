using System.IO;
using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;
using CodexThemeStudio.Desktop.Infrastructure;
using CodexThemeStudio.Desktop.Services;
using CodexThemeStudio.Storage;
using CodexThemeStudio.ThemeCore;

namespace CodexThemeStudio.Desktop.ViewModels;

public sealed class ThemeEditorViewModel : ObservableObject
{
    private const double PanelOpacityReductionPerPixel = 0.0028;
    private static readonly ThemePalette DefaultPalette = new(
        "#111111",
        "#1C1C1CE6",
        "#3B82F6",
        "#F5F5F5",
        "#A3A3A3",
        "#30303080");

    private readonly IThemeRepository repository;
    private readonly IImagePipeline imagePipeline;
    private readonly IThemeAssetStore assetStore;
    private readonly Func<string, string?> resolveDataPath;
    private readonly IColorHistoryService colorHistory;
    private ThemeDraft? draft;
    private bool isNew;
    private string? previewImagePath;
    private string? thumbnailRelativePath;
    private bool isTaskPreview;
    private bool isColorPickerOpen;
    private readonly HashSet<string> generatedAssetFileNames = new(StringComparer.OrdinalIgnoreCase);
    private string? originalArtFileName;

    public ThemeEditorViewModel(
        IThemeRepository repository,
        IImagePipeline imagePipeline,
        IThemeAssetStore assetStore,
        Func<string, string?> resolveDataPath,
        IColorHistoryService? colorHistory = null)
    {
        this.repository = repository;
        this.imagePipeline = imagePipeline;
        this.assetStore = assetStore;
        this.resolveDataPath = resolveDataPath;
        this.colorHistory = colorHistory ?? NullColorHistoryService.Instance;
        ResetDefaultsCommand = new RelayCommand(_ => ResetDefaults());
        ShowHomePreviewCommand = new RelayCommand(_ => IsTaskPreview = false);
        ShowTaskPreviewCommand = new RelayCommand(_ => IsTaskPreview = true);
    }

    public IReadOnlyList<ThemeArtSize> ArtSizes { get; } = Enum.GetValues<ThemeArtSize>();

    public IReadOnlyList<ThemeTaskMode> TaskModes { get; } = Enum.GetValues<ThemeTaskMode>();

    public IColorHistoryService ColorHistoryService => colorHistory;

    public IEnumerable<string> ColorHistory => colorHistory.Colors;

    public bool IsColorPickerOpen
    {
        get => isColorPickerOpen;
        set => SetProperty(ref isColorPickerOpen, value);
    }

    public RelayCommand ResetDefaultsCommand { get; }

    public RelayCommand ShowHomePreviewCommand { get; }

    public RelayCommand ShowTaskPreviewCommand { get; }

    public bool HasDraft => draft is not null;

    public bool IsNew => isNew;

    public Guid ThemeId => draft?.Id ?? Guid.Empty;

    public string Name
    {
        get => draft?.Name ?? string.Empty;
        set
        {
            if (draft is not null && draft.Name != value)
            {
                draft.Name = value;
                OnPropertyChanged();
            }
        }
    }

    public string BackgroundColor
    {
        get => draft?.Palette.Background ?? DefaultPalette.Background;
        set => SetPalette(draft is null ? null : draft.Palette with { Background = value });
    }

    public string PanelColor
    {
        get => draft?.Palette.Panel ?? DefaultPalette.Panel;
        set => SetPalette(draft is null ? null : draft.Palette with { Panel = value });
    }

    public string AccentColor
    {
        get => draft?.Palette.Accent ?? DefaultPalette.Accent;
        set => SetPalette(draft is null ? null : draft.Palette with { Accent = value });
    }

    public string TextColor
    {
        get => draft?.Palette.Text ?? DefaultPalette.Text;
        set => SetPalette(draft is null ? null : draft.Palette with { Text = value });
    }

    public string MutedColor
    {
        get => draft?.Palette.Muted ?? DefaultPalette.Muted;
        set => SetPalette(draft is null ? null : draft.Palette with { Muted = value });
    }

    public string BorderColor
    {
        get => draft?.Palette.Border ?? DefaultPalette.Border;
        set => SetPalette(draft is null ? null : draft.Palette with { Border = value });
    }

    public IReadOnlyList<string> ThemePaletteColors =>
        draft is null
            ?
            [
                DefaultPalette.Background,
                DefaultPalette.Panel,
                DefaultPalette.Accent,
                DefaultPalette.Text,
                DefaultPalette.Muted,
                DefaultPalette.Border,
            ]
            :
            [
                draft.Palette.Background,
                draft.Palette.Panel,
                draft.Palette.Accent,
                draft.Palette.Text,
                draft.Palette.Muted,
                draft.Palette.Border,
            ];

    public double FocusX
    {
        get => draft?.Art.FocusX ?? 0.5;
        set => SetArt(draft is null ? null : draft.Art with { FocusX = Math.Clamp(value, 0, 1) });
    }

    public double FocusY
    {
        get => draft?.Art.FocusY ?? 0.5;
        set => SetArt(draft is null ? null : draft.Art with { FocusY = Math.Clamp(value, 0, 1) });
    }

    public ThemeArtSize ArtSize
    {
        get => draft?.Art.Size ?? ThemeArtSize.Cover;
        set => SetArt(draft is null ? null : draft.Art with { Size = value });
    }

    public bool IsCropMode => ArtSize == ThemeArtSize.Crop;

    public double CropScale
    {
        get => draft?.Art.CropScale ?? 1;
        set => SetArt(draft is null
            ? null
            : draft.Art with
            {
                CropScale = Math.Clamp(value, 1, ThemePackageContractValidator.MaximumCropScale),
            });
    }

    public double HomeOpacity
    {
        get => draft?.Art.HomeOpacity ?? 0.72;
        set => SetArt(draft is null ? null : draft.Art with { HomeOpacity = Math.Clamp(value, 0, 1) });
    }

    public double HomeOverlay
    {
        get => draft?.Art.HomeOverlay ?? 0.28;
        set => SetArt(draft is null ? null : draft.Art with { HomeOverlay = Math.Clamp(value, 0, 1) });
    }

    public ThemeTaskMode TaskMode
    {
        get => draft?.Art.TaskMode ?? ThemeTaskMode.Ambient;
        set => SetArt(draft is null ? null : draft.Art with { TaskMode = value });
    }

    public double TaskOpacity
    {
        get => draft?.Art.TaskOpacity ?? 0.22;
        set => SetArt(draft is null ? null : draft.Art with { TaskOpacity = Math.Clamp(value, 0, 1) });
    }

    public double TaskOverlay
    {
        get => draft?.Art.TaskOverlay ?? 0.62;
        set => SetArt(draft is null ? null : draft.Art with { TaskOverlay = Math.Clamp(value, 0, 1) });
    }

    public double Blur
    {
        get => draft?.Art.Blur ?? 0;
        set => SetArt(draft is null ? null : draft.Art with { Blur = Math.Clamp(value, 0, 64) });
    }

    public double PanelBlur
    {
        get => draft?.Art.PanelBlur ?? 0;
        set => SetArt(draft is null ? null : draft.Art with { PanelBlur = Math.Clamp(value, 0, 64) });
    }

    public double PanelSurfaceOpacity => Math.Clamp(
        1 - (PanelBlur * PanelOpacityReductionPerPixel),
        0,
        1);

    public string? PreviewImagePath
    {
        get => previewImagePath;
        private set
        {
            if (SetProperty(ref previewImagePath, value))
            {
                OnPropertyChanged(nameof(HasPreviewImage));
            }
        }
    }

    public bool HasPreviewImage => !string.IsNullOrWhiteSpace(PreviewImagePath);

    public bool IsTaskPreview
    {
        get => isTaskPreview;
        set
        {
            if (SetProperty(ref isTaskPreview, value))
            {
                OnPropertyChanged(nameof(PreviewTitle));
                OnPropertyChanged(nameof(PreviewSubtitle));
                OnPropertyChanged(nameof(PreviewOpacity));
                OnPropertyChanged(nameof(PreviewOverlay));
                OnPropertyChanged(nameof(TaskContentOverlay));
            }
        }
    }

    public string PreviewTitle => IsTaskPreview ? "示例任务" : "今天想做什么？";

    public string PreviewSubtitle =>
        IsTaskPreview ? "这是模拟任务内容，不包含真实对话。" : "选择一个本地主题开始。";

    public double PreviewOpacity => IsTaskPreview ? TaskOpacity : HomeOpacity;

    public double PreviewOverlay => IsTaskPreview ? 0 : HomeOverlay;

    public double TaskContentOverlay => !IsTaskPreview
        ? 0
        : TaskMode == ThemeTaskMode.Hidden
            ? 1
            : TaskOverlay;

    public bool IsTaskOverlayEnabled => TaskMode != ThemeTaskMode.Hidden;

    public void Begin(ThemePackage theme, bool newTheme, string? thumbnailPath = null)
    {
        draft = new ThemeDraft(theme);
        isNew = newTheme;
        originalArtFileName = theme.Art.File;
        generatedAssetFileNames.Clear();
        IsTaskPreview = false;
        thumbnailRelativePath = null;
        PreviewImagePath = thumbnailPath ??
            resolveDataPath(
                $"{StorageLayout.GetThemeDirectory(theme.Id)}/{theme.Art.File}");
        NotifyAll();
    }

    public async Task<OperationResult> SelectImageAsync(
        string sourceFile,
        CancellationToken cancellationToken)
    {
        var currentDraft = draft;
        if (currentDraft is null || !File.Exists(sourceFile))
        {
            return OperationResult.Failure(
                OperationErrorCode.InvalidPath,
                "请选择存在的 PNG、JPEG 或 WebP 图片。",
                "theme_editor.image.path_invalid");
        }

        await using var stream = new FileStream(
            sourceFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var processed = await imagePipeline.ProcessAsync(
            stream,
            Path.GetFileName(sourceFile),
            currentDraft.Id,
            cancellationToken);
        if (!processed.IsSuccess)
        {
            return OperationResult.Failure(processed.Error!);
        }

        currentDraft.Art = currentDraft.Art with
        {
            File = processed.Value!.ThemeArtFileName,
            TaskMode = processed.Value.SuggestedTaskMode switch
            {
                ImageTaskModeSuggestion.Off => ThemeTaskMode.Hidden,
                ImageTaskModeSuggestion.Banner => ThemeTaskMode.Full,
                _ => ThemeTaskMode.Ambient,
            },
        };
        if (!processed.Value.RuntimeBackground.WasReused)
        {
            generatedAssetFileNames.Add(processed.Value.ThemeArtFileName);
        }
        thumbnailRelativePath = processed.Value.CardThumbnail.RelativePath;
        PreviewImagePath = resolveDataPath(processed.Value.EditorPreview.RelativePath);
        NotifyArt();
        return OperationResult.Success();
    }

    public async Task<OperationResult<ThemePackage>> SaveAsync(
        bool saveCopy,
        string? copyName,
        CancellationToken cancellationToken)
    {
        if (draft is null)
        {
            return OperationResult<ThemePackage>.Failure(
                OperationErrorCode.Conflict,
                "当前没有可保存的主题草稿。",
                "theme_editor.draft_missing");
        }

        var theme = draft.Build();
        if (saveCopy)
        {
            var newId = Guid.NewGuid();
            var sourceRelative =
                $"{StorageLayout.GetThemeDirectory(theme.Id)}/{theme.Art.File}";
            var source = await assetStore.OpenReadAsync(sourceRelative, cancellationToken);
            if (!source.IsSuccess)
            {
                return OperationResult<ThemePackage>.Failure(source.Error!);
            }

            await using (source.Value!)
            {
                var savedAsset = await assetStore.SaveAsync(
                    newId,
                    theme.Art.File,
                    source.Value!,
                    cancellationToken);
                if (!savedAsset.IsSuccess)
                {
                    return OperationResult<ThemePackage>.Failure(savedAsset.Error!);
                }
            }

            theme = theme with
            {
                Id = newId,
                Name = string.IsNullOrWhiteSpace(copyName)
                    ? $"{theme.Name} 副本"
                    : copyName.Trim(),
            };
        }

        var saved = await repository.SaveAsync(
            theme,
            new ThemeCreateOptions(
                Tags: isNew ? ["自制"] : null,
                ThumbnailRelativePath: thumbnailRelativePath),
            cancellationToken);
        if (!saved.IsSuccess)
        {
            if (saveCopy)
            {
                await assetStore.DeleteGeneratedAssetsAsync(
                    theme.Id,
                    [theme.Art.File],
                    deleteEmptyThemeDirectory: true,
                    cancellationToken);
            }

            return OperationResult<ThemePackage>.Failure(saved.Error!);
        }

        if (!saveCopy)
        {
            var obsolete = generatedAssetFileNames
                .Where(fileName => !string.Equals(fileName, theme.Art.File, StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!string.Equals(originalArtFileName, theme.Art.File, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(originalArtFileName))
            {
                obsolete.Add(originalArtFileName);
            }

            if (obsolete.Count > 0)
            {
                var cleanup = await assetStore.DeleteGeneratedAssetsAsync(
                    theme.Id,
                    obsolete,
                    deleteEmptyThemeDirectory: false,
                    cancellationToken);
                if (!cleanup.IsSuccess)
                {
                    return OperationResult<ThemePackage>.Failure(cleanup.Error!);
                }
            }
        }

        generatedAssetFileNames.Clear();
        return OperationResult<ThemePackage>.Success(theme);
    }

    public void ResetDefaults()
    {
        if (draft is null)
        {
            return;
        }

        draft.Variant = ThemeVariant.Auto;
        draft.Palette = DefaultPalette;
        draft.Art = draft.Art with
        {
            FocusX = 0.5,
            FocusY = 0.5,
            SafeArea = ThemeSafeArea.None,
            Size = ThemeArtSize.Cover,
            HomeOpacity = 0.72,
            HomeOverlay = 0.28,
            TaskMode = ThemeTaskMode.Ambient,
            TaskOpacity = 0.22,
            TaskOverlay = 0.62,
            Blur = 0,
            PanelBlur = 10,
            CropScale = 1,
        };
        NotifyAll();
    }

    public async Task<OperationResult> CancelAsync(CancellationToken cancellationToken)
    {
        var currentDraft = draft;
        if (currentDraft is not null)
        {
            var cleanup = await assetStore.DeleteGeneratedAssetsAsync(
                currentDraft.Id,
                generatedAssetFileNames,
                deleteEmptyThemeDirectory: isNew,
                cancellationToken);
            if (!cleanup.IsSuccess)
            {
                return cleanup;
            }
        }

        Cancel();
        return OperationResult.Success();
    }

    public void Cancel()
    {
        draft = null;
        PreviewImagePath = null;
        thumbnailRelativePath = null;
        originalArtFileName = null;
        generatedAssetFileNames.Clear();
        OnPropertyChanged(nameof(HasDraft));
    }

    private void SetPalette(ThemePalette? value)
    {
        if (draft is null || value is null || draft.Palette == value)
        {
            return;
        }

        draft.Palette = value;
        NotifyPalette();
    }

    private void SetArt(ThemeArt? value)
    {
        if (draft is null || value is null || draft.Art == value)
        {
            return;
        }

        draft.Art = value;
        NotifyArt();
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(HasDraft));
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(ThemeId));
        OnPropertyChanged(nameof(Name));
        NotifyPalette();
        NotifyArt();
    }

    private void NotifyPalette()
    {
        OnPropertyChanged(nameof(BackgroundColor));
        OnPropertyChanged(nameof(PanelColor));
        OnPropertyChanged(nameof(AccentColor));
        OnPropertyChanged(nameof(TextColor));
        OnPropertyChanged(nameof(MutedColor));
        OnPropertyChanged(nameof(BorderColor));
        OnPropertyChanged(nameof(ThemePaletteColors));
    }

    private void NotifyArt()
    {
        OnPropertyChanged(nameof(FocusX));
        OnPropertyChanged(nameof(FocusY));
        OnPropertyChanged(nameof(ArtSize));
        OnPropertyChanged(nameof(IsCropMode));
        OnPropertyChanged(nameof(CropScale));
        OnPropertyChanged(nameof(HomeOpacity));
        OnPropertyChanged(nameof(HomeOverlay));
        OnPropertyChanged(nameof(TaskMode));
        OnPropertyChanged(nameof(TaskOpacity));
        OnPropertyChanged(nameof(TaskOverlay));
        OnPropertyChanged(nameof(Blur));
        OnPropertyChanged(nameof(PanelBlur));
        OnPropertyChanged(nameof(PanelSurfaceOpacity));
        OnPropertyChanged(nameof(PreviewOpacity));
        OnPropertyChanged(nameof(PreviewOverlay));
        OnPropertyChanged(nameof(TaskContentOverlay));
        OnPropertyChanged(nameof(IsTaskOverlayEnabled));
    }
}
