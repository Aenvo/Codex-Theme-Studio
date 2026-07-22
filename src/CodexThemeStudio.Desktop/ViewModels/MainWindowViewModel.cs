using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;
using CodexThemeStudio.Desktop.Infrastructure;
using CodexThemeStudio.Desktop.Services;

namespace CodexThemeStudio.Desktop.ViewModels;

public enum LibraryPage
{
    Current,
    All,
    Favorites,
    Import,
    Editor,
    Settings,
}

public enum ThemeSort
{
    Name,
    RecentlyModified,
    RecentlyUsed,
    FavoritesFirst,
}

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IThemeRepository repository;
    private readonly ICodexThemeRuntime runtime;
    private readonly IPersistenceService persistence;
    private readonly IStorageLocationService storageLocation;
    private readonly IThemePackageService? packageService;
    private readonly IUserDialogService dialogs;
    private readonly Func<string?, string?> resolveThumbnail;
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<ThemeCardViewModel> allThemes = [];
    private readonly ObservableCollection<ThemeCardViewModel> visibleThemes = [];
    private readonly ObservableCollection<string> availableTags = ["全部标签"];
    private ThemeCardViewModel? selectedTheme;
    private string searchText = string.Empty;
    private string selectedTag = "全部标签";
    private ThemeSort selectedSort = ThemeSort.RecentlyModified;
    private LibraryPage currentPage = LibraryPage.All;
    private bool isBusy;
    private string operationText = string.Empty;
    private string notificationMessage = "主题资料库已准备就绪。";
    private string notificationKind = "Info";
    private string codexStatusText = "正在检查 Codex…";
    private string codexStatusDetail = "状态由本地应用服务提供。";
    private string dataRoot = "正在读取…";
    private string themeCountText = "0 个主题";
    private ThemeRuntimeStatus? runtimeStatus;
    private bool isPersistenceEnabled;
    private string storageUsageText = "正在计算…";
    private string storageStatusText = "正在检查数据位置…";
    private bool isStorageAvailable;

    public MainWindowViewModel(
        IThemeRepository repository,
        ICodexThemeRuntime runtime,
        IPersistenceService persistence,
        IStorageLocationService storageLocation,
        IUserDialogService dialogs,
        Func<string?, string?> resolveThumbnail,
        IThemePackageService? packageService = null,
        ThemeEditorViewModel? editor = null)
    {
        this.repository = repository;
        this.runtime = runtime;
        this.persistence = persistence;
        this.storageLocation = storageLocation;
        this.packageService = packageService;
        this.dialogs = dialogs;
        this.resolveThumbnail = resolveThumbnail;
        Editor = editor;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        CreateCommand = new AsyncRelayCommand(CreateAsync, () => !IsBusy);
        CopyCommand = new AsyncRelayCommand(CopyAsync, CanMutateSelection);
        RenameCommand = new AsyncRelayCommand(RenameAsync, CanMutateSelection);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, CanDeleteSelection);
        ToggleFavoriteCommand = new AsyncRelayCommand(
            ToggleFavoriteAsync,
            () => SelectedTheme is not null && !IsBusy);
        ApplyTemporaryCommand = new AsyncRelayCommand(
            ApplyTemporaryAsync,
            () => SelectedTheme is not null && !IsBusy);
        SetPersistentCommand = new AsyncRelayCommand(
            SetPersistentAsync,
            () => SelectedTheme is not null && !IsBusy);
        RestoreCommand = new AsyncRelayCommand(
            RestoreAsync,
            () => !IsBusy && runtimeStatus is not null);
        DisablePersistenceCommand = new AsyncRelayCommand(
            DisablePersistenceAsync,
            () => !IsBusy && IsPersistenceEnabled);
        NavigateCommand = new RelayCommand(Navigate);
        ImportCommand = new AsyncRelayCommand(ImportAsync, () => !IsBusy && packageService is not null);
        EditCommand = new AsyncRelayCommand(EditAsync, CanMutateSelection);
        SelectImageCommand = new AsyncRelayCommand(SelectImageAsync, () => !IsBusy && Editor?.HasDraft == true);
        SaveDraftCommand = new AsyncRelayCommand(() => SaveDraftAsync(false), () => !IsBusy && Editor?.HasDraft == true);
        SaveCopyCommand = new AsyncRelayCommand(() => SaveDraftAsync(true), () => !IsBusy && Editor?.HasDraft == true);
        CancelDraftCommand = new AsyncRelayCommand(CancelDraftAsync, () => !IsBusy && Editor?.HasDraft == true);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => SelectedTheme is not null && !IsBusy && packageService is not null);
        MigrateStorageCommand = new AsyncRelayCommand(
            MigrateStorageAsync,
            () => !IsBusy && isStorageAvailable);
        OpenDataRootCommand = new RelayCommand(_ => OpenDataRoot(), _ => Directory.Exists(DataRoot));
    }

    public ReadOnlyObservableCollection<ThemeCardViewModel> Themes =>
        new(visibleThemes);

    public ReadOnlyObservableCollection<string> AvailableTags =>
        new(availableTags);

    public IReadOnlyList<ThemeSort> SortOptions { get; } =
        Enum.GetValues<ThemeSort>();

    public ThemeCardViewModel? SelectedTheme
    {
        get => selectedTheme;
        set
        {
            if (SetProperty(ref selectedTheme, value))
            {
                NotifyCommands();
                OnPropertyChanged(nameof(SelectedThemeName));
            }
        }
    }

    public string SelectedThemeName =>
        SelectedTheme?.DisplayName ?? "未选择主题";

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public string SelectedTag
    {
        get => selectedTag;
        set
        {
            if (SetProperty(ref selectedTag, value))
            {
                ApplyFilter();
            }
        }
    }

    public ThemeSort SelectedSort
    {
        get => selectedSort;
        set
        {
            if (SetProperty(ref selectedSort, value))
            {
                ApplyFilter();
            }
        }
    }

    public LibraryPage CurrentPage
    {
        get => currentPage;
        private set
        {
            if (SetProperty(ref currentPage, value))
            {
                OnPropertyChanged(nameof(IsLibraryVisible));
                OnPropertyChanged(nameof(IsSettingsVisible));
                OnPropertyChanged(nameof(IsEditorVisible));
                OnPropertyChanged(nameof(PageTitle));
            }
        }
    }

    public bool IsLibraryVisible =>
        CurrentPage is not LibraryPage.Settings and not LibraryPage.Editor;

    public bool IsSettingsVisible => CurrentPage == LibraryPage.Settings;

    public bool IsEditorVisible => CurrentPage == LibraryPage.Editor;

    public string PageTitle => CurrentPage switch
    {
        LibraryPage.Current => "当前主题",
        LibraryPage.Favorites => "收藏主题",
        LibraryPage.Import => "导入主题",
        LibraryPage.Editor => "主题编辑器",
        LibraryPage.Settings => "设置",
        _ => "主题资料库",
    };

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                NotifyCommands();
            }
        }
    }

    public string OperationText
    {
        get => operationText;
        private set => SetProperty(ref operationText, value);
    }

    public string NotificationMessage
    {
        get => notificationMessage;
        private set => SetProperty(ref notificationMessage, value);
    }

    public string NotificationKind
    {
        get => notificationKind;
        private set => SetProperty(ref notificationKind, value);
    }

    public string CodexStatusText
    {
        get => codexStatusText;
        private set => SetProperty(ref codexStatusText, value);
    }

    public string CodexStatusDetail
    {
        get => codexStatusDetail;
        private set => SetProperty(ref codexStatusDetail, value);
    }

    public string DataRoot
    {
        get => dataRoot;
        private set => SetProperty(ref dataRoot, value);
    }

    public string ThemeCountText
    {
        get => themeCountText;
        private set => SetProperty(ref themeCountText, value);
    }

    public bool IsPersistenceEnabled
    {
        get => isPersistenceEnabled;
        private set
        {
            if (SetProperty(ref isPersistenceEnabled, value))
            {
                NotifyCommands();
            }
        }
    }

    public ThemeEditorViewModel? Editor { get; }

    public string StorageUsageText
    {
        get => storageUsageText;
        private set => SetProperty(ref storageUsageText, value);
    }

    public string StorageStatusText
    {
        get => storageStatusText;
        private set => SetProperty(ref storageStatusText, value);
    }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand CreateCommand { get; }

    public AsyncRelayCommand CopyCommand { get; }

    public AsyncRelayCommand RenameCommand { get; }

    public AsyncRelayCommand DeleteCommand { get; }

    public AsyncRelayCommand ToggleFavoriteCommand { get; }

    public AsyncRelayCommand ApplyTemporaryCommand { get; }

    public AsyncRelayCommand SetPersistentCommand { get; }

    public AsyncRelayCommand RestoreCommand { get; }

    public AsyncRelayCommand DisablePersistenceCommand { get; }

    public RelayCommand NavigateCommand { get; }

    public AsyncRelayCommand ImportCommand { get; }

    public AsyncRelayCommand EditCommand { get; }

    public AsyncRelayCommand SelectImageCommand { get; }

    public AsyncRelayCommand SaveDraftCommand { get; }

    public AsyncRelayCommand SaveCopyCommand { get; }

    public AsyncRelayCommand CancelDraftCommand { get; }

    public AsyncRelayCommand ExportCommand { get; }

    public AsyncRelayCommand MigrateStorageCommand { get; }

    public RelayCommand OpenDataRootCommand { get; }

    public async Task InitializeAsync()
    {
        await LoadThemesAsync();
        await RefreshStatusAsync();
    }

    public void Dispose()
    {
        lifetime.Cancel();
        lifetime.Dispose();
    }

    private async Task RefreshAsync()
    {
        await RunOperationAsync(
            "正在刷新主题和 Codex 状态…",
            async cancellationToken =>
            {
                await LoadThemesAsync(cancellationToken);
                await RefreshStatusAsync(cancellationToken);
                Notify("资料库和 Codex 状态已刷新。", "Success");
            });
    }

    private async Task CreateAsync()
    {
        var name = await dialogs.RequestTextAsync(
            "新建主题",
            "为新主题输入名称。下一步可选择背景并调整预览参数。",
            "我的新主题",
            lifetime.Token);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (Editor is null)
        {
            Notify("主题编辑服务当前不可用。", "Error");
            return;
        }

        Editor.Begin(CreateStarterTheme(Guid.NewGuid(), name), newTheme: true);
        CurrentPage = LibraryPage.Editor;
        Notify("草稿已创建；保存前不会替换正式主题或运行主题。", "Info");
        NotifyCommands();
    }

    private async Task EditAsync()
    {
        if (SelectedTheme is null || Editor is null)
        {
            return;
        }

        var theme = await repository.GetAsync(SelectedTheme.ThemeId, lifetime.Token);
        if (!theme.IsSuccess)
        {
            NotifyError(theme.Error!);
            return;
        }

        Editor.Begin(theme.Value!, newTheme: false, SelectedTheme.ThumbnailPath);
        CurrentPage = LibraryPage.Editor;
        Notify("正在编辑草稿；取消不会影响正式主题或当前运行主题。", "Info");
        NotifyCommands();
    }

    private async Task SelectImageAsync()
    {
        if (Editor is null)
        {
            return;
        }

        var file = await dialogs.PickOpenFileAsync(
            "选择主题背景",
            "支持的图片|*.png;*.jpg;*.jpeg;*.webp|PNG|*.png|JPEG|*.jpg;*.jpeg|WebP|*.webp",
            lifetime.Token);
        if (file is null)
        {
            return;
        }

        await ImportEditorImageAsync(file);
    }

    public async Task ImportEditorImageAsync(string file)
    {
        if (Editor is null)
        {
            return;
        }

        await RunOperationAsync(
            "正在安全处理背景图片…",
            async cancellationToken =>
            {
                var result = await Editor.SelectImageAsync(file, cancellationToken);
                if (!result.IsSuccess)
                {
                    NotifyError(result.Error!);
                    return;
                }

                Notify("背景已进入草稿并生成受管预览；保存后才会更新正式主题。", "Success");
            });
    }

    private async Task SaveDraftAsync(bool saveCopy)
    {
        if (Editor is null)
        {
            return;
        }

        string? copyName = null;
        if (saveCopy)
        {
            copyName = await dialogs.RequestTextAsync(
                "另存为主题副本",
                "输入副本名称。原主题不会被修改。",
                $"{Editor.Name} 副本",
                lifetime.Token);
            if (string.IsNullOrWhiteSpace(copyName))
            {
                return;
            }
        }

        await RunOperationAsync(
            saveCopy ? "正在保存主题副本…" : "正在保存主题草稿…",
            async cancellationToken =>
            {
                var result = await Editor.SaveAsync(saveCopy, copyName, cancellationToken);
                if (!result.IsSuccess)
                {
                    NotifyError(result.Error!);
                    return;
                }

                var savedId = result.Value!.Id;
                Editor.Cancel();
                CurrentPage = LibraryPage.All;
                await LoadThemesAsync(cancellationToken, savedId);
                Notify(
                    saveCopy ? "主题副本已保存；原主题保持不变。" : "主题草稿已保存为正式主题。",
                    "Success");
            });
    }

    private async Task CancelDraftAsync()
    {
        if (Editor is null)
        {
            return;
        }

        var confirmed = await dialogs.ConfirmAsync(
            "取消主题编辑",
            "未保存的草稿参数将被放弃。正式主题和当前运行主题不会改变。",
            "选择“确定”放弃草稿。",
            lifetime.Token);
        if (!confirmed)
        {
            return;
        }

        Editor.Cancel();
        CurrentPage = LibraryPage.All;
        Notify("已取消编辑；正式主题和运行主题未改变。", "Info");
        NotifyCommands();
    }

    private async Task ImportAsync()
    {
        if (packageService is null)
        {
            return;
        }

        var file = await dialogs.PickOpenFileAsync(
            "导入主题包",
            "Codex Theme Studio 主题包|*.cttheme",
            lifetime.Token);
        if (file is null)
        {
            return;
        }

        await RunOperationAsync(
            "正在验证并导入主题包…",
            async cancellationToken =>
            {
                var result = await packageService.ImportAsync(file, cancellationToken);
                if (!result.IsSuccess)
                {
                    NotifyError(result.Error!);
                    return;
                }

                CurrentPage = LibraryPage.All;
                await LoadThemesAsync(cancellationToken, result.Value!.ThemeId);
                Notify($"已安全导入“{result.Value.DisplayName}”。", "Success");
            });
    }

    private async Task ExportAsync()
    {
        if (packageService is null || SelectedTheme is null)
        {
            return;
        }

        var file = await dialogs.PickSaveFileAsync(
            "导出主题包",
            "Codex Theme Studio 主题包|*.cttheme",
            ".cttheme",
            $"{SelectedTheme.DisplayName}.cttheme",
            lifetime.Token);
        if (file is null)
        {
            return;
        }

        await RunOperationAsync(
            "正在生成并校验主题包…",
            async cancellationToken =>
            {
                var result = await packageService.ExportAsync(
                    SelectedTheme.ThemeId,
                    file,
                    cancellationToken);
                if (!result.IsSuccess)
                {
                    NotifyError(result.Error!);
                    return;
                }

                Notify($"主题包已导出到 {result.Value}", "Success");
            });
    }

    private async Task MigrateStorageAsync()
    {
        var destination = await dialogs.PickFolderAsync(
            "选择新的空数据目录",
            null,
            lifetime.Token);
        if (destination is null)
        {
            return;
        }

        var confirmed = await dialogs.ConfirmAsync(
            "迁移数据位置",
            $"应用将复制并校验数据后再切换定位文件。旧目录会保留为恢复副本：\n{DataRoot}\n\n目标：\n{destination}",
            "选择“确定”开始迁移；迁移期间请勿关闭应用。",
            lifetime.Token);
        if (!confirmed)
        {
            return;
        }

        await RunOperationAsync(
            "正在迁移并校验数据位置…",
            async cancellationToken =>
            {
                var result = await storageLocation.MigrateAsync(destination, cancellationToken);
                if (!result.IsSuccess)
                {
                    NotifyError(result.Error!);
                    return;
                }

                DataRoot = result.Value!.DataRoot;
                StorageStatusText = result.Value.UserMessage;
                var report = storageLocation.LastMigrationReport;
                StorageUsageText = report is null
                    ? "迁移已完成"
                    : $"{FormatBytes(report.TotalBytes)} · {report.FileCount} 个文件 · SHA-256 {report.CombinedSha256[..12]}…";
                Notify("迁移与 SQLite 完整性校验成功。旧目录已保留；请重启应用以让全部服务使用新位置。", "Success");
            });
    }

    private void OpenDataRoot()
    {
        if (!Directory.Exists(DataRoot))
        {
            Notify("数据目录当前离线，无法打开。", "Error");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            ArgumentList = { DataRoot },
            UseShellExecute = false,
        });
    }

    private async Task CopyAsync()
    {
        var selected = SelectedTheme;
        if (selected is null)
        {
            return;
        }

        var name = await dialogs.RequestTextAsync(
            "复制主题",
            "输入副本名称。",
            $"{selected.DisplayName} 副本",
            lifetime.Token);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await RunOperationAsync(
            "正在复制主题…",
            async cancellationToken =>
            {
                var newId = Guid.NewGuid();
                var result = await repository.CopyAsync(
                    selected.ThemeId,
                    newId,
                    name,
                    cancellationToken);
                if (!result.IsSuccess)
                {
                    NotifyError(result.Error!);
                    return;
                }

                await LoadThemesAsync(cancellationToken, newId);
                Notify("主题副本已创建。", "Success");
            });
    }

    private async Task RenameAsync()
    {
        var selected = SelectedTheme;
        if (selected is null)
        {
            return;
        }

        var name = await dialogs.RequestTextAsync(
            "重命名主题",
            "输入新的主题名称。",
            selected.DisplayName,
            lifetime.Token);
        if (string.IsNullOrWhiteSpace(name) ||
            string.Equals(name, selected.DisplayName, StringComparison.Ordinal))
        {
            return;
        }

        await RunOperationAsync(
            "正在重命名主题…",
            async cancellationToken =>
            {
                var result = await repository.RenameAsync(
                    selected.ThemeId,
                    name,
                    cancellationToken);
                if (!result.IsSuccess)
                {
                    NotifyError(result.Error!);
                    return;
                }

                await LoadThemesAsync(cancellationToken, selected.ThemeId);
                Notify("主题已重命名。", "Success");
            });
    }

    private async Task DeleteAsync()
    {
        var selected = SelectedTheme;
        if (selected is null)
        {
            return;
        }

        if (selected.IsPersistent || selected.IsTemporary)
        {
            Notify(
                "当前正在使用的主题不能删除。请先切换主题、还原外观或停用持久化。",
                "Error");
            return;
        }

        var confirmed = await dialogs.ConfirmAsync(
            "移入应用回收站",
            $"“{selected.DisplayName}”将从资料库隐藏，但主题文件会保留以便恢复。",
            "选择“确定”继续。",
            lifetime.Token);
        if (!confirmed)
        {
            return;
        }

        await RunOperationAsync(
            "正在将主题移入应用回收站…",
            async cancellationToken =>
            {
                var result = await repository.DeleteAsync(
                    selected.ThemeId,
                    cancellationToken);
                if (!result.IsSuccess)
                {
                    NotifyError(result.Error!);
                    return;
                }

                await LoadThemesAsync(cancellationToken);
                Notify("主题已移入应用回收站，文件仍保留在数据目录中。", "Success");
            });
    }

    private async Task ToggleFavoriteAsync()
    {
        var selected = SelectedTheme;
        if (selected is null)
        {
            return;
        }

        await RunOperationAsync(
            selected.IsFavorite ? "正在取消收藏…" : "正在添加收藏…",
            async cancellationToken =>
            {
                var result = await repository.SetFavoriteAsync(
                    selected.ThemeId,
                    !selected.IsFavorite,
                    cancellationToken);
                if (!result.IsSuccess)
                {
                    NotifyError(result.Error!);
                    return;
                }

                await LoadThemesAsync(cancellationToken, selected.ThemeId);
                Notify(selected.IsFavorite ? "已取消收藏。" : "已加入收藏。", "Success");
            });
    }

    private async Task ApplyTemporaryAsync()
    {
        var selected = SelectedTheme;
        if (selected is null)
        {
            return;
        }

        await RunOperationAsync(
            "正在临时应用主题…",
            async cancellationToken =>
            {
                var theme = await repository.GetAsync(selected.ThemeId, cancellationToken);
                if (!theme.IsSuccess)
                {
                    NotifyError(theme.Error!);
                    return;
                }

                var result = runtimeStatus?.ThemeId is null
                    ? await runtime.ApplyTemporaryAsync(theme.Value!, cancellationToken)
                    : await runtime.SwitchTemporaryAsync(theme.Value!, cancellationToken);
                if (!result.IsSuccess)
                {
                    NotifyError(result.Error!);
                    return;
                }

                ApplyRuntimeStatus(result.Value! with
                {
                    IsPersistenceEnabled = IsPersistenceEnabled,
                });
                MarkThemeStates();
                Notify(result.Value!.UserMessage, "Success");
            });
    }

    private async Task SetPersistentAsync()
    {
        var selected = SelectedTheme;
        if (selected is null)
        {
            return;
        }

        await RunOperationAsync(
            IsPersistenceEnabled ? "正在切换持久主题…" : "正在启用持久化…",
            async cancellationToken =>
            {
                var theme = await repository.GetAsync(selected.ThemeId, cancellationToken);
                if (!theme.IsSuccess)
                {
                    NotifyError(theme.Error!);
                    return;
                }

                var result = IsPersistenceEnabled
                    ? await persistence.SwitchAsync(theme.Value!, cancellationToken)
                    : await persistence.EnableAsync(theme.Value!, cancellationToken);
                if (!result.IsSuccess)
                {
                    NotifyError(result.Error!);
                    return;
                }

                ApplyRuntimeStatus(result.Value!);
                await LoadThemesAsync(cancellationToken, selected.ThemeId);
                Notify(result.Value!.UserMessage, "Success");
            });
    }

    private async Task RestoreAsync()
    {
        var persistenceWasEnabled = IsPersistenceEnabled;
        await RunOperationAsync(
            "正在还原 Codex 外观…",
            async cancellationToken =>
            {
                var result = await runtime.RestoreAsync(cancellationToken);
                if (!result.IsSuccess)
                {
                    NotifyError(result.Error!);
                    return;
                }

                ApplyRuntimeStatus(result.Value! with
                {
                    IsPersistenceEnabled = persistenceWasEnabled,
                });
                MarkThemeStates();
                Notify(
                    persistenceWasEnabled
                        ? "当前 Codex 已还原；持久化仍启用，后续实例仍会应用持久主题。"
                        : result.Value!.UserMessage,
                    "Success");
            });
    }

    private async Task DisablePersistenceAsync()
    {
        var confirmed = await dialogs.ConfirmAsync(
            "停用持久化",
            "这会移除当前用户启动项、停止 Agent 并还原当前 Codex。主题库不会删除。",
            "选择“确定”继续。",
            lifetime.Token);
        if (!confirmed)
        {
            return;
        }

        await RunOperationAsync(
            "正在停用持久化…",
            async cancellationToken =>
            {
                var result = await persistence.DisableAsync(cancellationToken);
                if (!result.IsSuccess)
                {
                    NotifyError(result.Error!);
                    return;
                }

                ApplyRuntimeStatus(result.Value!);
                await LoadThemesAsync(cancellationToken);
                Notify(result.Value!.UserMessage, "Success");
            });
    }

    private async Task LoadThemesAsync(
        CancellationToken cancellationToken = default,
        Guid? selectThemeId = null)
    {
        var result = await repository.ListAsync(cancellationToken);
        if (!result.IsSuccess)
        {
            NotifyError(result.Error!);
            return;
        }

        var previousSelection = selectThemeId ?? SelectedTheme?.ThemeId;
        allThemes.Clear();
        allThemes.AddRange(
            result.Value!.Select(summary =>
                new ThemeCardViewModel(
                    summary,
                    resolveThumbnail(summary.ThumbnailRelativePath))));
        RebuildTags();
        ApplyFilter();
        SelectedTheme = allThemes.FirstOrDefault(item => item.ThemeId == previousSelection)
            ?? visibleThemes.FirstOrDefault();
        MarkThemeStates();
    }

    private async Task RefreshStatusAsync(CancellationToken cancellationToken = default)
    {
        var storage = await storageLocation.GetStatusAsync(cancellationToken);
        DataRoot = storage.IsSuccess
            ? storage.Value!.DataRoot
            : "数据目录状态不可用";
        StorageStatusText = storage.IsSuccess
            ? storage.Value!.UserMessage
            : storage.Error!.UserMessage;
        isStorageAvailable =
            storage.IsSuccess && storage.Value!.IsAvailable && storage.Value.IsWritable;
        StorageUsageText = storage.IsSuccess && storage.Value!.IsAvailable
            ? await CalculateStorageUsageAsync(storage.Value.DataRoot, cancellationToken)
            : "不可用（未创建替代空库）";
        OpenDataRootCommand.NotifyCanExecuteChanged();
        MigrateStorageCommand.NotifyCanExecuteChanged();

        var persistenceResult = await persistence.GetStatusAsync(cancellationToken);
        IsPersistenceEnabled =
            persistenceResult.IsSuccess && persistenceResult.Value!.IsPersistenceEnabled;

        var runtimeResult = await runtime.GetStatusAsync(cancellationToken);
        if (!runtimeResult.IsSuccess)
        {
            CodexStatusText = "Codex 状态不可用";
            CodexStatusDetail = runtimeResult.Error!.UserMessage;
            NotifyError(runtimeResult.Error);
            return;
        }

        var status = runtimeResult.Value! with
        {
            IsPersistenceEnabled = IsPersistenceEnabled,
            ThemeId = persistenceResult.IsSuccess &&
                persistenceResult.Value!.IsPersistenceEnabled
                    ? persistenceResult.Value.ThemeId
                    : runtimeResult.Value.ThemeId,
        };
        ApplyRuntimeStatus(status);
        MarkThemeStates();
    }

    private void ApplyRuntimeStatus(ThemeRuntimeStatus status)
    {
        runtimeStatus = status;
        IsPersistenceEnabled = status.IsPersistenceEnabled;
        CodexStatusText = status.State switch
        {
            ThemeRuntimeState.NotInstalled => "Codex 未安装",
            ThemeRuntimeState.NotRunning => "Codex 未运行",
            ThemeRuntimeState.Unsupported => "Codex 版本未验证",
            ThemeRuntimeState.Temporary => "已临时应用",
            ThemeRuntimeState.Persistent => "持久化已启用",
            ThemeRuntimeState.Partial => "部分窗口已应用",
            ThemeRuntimeState.Mismatch => "主题状态不一致",
            ThemeRuntimeState.InspectorResidual => "Inspector 状态异常",
            ThemeRuntimeState.Ready => "Codex 已就绪",
            ThemeRuntimeState.Default => "Codex 官方外观",
            _ => "Codex 状态需要关注",
        };
        CodexStatusDetail = status.UserMessage;
        NotifyCommands();
    }

    private void MarkThemeStates()
    {
        foreach (var theme in allThemes)
        {
            theme.IsPersistent =
                theme.Summary.IsCurrentPersistent ||
                (IsPersistenceEnabled && runtimeStatus?.ThemeId == theme.ThemeId);
            theme.IsTemporary =
                !theme.IsPersistent &&
                runtimeStatus?.State is ThemeRuntimeState.Temporary or ThemeRuntimeState.Partial &&
                runtimeStatus.ThemeId == theme.ThemeId;
        }

        if (CurrentPage == LibraryPage.Current)
        {
            ApplyFilter();
        }

        NotifyCommands();
    }

    private void ApplyFilter()
    {
        IEnumerable<ThemeCardViewModel> query = allThemes;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(item =>
                item.DisplayName.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
                item.Tags.Any(tag =>
                    tag.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)));
        }

        if (SelectedTag != "全部标签")
        {
            query = query.Where(item =>
                item.Tags.Contains(SelectedTag, StringComparer.CurrentCultureIgnoreCase));
        }

        query = CurrentPage switch
        {
            LibraryPage.Current => query.Where(item => item.IsTemporary || item.IsPersistent),
            LibraryPage.Favorites => query.Where(item => item.IsFavorite),
            _ => query,
        };

        query = SelectedSort switch
        {
            ThemeSort.Name => query.OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            ThemeSort.RecentlyUsed => query
                .OrderByDescending(item => item.Summary.LastUsedAtUtc)
                .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            ThemeSort.FavoritesFirst => query
                .OrderByDescending(item => item.IsFavorite)
                .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            _ => query
                .OrderByDescending(item => item.Summary.ModifiedAtUtc)
                .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase),
        };

        visibleThemes.Clear();
        foreach (var item in query)
        {
            visibleThemes.Add(item);
        }

        ThemeCountText = $"{visibleThemes.Count} 个主题";
    }

    private void RebuildTags()
    {
        var selected = SelectedTag;
        availableTags.Clear();
        availableTags.Add("全部标签");
        foreach (var tag in allThemes
                     .SelectMany(theme => theme.Tags)
                     .Distinct(StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase))
        {
            availableTags.Add(tag);
        }

        SelectedTag = availableTags.Contains(selected) ? selected : "全部标签";
    }

    private void Navigate(object? parameter)
    {
        if (parameter is not string value ||
            !Enum.TryParse<LibraryPage>(value, out var destination))
        {
            return;
        }

        CurrentPage = destination;
        ApplyFilter();
    }

    private async Task RunOperationAsync(
        string operation,
        Func<CancellationToken, Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        OperationText = operation;
        try
        {
            await action(lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            Notify("操作已取消。", "Info");
        }
        finally
        {
            OperationText = string.Empty;
            IsBusy = false;
        }
    }

    private void NotifyError(OperationError error) =>
        Notify($"{error.UserMessage} 请检查状态后重试。", "Error");

    private void Notify(string message, string kind)
    {
        NotificationMessage = message;
        NotificationKind = kind;
    }

    private bool CanMutateSelection() =>
        SelectedTheme is not null &&
        !SelectedTheme.Summary.IsSourceReadOnly &&
        !IsBusy;

    private bool CanDeleteSelection() =>
        CanMutateSelection() &&
        SelectedTheme is { IsPersistent: false, IsTemporary: false };

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        CreateCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        ToggleFavoriteCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        SelectImageCommand.NotifyCanExecuteChanged();
        SaveDraftCommand.NotifyCanExecuteChanged();
        SaveCopyCommand.NotifyCanExecuteChanged();
        CancelDraftCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
        ImportCommand.NotifyCanExecuteChanged();
        MigrateStorageCommand.NotifyCanExecuteChanged();
        ApplyTemporaryCommand.NotifyCanExecuteChanged();
        SetPersistentCommand.NotifyCanExecuteChanged();
        RestoreCommand.NotifyCanExecuteChanged();
        DisablePersistenceCommand.NotifyCanExecuteChanged();
    }

    private static ThemePackage CreateStarterTheme(Guid id, string name) =>
        new(
            1,
            id,
            name,
            ThemeVariant.Auto,
            new ThemePalette(
                "#111827",
                "#1F2937",
                "#6D5EF7",
                "#F9FAFB",
                "#9CA3AF",
                "#374151"),
            new ThemeArt(
                "background.png",
                0.5,
                0.5,
                ThemeSafeArea.Center,
                ThemeArtSize.Cover,
                0.72,
                0.28,
                ThemeTaskMode.Ambient,
                0.22,
                0.62,
                0));

    private static async Task<string> CalculateStorageUsageAsync(
        string root,
        CancellationToken cancellationToken)
    {
        try
        {
            long bytes = 0;
            var count = 0;
            await Task.Run(
                () =>
                {
                    foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        bytes += new FileInfo(path).Length;
                        count++;
                    }
                },
                cancellationToken);
            return $"{FormatBytes(bytes)} · {count} 个文件";
        }
        catch (UnauthorizedAccessException)
        {
            return "无法读取占用空间";
        }
        catch (IOException)
        {
            return "存储当前不可用";
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:F1} {units[unit]}";
    }
}
