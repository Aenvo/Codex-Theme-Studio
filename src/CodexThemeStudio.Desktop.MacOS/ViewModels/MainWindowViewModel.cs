using System.Collections.ObjectModel;
using System.ComponentModel;
using CodexThemeStudio.Desktop.MacOS.Infrastructure;
using CodexThemeStudio.Desktop.MacOS.Services;

namespace CodexThemeStudio.Desktop.MacOS.ViewModels;

public enum MacWorkspacePage
{
    Workbench,
    Favorites,
    Trash,
    Editor,
}

public sealed class MainWindowViewModel : ObservableObject
{
    public const string ScopeAll = "全部主题";
    public const string ScopeFavorites = "收藏主题";
    public const string ScopeBackground = "含背景预览";
    public const string TagAll = "全部标签";
    public const string SortRecent = "最近使用";
    public const string SortName = "名称";
    public const string SortFavorites = "收藏优先";

    private readonly IMacThemeStudioClient client;
    private readonly List<ThemeCardViewModel> allThemes = [];
    private string searchText = string.Empty;
    private string selectedScope = ScopeAll;
    private string selectedTag = TagAll;
    private string selectedSort = SortRecent;
    private ThemeCardViewModel? selectedTheme;
    private MacLibraryState libraryState = MacLibraryState.Initializing;
    private MacRuntimeState runtimeState = MacRuntimeState.Unknown;
    private MacOperationStage operationStage = MacOperationStage.Idle;
    private MacProofState proofState = MacProofState.NotRequested;
    private bool isCardView = true;
    private MacWorkspacePage currentPage = MacWorkspacePage.Workbench;
    private MacWorkspacePage editorReturnPage = MacWorkspacePage.Workbench;
    private ThemeEditorViewModel? editor;
    private bool isOperationActive;
    private bool closeDeferred;
    private int cardColumnCount = 2;
    private string statusMessage = "正在准备主题资料库…";
    private string capabilityMessage =
        "应用范围：调色板与受支持的声明式样式";
    private string? primaryError;
    private string? recoveryError;

    public MainWindowViewModel(IMacThemeStudioClient client)
    {
        this.client = client ??
            throw new ArgumentNullException(nameof(client));
        VisibleThemes = [];
        VisibleThemeRows = [];
        ScopeOptions = [ScopeAll, ScopeFavorites, ScopeBackground];
        TagOptions = [TagAll];
        SortOptions = [SortRecent, SortName, SortFavorites];
        RefreshCommand = new AsyncCommand(
            RefreshAsync,
            () => !IsOperationActive);
        RetryLibraryCommand = new AsyncCommand(
            () => InitializeAsync(CancellationToken.None),
            () => !IsOperationActive && LibraryState == MacLibraryState.Error);
        ApplyCommand = new AsyncCommand(
            ApplyAsync,
            () => CanApply);
        RestoreCommand = new AsyncCommand(
            RestoreAsync,
            () => CanRestore);
        ShowCardsCommand = new RelayCommand(
            () => IsCardView = true,
            () => !IsCardView);
        ShowListCommand = new RelayCommand(
            () => IsCardView = false,
            () => IsCardView);
        FocusSearchCommand = new RelayCommand(
            () => SearchFocusRequested?.Invoke(this, EventArgs.Empty));
        ShowWorkbenchCommand = new RelayCommand(
            () => CurrentPage = MacWorkspacePage.Workbench);
        ShowFavoritesPageCommand = new RelayCommand(
            () => CurrentPage = MacWorkspacePage.Favorites);
        ShowTrashCommand = new RelayCommand(
            () => CurrentPage = MacWorkspacePage.Trash);
        EditCommand = new RelayCommand(OpenEditor, () => CanEdit);
        SelectEditorImageCommand = new AsyncCommand(
            SelectEditorImageAsync,
            () => IsEditorPage);
        CancelEditorCommand = new AsyncCommand(
            CancelEditorAsync,
            () => IsEditorPage);
        SaveEditorCommand = new RelayCommand(
            SaveEditor,
            () => IsEditorPage && Editor?.IsDirty == true);
        SaveCopyEditorCommand = new AsyncCommand(
            SaveCopyEditorAsync,
            () => IsEditorPage);
        ShowSettingsCommand = new RelayCommand(
            () => SettingsRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? SearchFocusRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler<EditorDialogRequestEventArgs<Uri?>>?
        EditorImageRequested;

    public event EventHandler<EditorDialogRequestEventArgs<bool>>?
        EditorCancelConfirmationRequested;

    public event EventHandler<EditorDialogRequestEventArgs<string?>>?
        EditorCopyNameRequested;

    public ObservableCollection<ThemeCardViewModel> VisibleThemes { get; }

    public ObservableCollection<ThemeCardRowViewModel> VisibleThemeRows { get; }

    public ObservableCollection<string> TagOptions { get; }

    public IReadOnlyList<string> ScopeOptions { get; }

    public IReadOnlyList<string> SortOptions { get; }

    public AsyncCommand RefreshCommand { get; }

    public AsyncCommand RetryLibraryCommand { get; }

    public AsyncCommand ApplyCommand { get; }

    public AsyncCommand RestoreCommand { get; }

    public RelayCommand ShowCardsCommand { get; }

    public RelayCommand ShowListCommand { get; }

    public RelayCommand FocusSearchCommand { get; }

    public RelayCommand ShowWorkbenchCommand { get; }

    public RelayCommand ShowFavoritesPageCommand { get; }

    public RelayCommand ShowTrashCommand { get; }

    public RelayCommand EditCommand { get; }

    public AsyncCommand SelectEditorImageCommand { get; }

    public AsyncCommand CancelEditorCommand { get; }

    public RelayCommand SaveEditorCommand { get; }

    public AsyncCommand SaveCopyEditorCommand { get; }

    public RelayCommand ShowSettingsCommand { get; }

    public MacWorkspacePage CurrentPage
    {
        get => currentPage;
        private set
        {
            if (!SetProperty(ref currentPage, value))
            {
                return;
            }

            if (value == MacWorkspacePage.Workbench)
            {
                SelectedScope = ScopeAll;
            }
            else if (value == MacWorkspacePage.Favorites)
            {
                SelectedScope = ScopeFavorites;
            }

            OnPropertyChanged(nameof(IsWorkbenchPage));
            OnPropertyChanged(nameof(IsFavoritesPage));
            OnPropertyChanged(nameof(IsTrashPage));
            OnPropertyChanged(nameof(IsEditorPage));
            OnPropertyChanged(nameof(IsThemeLibraryPage));
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(PageSubtitle));
        }
    }

    public bool IsWorkbenchPage =>
        CurrentPage == MacWorkspacePage.Workbench;

    public bool IsFavoritesPage =>
        CurrentPage == MacWorkspacePage.Favorites;

    public bool IsTrashPage =>
        CurrentPage == MacWorkspacePage.Trash;

    public bool IsEditorPage =>
        CurrentPage == MacWorkspacePage.Editor;

    public bool IsThemeLibraryPage =>
        CurrentPage is MacWorkspacePage.Workbench or MacWorkspacePage.Favorites;

    public ThemeEditorViewModel? Editor
    {
        get => editor;
        private set => SetProperty(ref editor, value);
    }

    public string PageTitle => CurrentPage switch
    {
        MacWorkspacePage.Favorites => "我的收藏",
        MacWorkspacePage.Trash => "回收站",
        _ => "主题资料库",
    };

    public string PageSubtitle => CurrentPage switch
    {
        MacWorkspacePage.Trash => "0 个已删除主题",
        _ => ThemeCountLabel,
    };

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    public string SelectedScope
    {
        get => selectedScope;
        set
        {
            if (SetProperty(ref selectedScope, value ?? ScopeAll))
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
            if (SetProperty(ref selectedTag, value ?? TagAll))
            {
                ApplyFilter();
            }
        }
    }

    public string SelectedSort
    {
        get => selectedSort;
        set
        {
            if (SetProperty(ref selectedSort, value ?? SortRecent))
            {
                ApplyFilter();
            }
        }
    }

    public ThemeCardViewModel? SelectedTheme
    {
        get => selectedTheme;
        private set
        {
            if (ReferenceEquals(selectedTheme, value))
            {
                return;
            }

            if (selectedTheme is not null)
            {
                selectedTheme.IsSelected = false;
            }

            if (SetProperty(ref selectedTheme, value) &&
                selectedTheme is not null)
            {
                selectedTheme.IsSelected = true;
            }

            NotifyActionState();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(IsSelectionEmpty));
            OnPropertyChanged(nameof(SelectedThemeName));
        }
    }

    public bool HasSelection => SelectedTheme is not null;

    public bool IsSelectionEmpty => SelectedTheme is null;

    public string SelectedThemeName =>
        SelectedTheme?.DisplayName ?? "未选择主题";

    public MacLibraryState LibraryState
    {
        get => libraryState;
        private set
        {
            if (SetProperty(ref libraryState, value))
            {
                OnPropertyChanged(nameof(IsLibraryLoading));
                OnPropertyChanged(nameof(IsLibraryEmpty));
                OnPropertyChanged(nameof(IsLibraryError));
                OnPropertyChanged(nameof(IsLibraryReady));
                OnPropertyChanged(nameof(IsFilterEmpty));
                OnPropertyChanged(nameof(ThemeCountLabel));
                OnPropertyChanged(nameof(PageSubtitle));
                RetryLibraryCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsLibraryLoading =>
        LibraryState is MacLibraryState.Initializing or MacLibraryState.Loading;

    public bool IsLibraryEmpty => LibraryState == MacLibraryState.Empty;

    public bool IsLibraryError => LibraryState == MacLibraryState.Error;

    public bool IsLibraryReady => LibraryState == MacLibraryState.Ready;

    public MacRuntimeState RuntimeState
    {
        get => runtimeState;
        private set
        {
            if (SetProperty(ref runtimeState, value))
            {
                NotifyActionState();
                OnPropertyChanged(nameof(RuntimeBadge));
                OnPropertyChanged(nameof(RuntimeStatusDetail));
                NotifyActionPanelState();
            }
        }
    }

    public string RuntimeBadge => RuntimeState switch
    {
        MacRuntimeState.Ready => "Codex 已就绪",
        MacRuntimeState.ReadyUnqualified => "需要首次资格验证",
        MacRuntimeState.CodexNotRunning => "Codex 未运行",
        MacRuntimeState.IdentityChanged => "Codex 身份已变化",
        MacRuntimeState.Unavailable => "Runtime 不可用",
        _ => "正在检测 Codex",
    };

    public string RuntimeStatusDetail => RuntimeState switch
    {
        MacRuntimeState.Ready => "可临时应用纯调色板主题",
        MacRuntimeState.ReadyUnqualified => "首次应用会执行安全资格验证",
        MacRuntimeState.CodexNotRunning => "请先手动打开 Codex",
        MacRuntimeState.IdentityChanged => "请重新检测后再应用",
        MacRuntimeState.Unavailable => "应用与恢复已阻断",
        _ => "正在刷新可信状态",
    };

    public MacOperationStage OperationStage
    {
        get => operationStage;
        private set
        {
            if (SetProperty(ref operationStage, value))
            {
                OnPropertyChanged(nameof(IsTemporaryApplied));
                OnPropertyChanged(nameof(IsSafeCleanup));
                OnPropertyChanged(nameof(OperationLabel));
                OnPropertyChanged(nameof(OperationDetail));
                NotifyActionState();
                NotifyActionPanelState();
            }
        }
    }

    public string OperationLabel => OperationStage switch
    {
        MacOperationStage.Qualifying => "正在完成首次资格验证",
        MacOperationStage.Applying => "正在临时应用主题",
        MacOperationStage.TemporaryApplied => "临时主题已应用",
        MacOperationStage.Restoring => "正在恢复官方外观",
        MacOperationStage.SafeCleanup => "正在安全收尾",
        MacOperationStage.Failed => "操作未完成",
        _ => "尚未应用临时主题",
    };

    public string OperationDetail => OperationStage switch
    {
        MacOperationStage.Qualifying =>
            "Inspector 会短时开启，并在操作结束时自动关闭。",
        MacOperationStage.Applying =>
            "正在验证唯一主窗口和主题应用证明。",
        MacOperationStage.TemporaryApplied =>
            "可随时恢复官方外观；背景图片未应用到 Codex。",
        MacOperationStage.Restoring =>
            "窗口关闭不会中断 Cleanup 和 Inspector finally。",
        MacOperationStage.SafeCleanup =>
            "此步骤不可中断。请等待状态变为已恢复或失败。",
        MacOperationStage.Failed =>
            "已保持 fail-closed，请按下方恢复建议处理。",
        _ => "选择主题后可进行一次可恢复的临时应用。",
    };

    public bool IsTemporaryApplied =>
        OperationStage == MacOperationStage.TemporaryApplied;

    public bool IsSafeCleanup =>
        OperationStage == MacOperationStage.SafeCleanup;

    public MacProofState ProofState
    {
        get => proofState;
        private set => SetProperty(ref proofState, value);
    }

    public bool IsCardView
    {
        get => isCardView;
        private set
        {
            if (SetProperty(ref isCardView, value))
            {
                OnPropertyChanged(nameof(IsListView));
                ShowCardsCommand.RaiseCanExecuteChanged();
                ShowListCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsListView => !IsCardView;

    public bool IsOperationActive
    {
        get => isOperationActive;
        private set
        {
            if (SetProperty(ref isOperationActive, value))
            {
                NotifyActionState();
                OnPropertyChanged(nameof(IsSafeToClose));
                NotifyActionPanelState();
            }
        }
    }

    public bool IsSafeToClose => !IsOperationActive;

    public bool CloseDeferred
    {
        get => closeDeferred;
        private set => SetProperty(ref closeDeferred, value);
    }

    public int CardColumnCount => cardColumnCount;

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public string CapabilityMessage
    {
        get => capabilityMessage;
        private set => SetProperty(ref capabilityMessage, value);
    }

    public string? PrimaryError
    {
        get => primaryError;
        private set
        {
            if (SetProperty(ref primaryError, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(PrimaryErrorTitle));
                OnPropertyChanged(nameof(PrimaryErrorMessage));
                OnPropertyChanged(nameof(RequiresManualCodexRestart));
                NotifyActionPanelState();
            }
        }
    }

    public string? RecoveryError
    {
        get => recoveryError;
        private set
        {
            if (SetProperty(ref recoveryError, value))
            {
                OnPropertyChanged(nameof(HasRecoveryError));
                OnPropertyChanged(nameof(RecoveryErrorTitle));
                OnPropertyChanged(nameof(RecoveryErrorMessage));
                OnPropertyChanged(nameof(RequiresManualCodexRestart));
                NotifyActionPanelState();
            }
        }
    }

    public bool HasError => PrimaryError is not null;

    public bool HasRecoveryError => RecoveryError is not null;

    public string PrimaryErrorTitle =>
        DescribeError(PrimaryError, recovery: false).Title;

    public string PrimaryErrorMessage =>
        DescribeError(PrimaryError, recovery: false).Message;

    public string RecoveryErrorTitle =>
        DescribeError(RecoveryError, recovery: true).Title;

    public string RecoveryErrorMessage =>
        DescribeError(RecoveryError, recovery: true).Message;

    public bool RequiresManualCodexRestart =>
        PrimaryError is "inspector.close_failed" or
            "operation.cleanup_unverified" or
            "renderer.cleanup_failed" ||
        RecoveryError is not null;

    public string ActionStatusText
    {
        get
        {
            if (HasRecoveryError)
            {
                return RecoveryErrorTitle;
            }

            if (OperationStage == MacOperationStage.SafeCleanup)
            {
                return OperationLabel;
            }

            if (HasError)
            {
                return PrimaryErrorTitle;
            }

            if (IsOperationActive ||
                OperationStage == MacOperationStage.TemporaryApplied)
            {
                return OperationLabel;
            }

            return RuntimeState switch
            {
                MacRuntimeState.Ready or MacRuntimeState.ReadyUnqualified =>
                    "已检测到 ChatGPT (Codex)",
                MacRuntimeState.CodexNotRunning =>
                    "未检测到 ChatGPT (Codex)",
                MacRuntimeState.IdentityChanged => "Codex 身份已变化",
                MacRuntimeState.Unavailable => "Codex Runtime 不可用",
                _ => "正在检测 ChatGPT (Codex)…",
            };
        }
    }

    public string ActionStatusDetail
    {
        get
        {
            if (HasRecoveryError)
            {
                return
                    $"主操作：{PrimaryErrorTitle}；恢复：{RecoveryErrorTitle}。{RecoveryErrorMessage}";
            }

            if (OperationStage == MacOperationStage.SafeCleanup)
            {
                return $"{OperationDetail} · 原因：{PrimaryErrorTitle}";
            }

            if (HasError)
            {
                return PrimaryErrorMessage;
            }

            return RuntimeState switch
            {
                MacRuntimeState.Ready or MacRuntimeState.ReadyUnqualified =>
                    "未知版本 · 能力探测兼容",
                MacRuntimeState.CodexNotRunning =>
                    "未检测到运行中的 Codex",
                MacRuntimeState.IdentityChanged =>
                    "身份已变化 · 需要重新检测",
                MacRuntimeState.Unavailable =>
                    "能力不兼容",
                _ => "正在确认兼容状态",
            };
        }
    }

    public bool IsActionStatusSuccess =>
        !HasError &&
        !HasRecoveryError &&
        RuntimeState is MacRuntimeState.Ready or
            MacRuntimeState.ReadyUnqualified;

    public bool IsActionStatusDanger =>
        OperationStage != MacOperationStage.SafeCleanup &&
        (HasError ||
        HasRecoveryError ||
        RuntimeState is MacRuntimeState.IdentityChanged or
            MacRuntimeState.Unavailable);

    public bool IsActionStatusWarning =>
        !IsActionStatusSuccess && !IsActionStatusDanger;

    public string PersistenceEligibilityMessage =>
        "macOS 首版仅支持可恢复的临时主题，持久主题将在 Agent 阶段提供。";

    public bool CanApply =>
        !IsOperationActive &&
        !HasError &&
        !HasRecoveryError &&
        SelectedTheme is not null &&
        RuntimeState is (
            MacRuntimeState.Ready or
            MacRuntimeState.ReadyUnqualified) &&
        OperationStage != MacOperationStage.TemporaryApplied;

    public bool CanRestore =>
        !IsOperationActive &&
        OperationStage == MacOperationStage.TemporaryApplied;

    public bool CanEdit =>
        !IsOperationActive &&
        SelectedTheme is not null &&
        IsThemeLibraryPage;

    public bool IsFilterEmpty =>
        LibraryState == MacLibraryState.Ready &&
        allThemes.Count > 0 &&
        VisibleThemes.Count == 0;

    public string ThemeCountLabel => LibraryState switch
    {
        MacLibraryState.Loading or MacLibraryState.Initializing => "正在载入",
        MacLibraryState.Error => "载入失败",
        _ => $"{VisibleThemes.Count} 个主题",
    };

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ClearErrors();
        LibraryState = MacLibraryState.Loading;
        StatusMessage = "正在载入 fixture 主题…";
        SelectedTheme = null;
        try
        {
            var library = await client.LoadLibraryAsync(cancellationToken);
            allThemes.Clear();
            allThemes.AddRange(
                library.Themes.Select(theme =>
                    new ThemeCardViewModel(
                        theme,
                        SelectTheme,
                        ToggleFavorite)));
            PopulateTagOptions();
            CapabilityMessage = library.CapabilityMessage;
            LibraryState = allThemes.Count == 0
                ? MacLibraryState.Empty
                : MacLibraryState.Ready;
            ApplyFilter();
            await RefreshRuntimeAsync(cancellationToken);
            StatusMessage = allThemes.Count == 0
                ? "主题资料库为空。"
                : $"已载入 {allThemes.Count} 个 fixture 主题。";
        }
        catch (OperationCanceledException)
        {
            LibraryState = MacLibraryState.Error;
            StatusMessage = "载入已取消。";
            PrimaryError = "library.load_cancelled";
        }
        catch (Exception)
        {
            LibraryState = MacLibraryState.Error;
            StatusMessage = "无法载入主题资料库。";
            PrimaryError = "library.load_failed";
            VisibleThemes.Clear();
            RebuildRows();
        }
    }

    public Task InitializeAsync() => InitializeAsync(CancellationToken.None);

    public void SelectTheme(ThemeCardViewModel theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (VisibleThemes.Contains(theme))
        {
            SelectedTheme = theme;
        }
    }

    public void SetEditorPreviewImage(Uri image)
    {
        if (!IsEditorPage || Editor is null)
        {
            return;
        }

        Editor.SetPreviewImage(image);
        NotifyEditorCommandState();
        StatusMessage = "背景已载入当前草稿；保存前不会更新主题资料库。";
    }

    private void OpenEditor()
    {
        if (!CanEdit || SelectedTheme is null)
        {
            return;
        }

        editorReturnPage = CurrentPage;
        Editor = new ThemeEditorViewModel(SelectedTheme);
        Editor.PropertyChanged += OnEditorPropertyChanged;
        CurrentPage = MacWorkspacePage.Editor;
        StatusMessage =
            "正在编辑本地 fixture 草稿；取消不会改变主题资料库或 Codex。";
        NotifyEditorCommandState();
    }

    private async Task SelectEditorImageAsync()
    {
        if (!IsEditorPage || Editor is null)
        {
            return;
        }

        var request = new EditorDialogRequestEventArgs<Uri?>();
        if (EditorImageRequested is null)
        {
            request.Complete(null);
        }
        else
        {
            EditorImageRequested.Invoke(this, request);
        }

        var image = await request.Completion;
        if (image is not null)
        {
            SetEditorPreviewImage(image);
        }
    }

    private async Task CancelEditorAsync()
    {
        if (!IsEditorPage)
        {
            return;
        }

        if (Editor?.IsDirty == true)
        {
            var request = new EditorDialogRequestEventArgs<bool>();
            if (EditorCancelConfirmationRequested is null)
            {
                request.Complete(false);
            }
            else
            {
                EditorCancelConfirmationRequested.Invoke(this, request);
            }

            if (!await request.Completion)
            {
                return;
            }
        }

        CloseEditor("已取消编辑；主题资料库和 Codex 均未改变。");
    }

    private void SaveEditor()
    {
        if (!IsEditorPage || Editor is null || SelectedTheme is null)
        {
            return;
        }

        SelectedTheme.ApplyEditor(Editor);
        OnPropertyChanged(nameof(SelectedThemeName));
        Editor.MarkSaved();
        ApplyFilter();
        CloseEditor("主题草稿已保存到当前 fixture 资料库。", keepSelection: true);
    }

    private async Task SaveCopyEditorAsync()
    {
        if (!IsEditorPage || Editor is null || SelectedTheme is null)
        {
            return;
        }

        var request = new EditorDialogRequestEventArgs<string?>();
        if (EditorCopyNameRequested is null)
        {
            request.Complete($"{Editor.Name} 副本");
        }
        else
        {
            EditorCopyNameRequested.Invoke(this, request);
        }

        var copyName = await request.Completion;
        if (string.IsNullOrWhiteSpace(copyName))
        {
            return;
        }

        var copyItem = SelectedTheme.CreateCopy(copyName, Editor);
        var copy = new ThemeCardViewModel(copyItem, SelectTheme, ToggleFavorite);
        copy.ApplyEditor(Editor, copyName);
        allThemes.Add(copy);
        ApplyFilter();
        SelectTheme(copy);
        CloseEditor("主题副本已保存到当前 fixture 资料库。", keepSelection: true);
    }

    private void CloseEditor(string message, bool keepSelection = true)
    {
        if (Editor is not null)
        {
            Editor.PropertyChanged -= OnEditorPropertyChanged;
        }

        Editor = null;
        CurrentPage = editorReturnPage;
        if (!keepSelection)
        {
            SelectedTheme = null;
        }

        StatusMessage = message;
        NotifyEditorCommandState();
    }

    private void OnEditorPropertyChanged(
        object? sender,
        PropertyChangedEventArgs args) => NotifyEditorCommandState();

    private void NotifyEditorCommandState()
    {
        CancelEditorCommand.RaiseCanExecuteChanged();
        SelectEditorImageCommand.RaiseCanExecuteChanged();
        SaveEditorCommand.RaiseCanExecuteChanged();
        SaveCopyEditorCommand.RaiseCanExecuteChanged();
        EditCommand.RaiseCanExecuteChanged();
    }

    private void ToggleFavorite(ThemeCardViewModel theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        theme.IsFavorite = !theme.IsFavorite;
        if (IsFavoritesPage ||
            string.Equals(
                SelectedScope,
                ScopeFavorites,
                StringComparison.Ordinal))
        {
            ApplyFilter();
        }
    }

    public void SetCardColumnCount(int value)
    {
        var normalized = Math.Clamp(value, 1, 3);
        if (cardColumnCount == normalized)
        {
            return;
        }

        cardColumnCount = normalized;
        OnPropertyChanged(nameof(CardColumnCount));
        RebuildRows();
    }

    public void NotifyCloseDeferred()
    {
        if (!IsOperationActive)
        {
            return;
        }

        CloseDeferred = true;
        StatusMessage =
            "窗口将在安全收尾完成后关闭；Cleanup 与 Inspector finally 不会被中断。";
    }

    private async Task RefreshAsync()
    {
        try
        {
            await RefreshRuntimeAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            RuntimeState = MacRuntimeState.Unavailable;
            PrimaryError = "runtime.refresh_failed";
            StatusMessage = "无法刷新 Codex 状态。";
        }
    }

    private async Task RefreshRuntimeAsync(CancellationToken cancellationToken)
    {
        var snapshot = await client.RefreshRuntimeAsync(cancellationToken);
        RuntimeState = snapshot.State;
        StatusMessage = snapshot.UserMessage;
    }

    private async Task ApplyAsync()
    {
        if (SelectedTheme is null || !CanApply)
        {
            return;
        }

        ClearErrors();
        IsOperationActive = true;
        ProofState = MacProofState.Pending;
        OperationStage = RuntimeState == MacRuntimeState.ReadyUnqualified
            ? MacOperationStage.Qualifying
            : MacOperationStage.Applying;
        StatusMessage = OperationDetail;

        try
        {
            var handle = await client.BeginApplyTemporaryAsync(
                SelectedTheme.ThemeId,
                CancellationToken.None);
            var outcome = await handle.Completion;
            ApplyOutcome(outcome, applying: true);
        }
        catch (OperationCanceledException)
        {
            OperationStage = MacOperationStage.Failed;
            ProofState = MacProofState.Unverified;
            PrimaryError = "operation.cancelled_before_dispatch";
            StatusMessage = "操作在正式执行前已取消。";
        }
        finally
        {
            IsOperationActive = false;
        }
    }

    private async Task RestoreAsync()
    {
        if (!CanRestore)
        {
            return;
        }

        ClearErrors();
        IsOperationActive = true;
        ProofState = MacProofState.Pending;
        OperationStage = MacOperationStage.Restoring;
        StatusMessage = OperationDetail;

        try
        {
            var handle = await client.BeginRestoreAsync(CancellationToken.None);
            var outcome = await handle.Completion;
            ApplyOutcome(outcome, applying: false);
        }
        catch (OperationCanceledException)
        {
            OperationStage = MacOperationStage.SafeCleanup;
            ProofState = MacProofState.Unverified;
            PrimaryError = "operation.cleanup_unverified";
            StatusMessage = "正在安全收尾，请稍候。";
        }
        finally
        {
            IsOperationActive = false;
        }
    }

    private void ApplyOutcome(
        MacThemeOperationOutcome outcome,
        bool applying)
    {
        ProofState = outcome.CleanupProof;
        PrimaryError = outcome.ErrorCode;
        RecoveryError = outcome.RecoveryErrorCode;
        if (outcome.IsSuccess)
        {
            OperationStage = outcome.FinalStage;
            RuntimeState = MacRuntimeState.Ready;
            StatusMessage = applying
                ? "临时主题已应用；背景图片仍仅在 Studio 内预览。"
                : "已恢复官方外观并完成安全收尾。";
            return;
        }

        OperationStage = outcome.CleanupProof == MacProofState.Pending
            ? MacOperationStage.SafeCleanup
            : MacOperationStage.Failed;
        StatusMessage = outcome.RecoveryErrorCode is null
            ? "操作未完成；已保持 fail-closed。"
            : "操作和恢复均未取得证明，请完全退出并重新打开 Codex。";
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        IEnumerable<ThemeCardViewModel> filtered = allThemes;
        filtered = filtered.Where(theme =>
            query.Length == 0 ||
            theme.DisplayName.Contains(
                query,
                StringComparison.CurrentCultureIgnoreCase) ||
            theme.Subtitle.Contains(
                query,
                StringComparison.CurrentCultureIgnoreCase) ||
            theme.Tags.Contains(
                query,
                StringComparison.CurrentCultureIgnoreCase));

        filtered = SelectedScope switch
        {
            ScopeFavorites => filtered.Where(theme => theme.IsFavorite),
            ScopeBackground => filtered.Where(
                theme => theme.HasLocalBackgroundPreview),
            _ => filtered,
        };

        if (!string.Equals(SelectedTag, TagAll, StringComparison.Ordinal))
        {
            filtered = filtered.Where(theme =>
                theme.TagItems.Contains(
                    SelectedTag,
                    StringComparer.CurrentCultureIgnoreCase));
        }

        filtered = SelectedSort switch
        {
            SortName => filtered
                .OrderBy(
                    theme => theme.DisplayName,
                    StringComparer.CurrentCulture),
            SortFavorites => filtered
                .OrderByDescending(theme => theme.IsFavorite)
                .ThenByDescending(theme => theme.LastUsedAtUtc)
                .ThenBy(
                    theme => theme.DisplayName,
                    StringComparer.CurrentCulture),
            _ => filtered
                .OrderByDescending(theme => theme.LastUsedAtUtc)
                .ThenBy(
                    theme => theme.DisplayName,
                    StringComparer.CurrentCulture),
        };

        VisibleThemes.Clear();
        foreach (var theme in filtered)
        {
            VisibleThemes.Add(theme);
        }

        if (SelectedTheme is not null &&
            !VisibleThemes.Contains(SelectedTheme))
        {
            SelectedTheme = null;
        }

        RebuildRows();
        OnPropertyChanged(nameof(IsFilterEmpty));
        OnPropertyChanged(nameof(ThemeCountLabel));
        OnPropertyChanged(nameof(PageSubtitle));
    }

    private void PopulateTagOptions()
    {
        var previous = SelectedTag;
        TagOptions.Clear();
        TagOptions.Add(TagAll);
        foreach (var tag in allThemes
                     .SelectMany(theme => theme.TagItems)
                     .Distinct(StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(tag => tag, StringComparer.CurrentCulture))
        {
            TagOptions.Add(tag);
        }

        SelectedTag = TagOptions.Contains(previous)
            ? previous
            : TagAll;
    }

    private void RebuildRows()
    {
        VisibleThemeRows.Clear();
        for (var index = 0; index < VisibleThemes.Count; index += cardColumnCount)
        {
            VisibleThemeRows.Add(
                new ThemeCardRowViewModel(
                    VisibleThemes
                        .Skip(index)
                        .Take(cardColumnCount)
                        .ToArray(),
                    cardColumnCount));
        }
    }

    private void ClearErrors()
    {
        PrimaryError = null;
        RecoveryError = null;
    }

    private void NotifyActionState()
    {
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanRestore));
        ApplyCommand.RaiseCanExecuteChanged();
        RestoreCommand.RaiseCanExecuteChanged();
        RefreshCommand.RaiseCanExecuteChanged();
        EditCommand.RaiseCanExecuteChanged();
        NotifyEditorCommandState();
    }

    private void NotifyActionPanelState()
    {
        OnPropertyChanged(nameof(ActionStatusText));
        OnPropertyChanged(nameof(ActionStatusDetail));
        OnPropertyChanged(nameof(IsActionStatusSuccess));
        OnPropertyChanged(nameof(IsActionStatusWarning));
        OnPropertyChanged(nameof(IsActionStatusDanger));
    }

    private static (string Title, string Message) DescribeError(
        string? code,
        bool recovery) =>
        code switch
        {
            null => (string.Empty, string.Empty),
            "library.load_failed" => (
                "无法载入主题资料库",
                "fixture 数据没有通过载入，请重试。"),
            "library.load_cancelled" => (
                "主题载入已取消",
                "可以重新载入；当前没有主题被应用。"),
            "runtime.refresh_failed" => (
                "无法检测 Codex",
                "应用已被阻断，请稍后重新检测。"),
            "process.identity_changed" => (
                "Codex 身份已变化",
                "请重新检测并重新取得资格后再应用。"),
            "inspector.close_failed" => (
                "无法确认 Inspector 已关闭",
                "请完全退出并重新打开 Codex，再重新检测。"),
            "renderer.cleanup_failed" or "operation.cleanup_unverified" => (
                recovery ? "恢复未取得证明" : "清理未取得证明",
                "请完全退出并重新打开 Codex，以确保没有受管状态残留。"),
            "operation.cancelled_before_dispatch" => (
                "操作已取消",
                "取消发生在可能产生副作用之前。"),
            _ => (
                recovery ? "恢复操作未完成" : "操作未完成",
                "已保持 fail-closed。请打开诊断页查看脱敏错误码。"),
        };
}
