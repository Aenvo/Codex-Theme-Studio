using System.Collections.ObjectModel;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Desktop.MacOS.Infrastructure;

namespace CodexThemeStudio.Desktop.MacOS.ViewModels;

public sealed record ThemeEditorState(
    string Name,
    string BackgroundColor,
    string PanelColor,
    string AccentColor,
    string TextColor,
    string MutedColor,
    string BorderColor,
    string ArtSize,
    string TaskMode,
    double CropScale,
    double FocusX,
    double FocusY,
    double HomeOpacity,
    double HomeOverlay,
    double TaskOpacity,
    double TaskOverlay,
    double Blur,
    double PanelBlur,
    Uri? LocalPreviewAssetUri);

public sealed class ThemeEditorViewModel : ObservableObject
{
    public const string ArtCover = "铺满（cover）";
    public const string ArtContain = "完整显示（contain）";
    public const string ArtCrop = "裁切（crop）";
    public const string TaskAmbient = "弱化背景";
    public const string TaskHidden = "隐藏背景";
    public const string TaskFull = "完整背景";

    private string name;
    private string backgroundColor;
    private string panelColor;
    private string accentColor;
    private string textColor;
    private string mutedColor;
    private string borderColor;
    private string artSize = ArtCover;
    private string taskMode = TaskAmbient;
    private double cropScale = 1;
    private double focusX = 0.5;
    private double focusY = 0.5;
    private double homeOpacity = 0.72;
    private double homeOverlay = 0.28;
    private double taskOpacity = 0.22;
    private double taskOverlay = 0.62;
    private double blur;
    private double panelBlur = 10;
    private Uri? localPreviewAssetUri;
    private bool isTaskPreview;
    private bool isDirty;
    private readonly ObservableCollection<string> colorHistory = [];

    public ThemeEditorViewModel(ThemeCardViewModel source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ColorHistory = new ReadOnlyObservableCollection<string>(colorHistory);
        ThemeId = source.ThemeId;
        var state = source.EditorState;
        name = state?.Name ?? source.DisplayName;
        backgroundColor = state?.BackgroundColor ?? source.Palette.Background;
        panelColor = state?.PanelColor ?? source.Palette.Panel;
        accentColor = state?.AccentColor ?? source.Palette.Accent;
        textColor = state?.TextColor ?? source.Palette.Text;
        mutedColor = state?.MutedColor ?? source.Palette.Muted;
        borderColor = state?.BorderColor ?? source.Palette.Border;
        artSize = state?.ArtSize ?? ArtCover;
        taskMode = state?.TaskMode ?? TaskAmbient;
        cropScale = state?.CropScale ?? 1;
        focusX = state?.FocusX ?? 0.5;
        focusY = state?.FocusY ?? 0.5;
        homeOpacity = state?.HomeOpacity ?? 0.72;
        homeOverlay = state?.HomeOverlay ?? 0.28;
        taskOpacity = state?.TaskOpacity ?? 0.22;
        taskOverlay = state?.TaskOverlay ?? 0.62;
        blur = state?.Blur ?? 0;
        panelBlur = state?.PanelBlur ?? 10;
        localPreviewAssetUri = state?.LocalPreviewAssetUri ?? source.LocalPreviewAssetUri;
        ShowHomePreviewCommand = new RelayCommand(() => IsTaskPreview = false);
        ShowTaskPreviewCommand = new RelayCommand(() => IsTaskPreview = true);
        ResetDefaultsCommand = new RelayCommand(ResetDefaults);
    }

    public Guid ThemeId { get; }

    public IReadOnlyList<string> ArtSizes { get; } =
        [ArtCover, ArtContain, ArtCrop];

    public IReadOnlyList<string> TaskModes { get; } =
        [TaskAmbient, TaskHidden, TaskFull];

    public RelayCommand ShowHomePreviewCommand { get; }

    public RelayCommand ShowTaskPreviewCommand { get; }

    public RelayCommand ResetDefaultsCommand { get; }

    public IReadOnlyList<string> ThemePaletteColors =>
        [BackgroundColor, PanelColor, AccentColor,
         TextColor, MutedColor, BorderColor];

    public ReadOnlyObservableCollection<string> ColorHistory { get; private set; } = null!;

    public string Name
    {
        get => name;
        set => SetDraftProperty(ref name, value ?? string.Empty);
    }

    public string BackgroundColor
    {
        get => backgroundColor;
        set => SetColorProperty(ref backgroundColor, value);
    }

    public string PanelColor
    {
        get => panelColor;
        set => SetColorProperty(ref panelColor, value);
    }

    public string AccentColor
    {
        get => accentColor;
        set => SetColorProperty(ref accentColor, value);
    }

    public string TextColor
    {
        get => textColor;
        set => SetColorProperty(ref textColor, value);
    }

    public string MutedColor
    {
        get => mutedColor;
        set => SetColorProperty(ref mutedColor, value);
    }

    public string BorderColor
    {
        get => borderColor;
        set => SetColorProperty(ref borderColor, value);
    }

    public string ArtSize
    {
        get => artSize;
        set
        {
            var normalized = ArtSizes.Contains(value) ? value : ArtCover;
            if (SetDraftProperty(ref artSize, normalized))
            {
                OnPropertyChanged(nameof(IsCropMode));
                OnPropertyChanged(nameof(PreviewScale));
            }
        }
    }

    public bool IsCropMode => ArtSize == ArtCrop;

    public double CropScale
    {
        get => cropScale;
        set
        {
            if (SetDraftProperty(ref cropScale, Math.Clamp(value, 1, 3)))
            {
                OnPropertyChanged(nameof(PreviewScale));
            }
        }
    }

    public double FocusX
    {
        get => focusX;
        set => SetDraftProperty(ref focusX, Math.Clamp(value, 0, 1));
    }

    public double FocusY
    {
        get => focusY;
        set => SetDraftProperty(ref focusY, Math.Clamp(value, 0, 1));
    }

    public double HomeOpacity
    {
        get => homeOpacity;
        set
        {
            if (SetDraftProperty(ref homeOpacity, Math.Clamp(value, 0, 1)))
            {
                OnPropertyChanged(nameof(PreviewOpacity));
            }
        }
    }

    public double HomeOverlay
    {
        get => homeOverlay;
        set
        {
            if (SetDraftProperty(ref homeOverlay, Math.Clamp(value, 0, 1)))
            {
                OnPropertyChanged(nameof(PreviewOverlay));
            }
        }
    }

    public string TaskMode
    {
        get => taskMode;
        set
        {
            var normalized = TaskModes.Contains(value) ? value : TaskAmbient;
            if (SetDraftProperty(ref taskMode, normalized))
            {
                OnPropertyChanged(nameof(IsTaskOverlayEnabled));
                OnPropertyChanged(nameof(TaskContentOverlay));
                OnPropertyChanged(nameof(PreviewOpacity));
            }
        }
    }

    public bool IsTaskOverlayEnabled => TaskMode != TaskHidden;

    public double TaskOpacity
    {
        get => taskOpacity;
        set
        {
            if (SetDraftProperty(ref taskOpacity, Math.Clamp(value, 0, 1)))
            {
                OnPropertyChanged(nameof(PreviewOpacity));
            }
        }
    }

    public double TaskOverlay
    {
        get => taskOverlay;
        set
        {
            if (SetDraftProperty(ref taskOverlay, Math.Clamp(value, 0, 1)))
            {
                OnPropertyChanged(nameof(TaskContentOverlay));
            }
        }
    }

    public double Blur
    {
        get => blur;
        set => SetDraftProperty(ref blur, Math.Clamp(value, 0, 64));
    }

    public double PanelBlur
    {
        get => panelBlur;
        set
        {
            if (SetDraftProperty(ref panelBlur, Math.Clamp(value, 0, 64)))
            {
                OnPropertyChanged(nameof(PanelGlassOpacity));
            }
        }
    }

    public double PanelGlassOpacity => PanelBlur / 64 * 0.45;

    public Uri? LocalPreviewAssetUri
    {
        get => localPreviewAssetUri;
        private set => SetDraftProperty(ref localPreviewAssetUri, value);
    }

    public bool HasPreviewImage => LocalPreviewAssetUri is not null;

    public bool IsTaskPreview
    {
        get => isTaskPreview;
        private set
        {
            if (SetProperty(ref isTaskPreview, value))
            {
                OnPropertyChanged(nameof(IsHomePreview));
                OnPropertyChanged(nameof(PreviewOpacity));
                OnPropertyChanged(nameof(PreviewOverlay));
                OnPropertyChanged(nameof(TaskContentOverlay));
                OnPropertyChanged(nameof(PreviewTitle));
                OnPropertyChanged(nameof(PreviewSubtitle));
            }
        }
    }

    public bool IsHomePreview => !IsTaskPreview;

    public string PreviewTitle =>
        IsTaskPreview ? "示例任务" : "今天想创建什么？";

    public string PreviewSubtitle => IsTaskPreview
        ? "已完成的任务和变更会显示在这里。"
        : "描述你想完成的任务";

    public double PreviewOpacity => IsTaskPreview
        ? TaskMode == TaskHidden ? 0 : TaskOpacity
        : HomeOpacity;

    public double PreviewOverlay => IsTaskPreview ? 0 : HomeOverlay;

    public double TaskContentOverlay =>
        !IsTaskPreview ? 0 : TaskMode == TaskHidden ? 1 : TaskOverlay;

    public double PreviewScale => IsCropMode ? CropScale : 1;

    public bool IsDirty
    {
        get => isDirty;
        private set => SetProperty(ref isDirty, value);
    }

    public ThemePalette BuildPalette() => new(
        BackgroundColor,
        PanelColor,
        AccentColor,
        TextColor,
        MutedColor,
        BorderColor);

    public ThemeEditorState CreateState() => new(
        Name,
        BackgroundColor,
        PanelColor,
        AccentColor,
        TextColor,
        MutedColor,
        BorderColor,
        ArtSize,
        TaskMode,
        CropScale,
        FocusX,
        FocusY,
        HomeOpacity,
        HomeOverlay,
        TaskOpacity,
        TaskOverlay,
        Blur,
        PanelBlur,
        LocalPreviewAssetUri);

    public void SetPreviewImage(Uri value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.IsAbsoluteUri ||
            (value.Scheme != Uri.UriSchemeFile && value.Scheme != "avares"))
        {
            throw new ArgumentException(
                "Only local file and bundled asset images are allowed.",
                nameof(value));
        }

        LocalPreviewAssetUri = value;
        OnPropertyChanged(nameof(HasPreviewImage));
    }

    public void RecordColor(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var existing = colorHistory.FirstOrDefault(item =>
            string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            colorHistory.Remove(existing);
        }

        colorHistory.Insert(0, value);
        while (colorHistory.Count > 12)
        {
            colorHistory.RemoveAt(colorHistory.Count - 1);
        }
    }

    public void MarkSaved() => IsDirty = false;

    private void ResetDefaults()
    {
        BackgroundColor = "#111111";
        PanelColor = "#1C1C1CE6";
        AccentColor = "#3B82F6";
        TextColor = "#F5F5F5";
        MutedColor = "#A3A3A3";
        BorderColor = "#30303080";
        ArtSize = ArtCover;
        CropScale = 1;
        FocusX = 0.5;
        FocusY = 0.5;
        HomeOpacity = 0.72;
        HomeOverlay = 0.28;
        TaskMode = TaskAmbient;
        TaskOpacity = 0.22;
        TaskOverlay = 0.62;
        Blur = 0;
        PanelBlur = 10;
    }

    private bool SetDraftProperty<T>(ref T field, T value)
    {
        if (!SetProperty(ref field, value))
        {
            return false;
        }

        IsDirty = true;
        return true;
    }

    private void SetColorProperty(ref string field, string? value)
    {
        if (SetDraftProperty(ref field, value ?? string.Empty))
        {
            OnPropertyChanged(nameof(ThemePaletteColors));
        }
    }
}
