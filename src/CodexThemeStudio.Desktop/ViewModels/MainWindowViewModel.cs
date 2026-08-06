using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CodexThemeStudio.CodexAdapter;
using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;
using CodexThemeStudio.Desktop.Infrastructure;
using CodexThemeStudio.Desktop.Services;
using DiagnosticSource = CodexThemeStudio.Contracts.Models.DiagnosticSource;

namespace CodexThemeStudio.Desktop.ViewModels;

public enum LibraryPage
{
    Current,
    All,
    Favorites,
    Trash,
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

public enum ThemeLibraryLayout
{
    Cards,
    List,
}

public enum ThemeScope
{
    All,
    Current,
}

public enum SettingsSection
{
    General,
    Diagnostics,
    About,
}

public enum CodexDetectionPhase
{
    Cached = 0,
    Confirming,
    Live,
    Error,
}

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private const string GitHubRepositoryUrl = "https://github.com/Aenvo/Codex-Theme-Studio";
    private static readonly TimeSpan PresencePollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PresencePollDuration = TimeSpan.FromSeconds(20);

    private readonly IThemeRepository repository;
    private readonly ICodexThemeRuntime runtime;
    private readonly IPersistenceService persistence;
    private readonly IStorageLocationService storageLocation;
    private readonly IThemePackageService? packageService;
    private readonly IUserDialogService dialogs;
    private readonly Func<string?, string?> resolveThumbnail;
    private readonly IExternalPersistenceService? externalPersistence;
    private readonly ICodexTargetSelectionService? codexTargetSelection;
    private readonly IDiagnosticEventSink? diagnosticSink;
    private readonly IDiagnosticQueryService? diagnosticQuery;
    private readonly IDiagnosticBundleService? diagnosticBundle;
    private readonly Action<string>? copyText;
    private readonly Action<string> openExternalUrl;
    private readonly string appVersion;
    private readonly Guid diagnosticSessionId;
    private readonly ExternalThemeCatalogService? externalThemeCatalog;
    private readonly ICodexDiscoveryService? codexDiscovery;
    private readonly IUpdateService? updateService;
    private readonly IUpdateDialogService? updateDialogs;
    private readonly IUpdateInstaller? updateInstaller;
    private readonly Func<long, OperationResult>? updatePreflight;
    private readonly Action? requestApplicationShutdown;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim presenceCheckLock = new(1, 1);
    private readonly object presenceMonitorSync = new();
    private readonly List<ThemeCardViewModel> allThemes = [];
    private readonly List<ThemeCardViewModel> deletedThemes = [];
    private readonly ObservableCollection<ThemeCardViewModel> visibleThemes = [];
    private readonly ObservableCollection<string> availableTags = ["全部标签"];
    private readonly ObservableCollection<DiagnosticEventViewModel> diagnosticEvents = [];
    private ThemeCardViewModel? selectedTheme;
    private string searchText = string.Empty;
    private string selectedTag = "全部标签";
    private ThemeSort selectedSort = ThemeSort.RecentlyModified;
    private LibraryPage currentPage = LibraryPage.All;
    private bool isBusy;
    private string operationText = string.Empty;
    private string notificationMessage = "主题资料库已准备就绪。";
    private string notificationKind = "Info";
    private bool isNotificationVisible;
    private int notificationVersion;
    private string codexStatusText = "正在检测 ChatGPT (Codex)…";
    private string codexStatusDetail = "状态由本地应用服务提供。";
    private bool isCodexDetected;
    private string dataRoot = "正在读取…";
    private string themeCountText = "0 个主题";
    private ThemeRuntimeStatus? runtimeStatus;
    private bool isPersistenceEnabled;
    private string storageUsageText = "正在计算…";
    private string storageStatusText = "正在检查数据位置…";
    private bool isStorageAvailable;
    private bool externalThemeSuppressedForCurrentProcess;
    private ThemeLibraryLayout selectedLibraryLayout = ThemeLibraryLayout.Cards;
    private ThemeScope selectedScope = ThemeScope.All;
    private string codexTargetPath = "自动检测 Microsoft Store Codex";
    private string codexTargetFingerprint = "尚未检测";
    private string codexCompatibilityText = "尚未完成能力探测";
    private bool isPersistenceEligible = true;
    private string persistenceEligibilityMessage = string.Empty;
    private ExternalThemeDescriptor? externalTheme;
    private SettingsSection selectedSettingsSection;
    private DiagnosticSnapshot? diagnosticSnapshot;
    private DiagnosticHealth diagnosticHealth = DiagnosticHealth.Empty;
    private string diagnosticStatusText = "尚未读取诊断记录";
    private string diagnosticLastUpdatedText = "最近更新：暂无";
    private string diagnosticLatestErrorText = "最近错误：无";
    private string diagnosticLatestRecoveryText = "最近恢复：无";
    private string diagnosticLogStatusText = "日志状态：尚未检查";
    private string? codexExecutableSha256;
    private int? observedCodexProcessId;
    private DateTimeOffset? observedCodexProcessStartedAtUtc;
    private string activeDiagnosticOperation = "desktop.background";
    private Guid? activeDiagnosticCorrelationId;
    private bool activeDiagnosticOperationFailed;
    private CodexDetectionPhase codexDetectionPhase = CodexDetectionPhase.Confirming;
    private bool hasLiveCodexIdentity;
    private Task backgroundInitialization = Task.CompletedTask;
    private Task presenceMonitorTask = Task.CompletedTask;
    private CancellationTokenSource? presenceMonitorCancellation;
    private bool isWindowActive;
    private bool isInitialized;
    private bool isBackgroundInitializationComplete;
    private int statusRefreshGeneration;
    private int isDisposed;
    private bool isCheckingForUpdates;
    private UpdateReleaseInfo? availableUpdate;
    private Task updateCheckTask = Task.CompletedTask;

    public MainWindowViewModel(
        IThemeRepository repository,
        ICodexThemeRuntime runtime,
        IPersistenceService persistence,
        IStorageLocationService storageLocation,
        IUserDialogService dialogs,
        Func<string?, string?> resolveThumbnail,
        IThemePackageService? packageService = null,
        ThemeEditorViewModel? editor = null,
        ExternalThemeDescriptor? externalTheme = null,
        IExternalPersistenceService? externalPersistence = null,
        ICodexTargetSelectionService? codexTargetSelection = null,
        IDiagnosticEventSink? diagnosticSink = null,
        IDiagnosticQueryService? diagnosticQuery = null,
        IDiagnosticBundleService? diagnosticBundle = null,
        Action<string>? copyText = null,
        string appVersion = "unknown",
        Guid? diagnosticSessionId = null,
        ExternalThemeCatalogService? externalThemeCatalog = null,
        Action<string>? openExternalUrl = null,
        ICodexDiscoveryService? codexDiscovery = null,
        IUpdateService? updateService = null,
        IUpdateDialogService? updateDialogs = null,
        IUpdateInstaller? updateInstaller = null,
        Func<long, OperationResult>? updatePreflight = null,
        Action? requestApplicationShutdown = null)
    {
        this.repository = repository;
        this.runtime = runtime;
        this.persistence = persistence;
        this.storageLocation = storageLocation;
        this.packageService = packageService;
        this.dialogs = dialogs;
        this.resolveThumbnail = resolveThumbnail;
        this.externalTheme = externalTheme;
        this.externalPersistence = externalPersistence;
        this.codexTargetSelection = codexTargetSelection;
        this.diagnosticSink = diagnosticSink;
        this.diagnosticQuery = diagnosticQuery;
        this.diagnosticBundle = diagnosticBundle;
        this.copyText = copyText;
        this.openExternalUrl = openExternalUrl ?? OpenExternalUrl;
        this.appVersion = appVersion;
        this.diagnosticSessionId = diagnosticSessionId ?? Guid.NewGuid();
        this.externalThemeCatalog = externalThemeCatalog;
        this.codexDiscovery = codexDiscovery;
        this.updateService = updateService;
        this.updateDialogs = updateDialogs;
        this.updateInstaller = updateInstaller;
        this.updatePreflight = updatePreflight;
        this.requestApplicationShutdown = requestApplicationShutdown;
        Editor = editor;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        CreateCommand = new AsyncRelayCommand(CreateAsync, () => !IsBusy);
        CopyCommand = new AsyncRelayCommand(
            CopyAsync,
            CanUseActiveSelection);
        RenameCommand = new AsyncRelayCommand(RenameAsync, CanMutateSelection);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, CanDeleteSelection);
        ToggleFavoriteCommand = new AsyncRelayCommand<ThemeCardViewModel>(
            ToggleFavoriteAsync,
            CanToggleFavorite);
        ApplyTemporaryCommand = new AsyncRelayCommand(
            ApplyTemporaryAsync,
            CanUseActiveSelection);
        SetPersistentCommand = new AsyncRelayCommand(
            SetPersistentAsync,
            CanUseActiveSelection);
        RestoreCommand = new AsyncRelayCommand(
            RestoreAsync,
            () => !IsBusy);
        NavigateCommand = new RelayCommand(Navigate);
        ImportCommand = new AsyncRelayCommand(ImportAsync, () => !IsBusy && packageService is not null);
        EditCommand = new AsyncRelayCommand(
            EditAsync,
            CanUseActiveSelection);
        SelectImageCommand = new AsyncRelayCommand(SelectImageAsync, () => !IsBusy && Editor?.HasDraft == true);
        SaveDraftCommand = new AsyncRelayCommand(() => SaveDraftAsync(false), () => !IsBusy && Editor?.HasDraft == true);
        SaveCopyCommand = new AsyncRelayCommand(() => SaveDraftAsync(true), () => !IsBusy && Editor?.HasDraft == true);
        CancelDraftCommand = new AsyncRelayCommand(CancelDraftAsync, () => !IsBusy && Editor?.HasDraft == true);
        ExportCommand = new AsyncRelayCommand(
            ExportAsync,
            () => CanUseActiveSelection() && packageService is not null);
        RestoreDeletedCommand = new AsyncRelayCommand(
            RestoreDeletedAsync,
            CanUseTrashSelection);
        PermanentlyDeleteCommand = new AsyncRelayCommand(
            PermanentlyDeleteAsync,
            CanUseTrashSelection);
        EmptyTrashCommand = new AsyncRelayCommand(
            EmptyTrashAsync,
            () => CurrentPage == LibraryPage.Trash && deletedThemes.Count > 0 && !IsBusy);
        MigrateStorageCommand = new AsyncRelayCommand(
            MigrateStorageAsync,
            () => !IsBusy && isStorageAvailable);
        OpenDataRootCommand = new RelayCommand(_ => OpenDataRoot(), _ => Directory.Exists(DataRoot));
        OpenGitHubRepositoryCommand = new RelayCommand(_ => OpenGitHubRepository());
        SetLibraryLayoutCommand = new RelayCommand(SetLibraryLayout);
        SelectCodexExecutableCommand = new AsyncRelayCommand(
            SelectCodexExecutableAsync,
            () => !IsBusy && codexTargetSelection is not null);
        RedetectCodexCommand = new AsyncRelayCommand(
            RedetectCodexAsync,
            () => !IsBusy);
        ResetCodexTargetCommand = new AsyncRelayCommand(
            ResetCodexTargetAsync,
            () => !IsBusy && codexTargetSelection is not null);
        RefreshDiagnosticsCommand = new AsyncRelayCommand(
            RefreshDiagnosticsAsync,
            () => !IsBusy && diagnosticQuery is not null);
        OpenDiagnosticLogCommand = new RelayCommand(
            _ => OpenDiagnosticLogDirectory(),
            _ => diagnosticSnapshot?.LogDirectoryAvailable == true);
        CopyDiagnosticSummaryCommand = new AsyncRelayCommand(
            CopyDiagnosticSummaryAsync,
            () => !IsBusy && diagnosticBundle is not null && copyText is not null);
        ExportDiagnosticBundleCommand = new AsyncRelayCommand(
            ExportDiagnosticBundleAsync,
            () => !IsBusy && diagnosticBundle is not null);
        CheckUpdatesCommand = new AsyncRelayCommand(
            () => CheckForUpdatesAsync(userInitiated: true),
            () => !IsCheckingForUpdates && updateService is not null);
        NavigateToUpdateCommand = new RelayCommand(_ => NavigateToUpdate());
    }

    public ReadOnlyObservableCollection<ThemeCardViewModel> Themes =>
        new(visibleThemes);

    public ReadOnlyObservableCollection<string> AvailableTags =>
        new(availableTags);

    public IReadOnlyList<ThemeSort> SortOptions { get; } =
        Enum.GetValues<ThemeSort>();

    public IReadOnlyList<ThemeScope> ScopeOptions { get; } =
        Enum.GetValues<ThemeScope>();

    public ReadOnlyObservableCollection<DiagnosticEventViewModel> DiagnosticEvents =>
        new(diagnosticEvents);

    public ThemeScope SelectedScope
    {
        get => selectedScope;
        set
        {
            if (SetProperty(ref selectedScope, value))
            {
                ApplyFilter();
            }
        }
    }

    public ThemeLibraryLayout SelectedLibraryLayout
    {
        get => selectedLibraryLayout;
        private set
        {
            if (SetProperty(ref selectedLibraryLayout, value))
            {
                OnPropertyChanged(nameof(IsCardView));
                OnPropertyChanged(nameof(IsListView));
            }
        }
    }

    public bool IsCardView => SelectedLibraryLayout == ThemeLibraryLayout.Cards;

    public bool IsListView => SelectedLibraryLayout == ThemeLibraryLayout.List;

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
                OnPropertyChanged(nameof(IsTrashVisible));
                OnPropertyChanged(nameof(IsSettingsVisible));
                OnPropertyChanged(nameof(IsEditorVisible));
                OnPropertyChanged(nameof(PageTitle));
            }
        }
    }

    public bool IsLibraryVisible =>
        CurrentPage is not LibraryPage.Settings and
            not LibraryPage.Editor and
            not LibraryPage.Trash;

    public bool IsTrashVisible => CurrentPage == LibraryPage.Trash;

    public bool IsSettingsVisible => CurrentPage == LibraryPage.Settings;

    public bool IsEditorVisible => CurrentPage == LibraryPage.Editor;

    public string PageTitle => CurrentPage switch
    {
        LibraryPage.Favorites => "我的收藏",
        LibraryPage.Trash => "回收站",
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

    public bool IsNotificationVisible
    {
        get => isNotificationVisible;
        private set => SetProperty(ref isNotificationVisible, value);
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

    public bool IsCodexDetected
    {
        get => isCodexDetected;
        private set => SetProperty(ref isCodexDetected, value);
    }

    public CodexDetectionPhase CodexStatusPhase
    {
        get => codexDetectionPhase;
        private set
        {
            if (SetProperty(ref codexDetectionPhase, value))
            {
                OnPropertyChanged(nameof(IsCodexStatusConfirming));
            }
        }
    }

    public bool IsCodexStatusConfirming =>
        CodexStatusPhase == CodexDetectionPhase.Confirming;

    public string CodexTargetPath
    {
        get => codexTargetPath;
        private set => SetProperty(ref codexTargetPath, value);
    }

    public string CodexTargetFingerprint
    {
        get => codexTargetFingerprint;
        private set => SetProperty(ref codexTargetFingerprint, value);
    }

    public string CodexCompatibilityText
    {
        get => codexCompatibilityText;
        private set => SetProperty(ref codexCompatibilityText, value);
    }

    public bool IsPersistenceEligible
    {
        get => isPersistenceEligible;
        private set
        {
            if (SetProperty(ref isPersistenceEligible, value))
            {
                NotifyCommands();
            }
        }
    }

    public string PersistenceEligibilityMessage
    {
        get => persistenceEligibilityMessage;
        private set => SetProperty(ref persistenceEligibilityMessage, value);
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

    public SettingsSection SelectedSettingsSection
    {
        get => selectedSettingsSection;
        set
        {
            if (SetProperty(ref selectedSettingsSection, value) &&
                value == SettingsSection.Diagnostics)
            {
                _ = RefreshDiagnosticsAsync();
            }
        }
    }

    public string AppVersion => appVersion;

    public bool IsCheckingForUpdates
    {
        get => isCheckingForUpdates;
        private set
        {
            if (SetProperty(ref isCheckingForUpdates, value))
            {
                OnPropertyChanged(nameof(CheckUpdateButtonText));
                CheckUpdatesCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string CheckUpdateButtonText =>
        IsCheckingForUpdates ? "正在检查" : "检查更新";

    public bool IsUpdateAvailable => availableUpdate is not null;

    public DiagnosticHealth DiagnosticHealth
    {
        get => diagnosticHealth;
        private set => SetProperty(ref diagnosticHealth, value);
    }

    public string DiagnosticStatusText
    {
        get => diagnosticStatusText;
        private set => SetProperty(ref diagnosticStatusText, value);
    }

    public string DiagnosticLastUpdatedText
    {
        get => diagnosticLastUpdatedText;
        private set => SetProperty(ref diagnosticLastUpdatedText, value);
    }

    public string DiagnosticLatestErrorText
    {
        get => diagnosticLatestErrorText;
        private set => SetProperty(ref diagnosticLatestErrorText, value);
    }

    public string DiagnosticLatestRecoveryText
    {
        get => diagnosticLatestRecoveryText;
        private set => SetProperty(ref diagnosticLatestRecoveryText, value);
    }

    public string DiagnosticLogStatusText
    {
        get => diagnosticLogStatusText;
        private set => SetProperty(ref diagnosticLogStatusText, value);
    }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand CreateCommand { get; }

    public AsyncRelayCommand CopyCommand { get; }

    public AsyncRelayCommand RenameCommand { get; }

    public AsyncRelayCommand DeleteCommand { get; }

    public AsyncRelayCommand<ThemeCardViewModel> ToggleFavoriteCommand { get; }

    public AsyncRelayCommand ApplyTemporaryCommand { get; }

    public AsyncRelayCommand SetPersistentCommand { get; }

    public AsyncRelayCommand SelectCodexExecutableCommand { get; }

    public AsyncRelayCommand RedetectCodexCommand { get; }

    public AsyncRelayCommand ResetCodexTargetCommand { get; }

    public AsyncRelayCommand RestoreCommand { get; }

    public RelayCommand NavigateCommand { get; }

    public AsyncRelayCommand ImportCommand { get; }

    public AsyncRelayCommand EditCommand { get; }

    public AsyncRelayCommand SelectImageCommand { get; }

    public AsyncRelayCommand SaveDraftCommand { get; }

    public AsyncRelayCommand SaveCopyCommand { get; }

    public AsyncRelayCommand CancelDraftCommand { get; }

    public AsyncRelayCommand ExportCommand { get; }

    public AsyncRelayCommand RestoreDeletedCommand { get; }

    public AsyncRelayCommand PermanentlyDeleteCommand { get; }

    public AsyncRelayCommand EmptyTrashCommand { get; }

    public AsyncRelayCommand MigrateStorageCommand { get; }

    public RelayCommand OpenDataRootCommand { get; }

    public RelayCommand OpenGitHubRepositoryCommand { get; }

    public RelayCommand SetLibraryLayoutCommand { get; }

    public AsyncRelayCommand RefreshDiagnosticsCommand { get; }

    public RelayCommand OpenDiagnosticLogCommand { get; }

    public AsyncRelayCommand CopyDiagnosticSummaryCommand { get; }

    public AsyncRelayCommand ExportDiagnosticBundleCommand { get; }

    public AsyncRelayCommand CheckUpdatesCommand { get; }

    public RelayCommand NavigateToUpdateCommand { get; }

    public async Task InitializeAsync()
    {
        await ApplyCachedCompatibilityAsync(lifetime.Token);
        var correlationId = Guid.NewGuid();
        await WriteDiagnosticAsync(
            DiagnosticLevel.Information,
            "desktop.theme_load.started",
            DiagnosticOutcome.Started,
            "desktop.theme_load",
            correlationId);
        await LoadThemesAsync();
        await WriteDiagnosticAsync(
            DiagnosticLevel.Information,
            "desktop.theme_load.completed",
            DiagnosticOutcome.Succeeded,
            "desktop.theme_load",
            correlationId);
        backgroundInitialization = CompleteBackgroundInitializationAsync(lifetime.Token);
        updateCheckTask = CheckForUpdatesAfterStartupAsync(lifetime.Token);
        isInitialized = true;
    }

    internal Task WaitForBackgroundInitializationAsync() => backgroundInitialization;

    internal Task WaitForUpdateCheckAsync() => updateCheckTask;

    internal Task WaitForPresenceMonitorAsync()
    {
        lock (presenceMonitorSync)
        {
            return presenceMonitorTask;
        }
    }

    public void OnWindowActivated()
    {
        isWindowActive = true;
        if (isInitialized)
        {
            StartPresenceMonitor();
        }
    }

    public void OnWindowDeactivated()
    {
        isWindowActive = false;
        CancelPresenceMonitor();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        lifetime.Cancel();
        CancelPresenceMonitor();
        lifetime.Dispose();
    }

    public async Task HandleUpdateInstallResultAsync(UpdateInstallResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Outcome == UpdateInstallOutcome.Succeeded)
        {
            var agent = await persistence.UpgradeAgentAsync(lifetime.Token);
            if (!agent.IsSuccess)
            {
                var retry = await dialogs.ChooseActionAsync(
                    "持久化服务升级失败",
                    "应用已更新，但持久化服务升级失败；旧服务已保留并继续运行。",
                    "关闭",
                    "重试升级",
                    lifetime.Token);
                if (retry)
                {
                    agent = await persistence.UpgradeAgentAsync(lifetime.Token);
                    if (!agent.IsSuccess)
                    {
                        dialogs.ShowInformation(
                            "持久化服务仍未升级",
                            "应用保持新版，旧持久化服务继续保留并运行。请检查诊断日志后再试。");
                    }
                }
            }

            Notify($"更新成功，已升级至 v{result.NewVersion}", "Success");
            if (!string.IsNullOrWhiteSpace(result.PreservedDirectory))
            {
                dialogs.ShowInformation(
                    "已保留旧目录中的文件",
                    $"未知或被修改的旧文件未被删除，已移动到：\n{result.PreservedDirectory}");
            }
            return;
        }

        if (result.Outcome == UpdateInstallOutcome.CleanupCompleted)
        {
            Notify("旧文件清理已完成", "Success");
            if (!string.IsNullOrWhiteSpace(result.PreservedDirectory))
            {
                dialogs.ShowInformation(
                    "已保留旧目录中的文件",
                    $"未知或被修改的旧文件已移动到：\n{result.PreservedDirectory}");
            }
            return;
        }

        if (result.Outcome == UpdateInstallOutcome.CleanupIncomplete)
        {
            var retryCleanup = await dialogs.ChooseActionAsync(
                "旧文件清理未完成",
                $"{result.UserMessage}\n\n旧目录仍保留在：\n{result.BackupDirectory}",
                "打开旧目录",
                "重试清理",
                lifetime.Token);
            if (retryCleanup && updateInstaller is not null && result.Token is not null)
            {
                var retried = await updateInstaller.RetryCleanupAsync(
                    result.Token,
                    lifetime.Token);
                if (retried.IsSuccess)
                {
                    await HandleUpdateInstallResultAsync(retried.Value!);
                }
                else
                {
                    dialogs.ShowInformation("重试清理失败", retried.Error!.UserMessage);
                }
            }
            else if (!string.IsNullOrWhiteSpace(result.BackupDirectory))
            {
                OpenDirectory(result.BackupDirectory);
            }
            return;
        }

        if (result.Outcome == UpdateInstallOutcome.RollbackIncomplete)
        {
            var openGitHub = await dialogs.ChooseActionAsync(
                "更新失败且回滚未完成",
                $"{result.UserMessage}\n\n备份目录：\n{result.BackupDirectory}",
                "打开备份目录",
                "前往 GitHub",
                lifetime.Token);
            if (openGitHub) openExternalUrl(GitHubRepositoryUrl);
            else if (!string.IsNullOrWhiteSpace(result.BackupDirectory))
                OpenDirectory(result.BackupDirectory);
            return;
        }

        if (result.Outcome == UpdateInstallOutcome.RolledBack)
        {
            var retry = await dialogs.ChooseActionAsync(
                "更新已回滚",
                result.UserMessage,
                "前往 GitHub",
                "重试",
                lifetime.Token);
            if (retry)
            {
                NavigateToUpdate();
                CheckUpdatesCommand.Execute(null);
            }
            else
            {
                openExternalUrl(GitHubRepositoryUrl);
            }
            return;
        }

        dialogs.ShowInformation(
            "更新失败",
            string.IsNullOrWhiteSpace(result.BackupDirectory)
                ? result.UserMessage
                : $"{result.UserMessage}\n\n备份目录：\n{result.BackupDirectory}");
    }

    public void ReportUpdateResultReadFailure() =>
        dialogs.ShowInformation(
            "无法读取更新结果",
            "应用已启动，但无法验证更新清理结果。请前往 GitHub 获取帮助，并保留 Updates 目录。");

    private static void OpenDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return;
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                ArgumentList = { path },
                UseShellExecute = false,
            });
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private void StartPresenceMonitor()
    {
        if (codexDiscovery is null ||
            !isWindowActive ||
            !isBackgroundInitializationComplete ||
            Volatile.Read(ref isDisposed) != 0)
        {
            return;
        }

        lock (presenceMonitorSync)
        {
            if (presenceMonitorCancellation is
                {
                    IsCancellationRequested: false,
                })
            {
                return;
            }

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                lifetime.Token);
            presenceMonitorCancellation = cancellation;
            presenceMonitorTask = MonitorCodexPresenceAsync(cancellation);
        }
    }

    private void CancelPresenceMonitor()
    {
        CancellationTokenSource? cancellation;
        lock (presenceMonitorSync)
        {
            cancellation = presenceMonitorCancellation;
        }

        cancellation?.Cancel();
    }

    private async Task MonitorCodexPresenceAsync(
        CancellationTokenSource cancellation)
    {
        var cancellationToken = cancellation.Token;
        var deadline = DateTimeOffset.UtcNow + PresencePollDuration;
        try
        {
            while (isWindowActive &&
                   DateTimeOffset.UtcNow <= deadline &&
                   !cancellationToken.IsCancellationRequested)
            {
                if (!IsBusy)
                {
                    var result = await CheckCodexPresenceAsync(cancellationToken);
                    if (result == PresenceCheckResult.Complete)
                    {
                        return;
                    }
                }

                await Task.Delay(PresencePollInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            lock (presenceMonitorSync)
            {
                if (ReferenceEquals(presenceMonitorCancellation, cancellation))
                {
                    presenceMonitorCancellation = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private async Task<PresenceCheckResult> CheckCodexPresenceAsync(
        CancellationToken cancellationToken)
    {
        if (codexDiscovery is null ||
            !await presenceCheckLock.WaitAsync(0, cancellationToken))
        {
            return PresenceCheckResult.Continue;
        }

        try
        {
            var discovery = await codexDiscovery.DiscoverAsync(cancellationToken);
            if (!discovery.IsSuccess)
            {
                if (discovery.Error!.Code != OperationErrorCode.CodexNotFound ||
                    runtimeStatus?.State != ThemeRuntimeState.NotInstalled)
                {
                    await RefreshRuntimeStatusAsync(
                        CodexStatusRefreshMode.PreferCache,
                        cancellationToken);
                }

                return PresenceCheckResult.Continue;
            }

            var processes = discovery.Value!.Processes;
            if (processes.Count == 0)
            {
                observedCodexProcessId = null;
                observedCodexProcessStartedAtUtc = null;
                if (runtimeStatus?.State != ThemeRuntimeState.NotRunning ||
                    hasLiveCodexIdentity)
                {
                    await RefreshRuntimeStatusAsync(
                        CodexStatusRefreshMode.PreferCache,
                        cancellationToken);
                }

                return PresenceCheckResult.Continue;
            }

            var processChanged =
                processes.Count != 1 ||
                !hasLiveCodexIdentity ||
                runtimeStatus?.CodexProcessId != processes[0].ProcessId ||
                observedCodexProcessId != processes[0].ProcessId ||
                observedCodexProcessStartedAtUtc != processes[0].StartedAtUtc;
            if (processChanged)
            {
                await RefreshRuntimeStatusAsync(
                    CodexStatusRefreshMode.PreferCache,
                    cancellationToken);
                await SynchronizeExternalThemeAsync(cancellationToken);
            }

            if (processes.Count == 1)
            {
                observedCodexProcessId = processes[0].ProcessId;
                observedCodexProcessStartedAtUtc = processes[0].StartedAtUtc;
            }
            else
            {
                observedCodexProcessId = null;
                observedCodexProcessStartedAtUtc = null;
            }

            return PresenceCheckResult.Complete;
        }
        finally
        {
            presenceCheckLock.Release();
        }
    }

    private void SetLibraryLayout(object? parameter)
    {
        if (parameter is string value &&
            Enum.TryParse<ThemeLibraryLayout>(value, ignoreCase: true, out var layout))
        {
            SelectedLibraryLayout = layout;
        }
    }

    private async Task RefreshAsync()
    {
        await RunOperationAsync(
            "正在刷新主题和 Codex 状态…",
            async cancellationToken =>
            {
                await LoadThemesAsync(cancellationToken);
                await RefreshStatusAsync(
                    CodexStatusRefreshMode.ForceProbe,
                    cancellationToken);
                if (!IsNotificationVisible ||
                    NotificationKind is not ("Warning" or "Error"))
                {
                    Notify("资料库和 Codex 状态已刷新。", "Success");
                }
            });
    }

    private async Task ApplyCachedCompatibilityAsync(
        CancellationToken cancellationToken)
    {
        var cached = await runtime.GetCachedCompatibilityAsync(cancellationToken);
        if (!cached.IsSuccess || cached.Value is null)
        {
            CodexStatusPhase = CodexDetectionPhase.Confirming;
            return;
        }

        var status = cached.Value;
        CodexStatusPhase = CodexDetectionPhase.Cached;
        IsCodexDetected = true;
        CodexStatusText = "上次检测到 ChatGPT (Codex)";
        CodexStatusDetail =
            $"上次于 {status.ProbedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} 完成兼容验证，正在后台确认当前安装与进程。";
        CodexCompatibilityText = status.CompatibilityLevel switch
        {
            CodexCompatibilityLevel.Verified => "上次结果：已验证版本",
            CodexCompatibilityLevel.CompatibleByProbe => "上次结果：能力探测兼容",
            _ => "上次结果：不兼容",
        };
        CodexTargetPath = status.ExecutablePath;
        codexExecutableSha256 = status.ExecutableSha256;
        CodexTargetFingerprint =
            status.ExecutableSha256[..Math.Min(16, status.ExecutableSha256.Length)] + "…";
        IsPersistenceEligible = status.IsPersistenceEligible;
        NotifyCommands();
    }

    private async Task CompleteBackgroundInitializationAsync(
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();
        await WriteDiagnosticAsync(
            DiagnosticLevel.Information,
            "desktop.codex_refresh.started",
            DiagnosticOutcome.Started,
            "desktop.codex_refresh",
            correlationId);
        CodexStatusPhase = CodexDetectionPhase.Confirming;
        CodexStatusText = "请稍作等待，程序加载中";

        try
        {
            var statusTask = RefreshStatusAsync(
                CodexStatusRefreshMode.PreferCache,
                cancellationToken);
            var externalTask = SynchronizeExternalThemeAsync(cancellationToken);
            await Task.WhenAll(statusTask, externalTask);
            await WriteDiagnosticAsync(
                DiagnosticLevel.Information,
                "desktop.codex_refresh.completed",
                DiagnosticOutcome.Succeeded,
                "desktop.codex_refresh",
                correlationId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            CodexStatusPhase = CodexDetectionPhase.Error;
            await WriteDiagnosticAsync(
                DiagnosticLevel.Error,
                "desktop.codex_refresh.failed",
                DiagnosticOutcome.Failed,
                "desktop.codex_refresh",
                correlationId,
                new OperationError(
                    OperationErrorCode.InternalError,
                    "后台 Codex 状态刷新失败。",
                    exception.GetType().Name));
        }
        finally
        {
            isBackgroundInitializationComplete = true;
            if (isWindowActive)
            {
                StartPresenceMonitor();
            }
        }
    }

    private async Task SynchronizeExternalThemeAsync(
        CancellationToken cancellationToken)
    {
        if (externalThemeCatalog is null)
        {
            return;
        }

        var correlationId = Guid.NewGuid();
        await WriteDiagnosticAsync(
            DiagnosticLevel.Information,
            "desktop.external_theme_sync.started",
            DiagnosticOutcome.Started,
            "desktop.external_theme_sync",
            correlationId);
        externalTheme = await externalThemeCatalog.SynchronizeAsync(cancellationToken);
        if (externalTheme is not null)
        {
            await LoadThemesAsync(cancellationToken);
        }

        await WriteDiagnosticAsync(
            DiagnosticLevel.Information,
            "desktop.external_theme_sync.completed",
            DiagnosticOutcome.Succeeded,
            "desktop.external_theme_sync",
            correlationId);
    }

    private Task CreateAsync()
    {
        if (Editor is null)
        {
            Notify("主题编辑服务当前不可用。", "Error");
            return Task.CompletedTask;
        }

        Editor.Begin(CreateStarterTheme(Guid.NewGuid(), "未命名主题"), newTheme: true);
        CurrentPage = LibraryPage.Editor;
        Notify("草稿已创建；保存前不会替换正式主题或运行主题。", "Info");
        NotifyCommands();
        return Task.CompletedTask;
    }

    private async Task EditAsync()
    {
        if (SelectedTheme is null || Editor is null)
        {
            return;
        }

        var selected = SelectedTheme;
        if (selected.Summary.IsSourceReadOnly)
        {
            var newId = Guid.NewGuid();
            var copy = await repository.CopyAsync(
                selected.ThemeId,
                newId,
                $"{selected.DisplayName}（副本）",
                lifetime.Token);
            if (!copy.IsSuccess)
            {
                NotifyError(copy.Error!);
                return;
            }

            await LoadThemesAsync(lifetime.Token, newId);
            selected = SelectedTheme!;
            Notify("已创建可编辑的本地副本；外部 Doro 数据不会被修改。", "Success");
        }

        var theme = await repository.GetAsync(selected.ThemeId, lifetime.Token);
        if (!theme.IsSuccess)
        {
            NotifyError(theme.Error!);
            return;
        }

        Editor.Begin(theme.Value!, newTheme: false, selected.ThumbnailPath);
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
            lifetime.Token);
        if (!confirmed)
        {
            return;
        }

        var cleanup = await Editor.CancelAsync(lifetime.Token);
        if (!cleanup.IsSuccess)
        {
            NotifyError(cleanup.Error!);
            return;
        }

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

    private void OpenGitHubRepository()
    {
        try
        {
            openExternalUrl(GitHubRepositoryUrl);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Notify("无法打开 GitHub 仓库页面。", "Error");
        }
    }

    private static void OpenExternalUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true,
        });
    }

    private async Task RefreshDiagnosticsAsync()
    {
        if (diagnosticQuery is null)
        {
            DiagnosticHealth = DiagnosticHealth.Unavailable;
            DiagnosticStatusText = "当前构建未提供诊断服务";
            DiagnosticLogStatusText = "日志状态：不可用";
            return;
        }

        var result = await diagnosticQuery.ReadAsync(20, lifetime.Token);
        if (!result.IsSuccess)
        {
            DiagnosticHealth = DiagnosticHealth.Unavailable;
            DiagnosticStatusText = "无法读取诊断记录";
            DiagnosticLogStatusText =
                $"日志状态：{result.Error!.DiagnosticCode ?? result.Error.Code.ToString()}";
            diagnosticEvents.Clear();
            NotifyCommands();
            return;
        }

        diagnosticSnapshot = result.Value!;
        DiagnosticHealth = diagnosticSnapshot.Health;
        DiagnosticStatusText = diagnosticSnapshot.StatusText;
        DiagnosticLastUpdatedText = diagnosticSnapshot.LastUpdatedAtUtc is { } updated
            ? $"最近更新：{updated.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
            : "最近更新：暂无";
        DiagnosticLatestErrorText = diagnosticSnapshot.LatestError is { } error
            ? $"最近错误：{error.DiagnosticCode ?? error.ErrorCode?.ToString() ?? error.EventName} · " +
              $"{error.TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
            : "最近错误：无";
        DiagnosticLatestRecoveryText = diagnosticSnapshot.LatestRecovery is { } recovery
            ? $"最近恢复：{recovery.TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
            : "最近恢复：无";
        DiagnosticLogStatusText = diagnosticSnapshot.WriteFailureCode is { } writeFailure
            ? $"日志状态：写入失败（{writeFailure}）"
            : diagnosticSnapshot.InvalidLineCount > 0
                ? $"日志状态：已忽略 {diagnosticSnapshot.InvalidLineCount} 条无效记录"
                : diagnosticSnapshot.LogDirectoryAvailable
                    ? "日志状态：可读写"
                    : "日志状态：尚未生成日志目录";

        diagnosticEvents.Clear();
        foreach (var item in diagnosticSnapshot.RecentEvents)
        {
            diagnosticEvents.Add(new DiagnosticEventViewModel(item));
        }

        NotifyCommands();
    }

    private void OpenDiagnosticLogDirectory()
    {
        var directory = diagnosticSnapshot?.LogDirectory;
        if (string.IsNullOrWhiteSpace(directory) ||
            !Directory.Exists(directory))
        {
            Notify("诊断日志目录尚不可用。", "Warning");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            ArgumentList = { directory },
            UseShellExecute = false,
        });
    }

    private async Task CopyDiagnosticSummaryAsync()
    {
        if (diagnosticBundle is null || copyText is null)
        {
            return;
        }

        var draft = await diagnosticBundle.CreateIssueDraftAsync(
            CreateDiagnosticIssueContext(),
            lifetime.Token);
        if (!draft.IsSuccess)
        {
            NotifyError(draft.Error!);
            return;
        }

        try
        {
            copyText(draft.Value!.Markdown);
            Notify(
                $"已复制脱敏 Issue 摘要。报告 ID：{draft.Value.ReportId:D}",
                "Success");
        }
        catch (Exception exception)
            when (exception is System.Runtime.InteropServices.ExternalException or
                InvalidOperationException)
        {
            Notify("剪贴板当前不可用，请稍后重试。", "Error");
        }
    }

    private async Task ExportDiagnosticBundleAsync()
    {
        if (diagnosticBundle is null)
        {
            return;
        }

        var destination = await dialogs.PickSaveFileAsync(
            "导出脱敏诊断包",
            "ZIP 诊断包|*.zip",
            ".zip",
            $"CodexThemeStudio-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            lifetime.Token);
        if (destination is null)
        {
            return;
        }

        var confirmed = await dialogs.ConfirmAsync(
            "导出诊断包",
            "诊断包只包含脱敏事件、运行环境、Issue 摘要和 SHA-256 清单；不包含主题、图片、数据库、配置、绝对路径或原始异常消息。",
            lifetime.Token);
        if (!confirmed)
        {
            return;
        }

        await RunOperationAsync(
            "正在生成并校验脱敏诊断包…",
            async cancellationToken =>
            {
                var result = await diagnosticBundle.ExportAsync(
                    destination,
                    CreateDiagnosticIssueContext(),
                    cancellationToken);
                if (!result.IsSuccess)
                {
                    NotifyError(result.Error!);
                    return;
                }

                Notify(
                    $"诊断包已导出。报告 ID：{result.Value!.ReportId:D}；" +
                    $"SHA-256 {result.Value.Sha256[..12]}…",
                    "Success");
            });
    }

    private DiagnosticIssueContext CreateDiagnosticIssueContext() =>
        new(
            appVersion,
            runtimeStatus?.CodexVersion,
            codexExecutableSha256,
            runtimeStatus?.State.ToString() ?? "Unknown");

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
                "当前正在使用的主题不能删除。请先切换主题或还原外观。",
                "Error");
            return;
        }

        var confirmed = await dialogs.ConfirmAsync(
            "移入应用回收站",
            $"“{selected.DisplayName}”将从资料库隐藏，但主题文件会保留以便恢复。",
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

    private async Task RestoreDeletedAsync()
    {
        var selected = SelectedTheme;
        if (selected is null || CurrentPage != LibraryPage.Trash)
        {
            return;
        }

        await RunOperationAsync(
            "正在还原主题…",
            async cancellationToken =>
            {
                var result = await repository.RestoreDeletedAsync(
                    selected.ThemeId,
                    cancellationToken);
                if (!result.IsSuccess)
                {
                    NotifyError(result.Error!);
                    return;
                }

                await LoadThemesAsync(cancellationToken);
                Notify($"已还原“{selected.DisplayName}”。", "Success");
            });
    }

    private async Task PermanentlyDeleteAsync()
    {
        var selected = SelectedTheme;
        if (selected is null || CurrentPage != LibraryPage.Trash)
        {
            return;
        }

        var confirmed = await dialogs.ConfirmAsync(
            "永久删除主题",
            $"“{selected.DisplayName}”将从 Theme Studio 永久移除，无法在应用内还原。主题目录会发送到 Windows 回收站，作为最后的系统级恢复措施。",
            lifetime.Token);
        if (!confirmed)
        {
            return;
        }

        await RunOperationAsync(
            "正在永久删除主题…",
            async cancellationToken =>
            {
                var result = await repository.PermanentlyDeleteAsync(
                    selected.ThemeId,
                    cancellationToken);
                if (!result.IsSuccess)
                {
                    NotifyError(result.Error!);
                    return;
                }

                await LoadThemesAsync(cancellationToken);
                Notify(
                    $"已从项目回收站永久删除“{selected.DisplayName}”；主题目录已发送到 Windows 回收站。",
                    "Success");
            });
    }

    private async Task EmptyTrashAsync()
    {
        if (CurrentPage != LibraryPage.Trash || deletedThemes.Count == 0)
        {
            return;
        }

        var count = deletedThemes.Count;
        var confirmed = await dialogs.ConfirmAsync(
            "清空回收站",
            $"将从 Theme Studio 永久移除回收站中的 {count} 个主题，且无法在应用内还原。各主题目录会发送到 Windows 回收站。",
            lifetime.Token);
        if (!confirmed)
        {
            return;
        }

        var targets = deletedThemes.Select(theme => theme.ThemeId).ToArray();
        await RunOperationAsync(
            "正在清空回收站…",
            async cancellationToken =>
            {
                foreach (var themeId in targets)
                {
                    var result = await repository.PermanentlyDeleteAsync(
                        themeId,
                        cancellationToken);
                    if (!result.IsSuccess)
                    {
                        NotifyError(result.Error!);
                        await LoadThemesAsync(cancellationToken);
                        return;
                    }
                }

                await LoadThemesAsync(cancellationToken);
                Notify(
                    $"已清空项目回收站；{count} 个主题目录已发送到 Windows 回收站。",
                    "Success");
            });
    }

    private async Task ToggleFavoriteAsync(ThemeCardViewModel theme)
    {
        var wasFavorite = theme.IsFavorite;

        await RunOperationAsync(
            wasFavorite ? "正在取消收藏…" : "正在添加收藏…",
            async cancellationToken =>
            {
                var result = await repository.SetFavoriteAsync(
                    theme.ThemeId,
                    !wasFavorite,
                    cancellationToken);
                if (!result.IsSuccess)
                {
                    NotifyError(result.Error!);
                    return;
                }

                await LoadThemesAsync(cancellationToken);
                Notify(wasFavorite ? "已取消收藏。" : "已加入收藏。", "Success");
            });
    }

    private async Task ApplyTemporaryAsync()
    {
        var selected = SelectedTheme;
        if (selected is null)
        {
            return;
        }

        if (!hasLiveCodexIdentity)
        {
            var message = runtimeStatus?.State switch
            {
                ThemeRuntimeState.NotRunning =>
                    "请先启动 Codex；检测到运行后即可临时应用。",
                ThemeRuntimeState.NotInstalled =>
                    "未检测到已安装的 Codex；请先安装或在设置中选择可信的 Codex 可执行文件。",
                _ => runtimeStatus?.UserMessage ??
                    "Codex 当前尚未完成实时身份确认，请启动 Codex 后重试。",
            };
            Notify(message, runtimeStatus?.State is
                ThemeRuntimeState.NotRunning or ThemeRuntimeState.NotInstalled
                    ? "Info"
                    : "Warning");
            StartPresenceMonitor();
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

                var result = await ApplyTemporaryWithRetryAsync(
                    theme.Value!,
                    runtimeStatus?.ThemeId is not null,
                    cancellationToken);
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
                NotifyRuntimeResult(result.Value!);
            });
    }

    private async Task<OperationResult<ThemeRuntimeStatus>>
        ApplyTemporaryWithRetryAsync(
            ThemePackage theme,
            bool switchExisting,
            CancellationToken cancellationToken)
    {
        const int maximumAttempts = 6;
        var delay = TimeSpan.FromMilliseconds(250);
        OperationResult<ThemeRuntimeStatus>? result = null;
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            result = switchExisting
                ? await runtime.SwitchTemporaryAsync(theme, cancellationToken)
                : await runtime.ApplyTemporaryAsync(theme, cancellationToken);
            if (result.IsSuccess ||
                result.Error!.DiagnosticCode != "injector.operation_busy" ||
                attempt == maximumAttempts)
            {
                return result;
            }

            await Task.Delay(delay, cancellationToken);
            delay = TimeSpan.FromMilliseconds(
                Math.Min(1000, delay.TotalMilliseconds * 2));
        }

        return result!;
    }

    private async Task SetPersistentAsync()
    {
        var selected = SelectedTheme;
        if (selected is null)
        {
            return;
        }

        if (externalTheme?.IsPersistenceConfigured == true)
        {
            var isSelectedExternalTheme = string.Equals(
                selected.Summary.SourceIdentifier,
                externalTheme.SourceIdentifier,
                StringComparison.Ordinal);
            if (isSelectedExternalTheme)
            {
                const string message =
                    "Doro 已由 OkkSkin 持久化；当前临时效果会在 Codex 重启后继续由 OkkSkin 应用。";
                CodexStatusDetail = message;
                Notify(message, "Success");
                return;
            }

            Notify(
                "当前由 OkkSkin 持久化。为避免两个 Agent 竞争，请先明确停用 OkkSkin 持久化，再为本地主题启用 Theme Studio 持久化。",
                "Warning");
            return;
        }

        if (!IsPersistenceEligible)
        {
            var message = runtimeStatus?.State == ThemeRuntimeState.NotInstalled
                ? "未检测到可用的 Codex，暂时不能设置持久主题。"
                : "当前 Codex 指纹尚未取得持久化资格。请先启动 Codex 并成功执行一次临时应用；程序会完成应用、清理和重新应用闭环。";
            Notify(message, "Warning");
            StartPresenceMonitor();
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
        ExternalPersistenceStatus? externalStatus = null;
        OperationError? externalStatusError = null;
        if (externalPersistence is not null)
        {
            var status = await externalPersistence.GetStatusAsync(lifetime.Token);
            if (status.IsSuccess)
            {
                externalStatus = status.Value;
            }
            else
            {
                externalStatusError = status.Error;
            }
        }

        var managedPersistenceEnabled = IsPersistenceEnabled;
        var externalPersistenceEnabled =
            externalStatus?.RequiresAction == true ||
            externalTheme?.IsPersistenceConfigured == true;
        var hasRestorableRuntimeState =
            runtimeStatus?.ThemeId is not null ||
            runtimeStatus?.SelectedThemeId is not null ||
            externalTheme?.IsAppliedToCurrentProcess == true ||
            (hasLiveCodexIdentity &&
             runtimeStatus?.State is
                 ThemeRuntimeState.Partial or
                 ThemeRuntimeState.Mismatch or
                 ThemeRuntimeState.InspectorResidual);
        if (!managedPersistenceEnabled &&
            !externalPersistenceEnabled &&
            !hasRestorableRuntimeState)
        {
            Notify("当前已是官方外观，无需还原。", "Info");
            return;
        }

        if (managedPersistenceEnabled || externalPersistenceEnabled)
        {
            var providers = new List<string>();
            if (managedPersistenceEnabled)
            {
                providers.Add("Theme Studio");
            }

            if (externalPersistenceEnabled)
            {
                providers.Add(externalTheme?.Provider ?? externalStatus?.Provider ?? "外部主题");
            }

            var confirmed = await dialogs.ConfirmAsync(
                "还原外观并停用持久化",
                $"将停用 {string.Join("、", providers.Distinct(StringComparer.Ordinal))} 持久化，" +
                "移除对应的当前用户启动项、停止 Agent，并还原当前 Codex。" +
                "\n\n主题、图片和缓存不会删除。",
                lifetime.Token);
            if (!confirmed)
            {
                return;
            }
        }

        await RunOperationAsync(
            "正在停用持久化并还原 Codex 外观…",
            async cancellationToken =>
            {
                var residuals = new List<string>();
                var managedDisabled = !managedPersistenceEnabled;
                var externalDisabled = !externalPersistenceEnabled;

                if (externalPersistenceEnabled)
                {
                    if (externalPersistence is null)
                    {
                        residuals.Add("OkkSkin 停用服务当前不可用。");
                    }
                    else
                    {
                        var disabled = await externalPersistence.DisableAsync(
                            cancellationToken);
                        if (!disabled.IsSuccess)
                        {
                            residuals.Add(disabled.Error!.UserMessage);
                        }
                        else
                        {
                            var disabledStatus = disabled.Value!.Status;
                            externalDisabled = !disabledStatus.RequiresAction;
                            residuals.AddRange(disabledStatus.Residuals);
                            if (disabled.Value.Outcome ==
                                ExternalPersistenceDisableOutcome.Partial &&
                                disabledStatus.Residuals.Count == 0)
                            {
                                residuals.Add(disabled.Value.UserMessage);
                            }

                            if (externalTheme is not null)
                            {
                                externalTheme = externalTheme with
                                {
                                    IsPersistenceConfigured =
                                        disabledStatus.IsConfigured,
                                    IsAppliedToCurrentProcess = false,
                                    AppliedProcessId = null,
                                    UserMessage = disabledStatus.UserMessage,
                                };
                            }
                        }
                    }
                }

                if (managedPersistenceEnabled)
                {
                    var disabled = await persistence.DisableAsync(
                        cancellationToken);
                    if (disabled.IsSuccess)
                    {
                        managedDisabled = true;
                    }
                    else
                    {
                        residuals.Add(disabled.Error!.UserMessage);
                    }
                }

                var restored = await runtime.RestoreAsync(cancellationToken);
                if (!restored.IsSuccess)
                {
                    residuals.Add(restored.Error!.UserMessage);
                    Notify(
                        $"无法完整还原外观：{string.Join("；", residuals.Distinct(StringComparer.Ordinal))}",
                        "Error");
                    return;
                }

                externalThemeSuppressedForCurrentProcess = true;
                if (externalPersistenceEnabled &&
                    externalStatusError is not null)
                {
                    residuals.Add(
                        $"无法完整确认 OkkSkin 初始状态：{externalStatusError.UserMessage}");
                }

                residuals = residuals
                    .Where(message => !string.IsNullOrWhiteSpace(message))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                var isComplete =
                    managedDisabled &&
                    externalDisabled &&
                    residuals.Count == 0;
                var message = isComplete
                    ? "持久化已停用，当前 Codex 已还原官方外观；下次启动仍保持官方外观。"
                    : $"当前 Codex 已还原，但仍存在持久化残留：{string.Join("；", residuals)}";
                ApplyRuntimeStatus(restored.Value! with
                {
                    State = isComplete
                        ? ThemeRuntimeState.Default
                        : ThemeRuntimeState.Partial,
                    ThemeId = null,
                    IsPersistenceEnabled = !managedDisabled,
                    UserMessage = message,
                });
                await LoadThemesAsync(cancellationToken);
                MarkThemeStates();
                Notify(message, isComplete ? "Success" : "Warning");
            });
    }

    private async Task LoadThemesAsync(
        CancellationToken cancellationToken = default,
        Guid? selectThemeId = null)
    {
        var activeResult = await repository.ListAsync(cancellationToken);
        if (!activeResult.IsSuccess)
        {
            NotifyError(activeResult.Error!);
            return;
        }

        var deletedResult = await repository.ListDeletedAsync(cancellationToken);
        if (!deletedResult.IsSuccess)
        {
            NotifyError(deletedResult.Error!);
            return;
        }

        var previousSelection = selectThemeId ?? SelectedTheme?.ThemeId;
        var activeCards = await Task.WhenAll(
            activeResult.Value!.Select(summary =>
                CreateThemeCardAsync(summary, cancellationToken)));
        var deletedCards = await Task.WhenAll(
            deletedResult.Value!.Select(summary =>
                CreateThemeCardAsync(summary, cancellationToken)));
        allThemes.Clear();
        allThemes.AddRange(activeCards);
        deletedThemes.Clear();
        deletedThemes.AddRange(deletedCards);
        RebuildTags();
        ApplyFilter();
        var selectionSource = CurrentPage == LibraryPage.Trash
            ? deletedThemes
            : allThemes;
        SelectedTheme = selectionSource.FirstOrDefault(item => item.ThemeId == previousSelection);
        MarkThemeStates();
    }

    private async Task<ThemeCardViewModel> CreateThemeCardAsync(
        ThemeSummary summary,
        CancellationToken cancellationToken)
    {
        ThemePalette? palette;
        if (externalTheme is not null &&
            string.Equals(
                summary.SourceIdentifier,
                externalTheme.SourceIdentifier,
                StringComparison.Ordinal))
        {
            palette = externalTheme?.Theme.Palette;
        }
        else
        {
            var theme = await repository.GetAsync(summary.ThemeId, cancellationToken);
            palette = theme.IsSuccess ? theme.Value!.Palette : null;
        }

        return new ThemeCardViewModel(
            summary,
            resolveThumbnail(summary.ThumbnailRelativePath),
            palette);
    }

    private async Task RefreshStatusAsync(
        CodexStatusRefreshMode refreshMode,
        CancellationToken cancellationToken = default)
    {
        var refreshGeneration = Interlocked.Increment(ref statusRefreshGeneration);
        if (codexTargetSelection is not null)
        {
            var target = await codexTargetSelection.GetStatusAsync(cancellationToken);
            if (target.IsSuccess)
            {
                codexExecutableSha256 = target.Value!.ExecutableSha256;
                CodexTargetPath = target.Value!.ExecutablePath ??
                    "自动检测 Microsoft Store Codex";
                CodexTargetFingerprint = string.IsNullOrWhiteSpace(
                    target.Value.ExecutableSha256)
                    ? "自动目标；将在发现后计算指纹"
                    : target.Value.ExecutableSha256[..Math.Min(16, target.Value.ExecutableSha256.Length)] + "…";
            }
            else
            {
                codexExecutableSha256 = null;
                CodexTargetPath = "已保存目标不可用；运行时将尝试自动检测";
                CodexTargetFingerprint = target.Error!.DiagnosticCode ?? "读取失败";
            }
        }

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

        await RefreshRuntimeStatusCoreAsync(
            refreshMode,
            refreshGeneration,
            cancellationToken);
    }

    private async Task RefreshRuntimeStatusAsync(
        CodexStatusRefreshMode refreshMode,
        CancellationToken cancellationToken)
    {
        var refreshGeneration = Interlocked.Increment(ref statusRefreshGeneration);
        await RefreshRuntimeStatusCoreAsync(
            refreshMode,
            refreshGeneration,
            cancellationToken);
    }

    private async Task RefreshRuntimeStatusCoreAsync(
        CodexStatusRefreshMode refreshMode,
        int refreshGeneration,
        CancellationToken cancellationToken)
    {
        var persistenceResult = await persistence.GetStatusAsync(cancellationToken);
        IsPersistenceEnabled =
            persistenceResult.IsSuccess && persistenceResult.Value!.IsPersistenceEnabled;

        var runtimeResult =
            persistenceResult.IsSuccess &&
            persistenceResult.Value!.IsPersistenceEnabled &&
            refreshMode == CodexStatusRefreshMode.PreferCache
                ? persistenceResult
                : await runtime.GetStatusAsync(refreshMode, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (refreshGeneration != Volatile.Read(ref statusRefreshGeneration))
        {
            return;
        }

        if (!runtimeResult.IsSuccess)
        {
            CodexStatusPhase = CodexDetectionPhase.Error;
            hasLiveCodexIdentity = false;
            if (runtimeStatus is null)
            {
                IsCodexDetected = false;
                CodexStatusText = "未检测到 ChatGPT (Codex)";
            }

            CodexStatusDetail = runtimeResult.Error!.UserMessage;
            NotifyError(runtimeResult.Error);
            return;
        }

        var runtimeValue = runtimeResult.Value!;
        var hasTemporaryRuntimeTheme =
            runtimeValue.State is ThemeRuntimeState.Temporary or ThemeRuntimeState.Partial &&
            runtimeValue.ThemeId is not null;
        var status = runtimeValue with
        {
            IsPersistenceEnabled = IsPersistenceEnabled,
            ThemeId = !hasTemporaryRuntimeTheme &&
                persistenceResult.IsSuccess &&
                persistenceResult.Value!.IsPersistenceEnabled
                    ? persistenceResult.Value.ThemeId
                    : runtimeValue.ThemeId,
        };
        ApplyRuntimeStatus(status);
        MarkThemeStates();
        NotifyRuntimeConcern(status);
    }

    private void ApplyRuntimeStatus(ThemeRuntimeStatus status)
    {
        runtimeStatus = status;
        CodexStatusPhase = CodexDetectionPhase.Live;
        hasLiveCodexIdentity =
            status.CodexProcessId is not null &&
            status.State is not (
                ThemeRuntimeState.Unavailable or
                ThemeRuntimeState.Unsupported or
                ThemeRuntimeState.InspectorResidual);
        IsPersistenceEnabled = status.IsPersistenceEnabled;
        IsCodexDetected = status.State != ThemeRuntimeState.NotInstalled;
        CodexStatusText = IsCodexDetected
            ? "已检测到 ChatGPT (Codex)"
            : "未检测到 ChatGPT (Codex)";
        CodexStatusDetail = status.UserMessage;
        IsPersistenceEligible = status.IsPersistenceEligible;
        CodexCompatibilityText = status.CompatibilityLevel switch
        {
            CodexCompatibilityLevel.Verified => "已验证版本 · 能力探测通过",
            CodexCompatibilityLevel.CompatibleByProbe =>
                status.IdentityAssessment == CodexIdentityAssessment.UnverifiedSource
                    ? "来源未验证 · 能力探测兼容"
                    : "未知版本 · 能力探测兼容",
            _ => "能力不兼容",
        };
        if (!string.IsNullOrWhiteSpace(status.ExecutableSha256))
        {
            codexExecutableSha256 = status.ExecutableSha256;
            CodexTargetFingerprint = status.ExecutableSha256[..Math.Min(16, status.ExecutableSha256.Length)] + "…";
        }

        PersistenceEligibilityMessage = status.IsPersistenceEligible
            ? "Codex 应用可永远保持主题持久化，直到还原外观"
            : "首次使用此 Codex 构建，请先临时应用；程序将自动验证应用、清理和重新应用，成功后即可持久化。";
        NotifyCommands();
    }

    private async Task SelectCodexExecutableAsync()
    {
        if (codexTargetSelection is null)
        {
            return;
        }

        var path = await dialogs.PickOpenFileAsync(
            "选择 Codex 可执行文件",
            "Windows 可执行文件 (*.exe)|*.exe",
            lifetime.Token);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var confirmed = await dialogs.ConfirmAsync(
            "确认 Codex 来源",
            "手动选择的可执行文件来源不会作为官方身份背书。程序只会在能力探测、注入和清理验证全部通过后使用它。是否继续？",
            lifetime.Token);
        if (!confirmed)
        {
            return;
        }

        var selected = await codexTargetSelection.SelectAsync(
            path,
            acknowledgeUnverifiedSource: true,
            lifetime.Token);
        if (!selected.IsSuccess)
        {
            NotifyError(selected.Error!);
            return;
        }

        await RefreshStatusAsync(
            CodexStatusRefreshMode.ForceProbe,
            lifetime.Token);
        Notify("已保存 Codex 目标；文件发生变化时会重新要求确认和兼容验证。", "Success");
    }

    private async Task RedetectCodexAsync()
    {
        await RefreshStatusAsync(
            CodexStatusRefreshMode.ForceProbe,
            lifetime.Token);
        Notify("已重新检测 Codex 目标和运行能力。", "Info");
    }

    private async Task ResetCodexTargetAsync()
    {
        if (codexTargetSelection is null)
        {
            return;
        }

        var reset = await codexTargetSelection.ResetAsync(lifetime.Token);
        if (!reset.IsSuccess)
        {
            NotifyError(reset.Error!);
            return;
        }

        await RefreshStatusAsync(
            CodexStatusRefreshMode.ForceProbe,
            lifetime.Token);
        Notify("已恢复 Microsoft Store Codex 自动检测。", "Success");
    }

    private void NotifyRuntimeConcern(ThemeRuntimeStatus status)
    {
        var kind = GetRuntimeConcernKind(status.State);
        if (kind is not null)
        {
            Notify(status.UserMessage, kind);
        }
    }

    private void NotifyRuntimeResult(ThemeRuntimeStatus status) =>
        Notify(status.UserMessage, GetRuntimeConcernKind(status.State) ?? "Success");

    private static string? GetRuntimeConcernKind(ThemeRuntimeState state) =>
        state switch
        {
            ThemeRuntimeState.Unsupported or
            ThemeRuntimeState.Partial or
            ThemeRuntimeState.Mismatch => "Warning",
            ThemeRuntimeState.Unavailable or
            ThemeRuntimeState.Failed or
            ThemeRuntimeState.InspectorResidual => "Error",
            _ => null,
        };

    private void MarkThemeStates()
    {
        foreach (var theme in allThemes)
        {
            var isTemporary =
                runtimeStatus?.State is ThemeRuntimeState.Temporary or ThemeRuntimeState.Partial &&
                runtimeStatus.ThemeId == theme.ThemeId;
            theme.IsPersistent =
                !isTemporary &&
                (theme.Summary.IsCurrentPersistent ||
                 (IsPersistenceEnabled &&
                  runtimeStatus?.State == ThemeRuntimeState.Persistent &&
                  runtimeStatus.ThemeId == theme.ThemeId));
            theme.IsTemporary = isTemporary;
            theme.IsExternalPersistent =
                externalTheme?.IsPersistenceConfigured == true &&
                string.Equals(
                    theme.Summary.SourceIdentifier,
                    externalTheme.SourceIdentifier,
                    StringComparison.Ordinal);
            theme.IsExternalActive =
                theme.IsExternalPersistent &&
                !externalThemeSuppressedForCurrentProcess &&
                externalTheme?.IsAppliedToCurrentProcess == true &&
                runtimeStatus?.State is not ThemeRuntimeState.Temporary and
                    not ThemeRuntimeState.Partial;
        }

        if (SelectedScope == ThemeScope.Current)
        {
            ApplyFilter();
        }

        NotifyCommands();
    }

    private void ApplyFilter()
    {
        var isTrash = CurrentPage == LibraryPage.Trash;
        IEnumerable<ThemeCardViewModel> query = isTrash ? deletedThemes : allThemes;
        if (!isTrash && !string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(item =>
                item.DisplayName.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
                item.Tags.Any(tag =>
                    tag.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)));
        }

        if (!isTrash && SelectedTag != "全部标签")
        {
            query = query.Where(item =>
                item.Tags.Contains(SelectedTag, StringComparer.CurrentCultureIgnoreCase));
        }

        if (!isTrash && SelectedScope == ThemeScope.Current)
        {
            query = query.Where(item =>
                item.IsTemporary || item.IsPersistent || item.IsExternalActive);
        }

        if (!isTrash && CurrentPage == LibraryPage.Favorites)
        {
            query = query.Where(item => item.IsFavorite);
        }

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

        ThemeCountText = isTrash
            ? $"{visibleThemes.Count} 个已删除主题"
            : $"{visibleThemes.Count} 个主题";
        EmptyTrashCommand.NotifyCanExecuteChanged();
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

        if (destination is LibraryPage.Current or LibraryPage.All)
        {
            SelectedScope = destination == LibraryPage.Current
                ? ThemeScope.Current
                : ThemeScope.All;
            CurrentPage = LibraryPage.All;
        }
        else
        {
            CurrentPage = destination;
        }

        ApplyFilter();
        SelectedTheme = null;
    }

    private void NavigateToUpdate()
    {
        CurrentPage = LibraryPage.Settings;
        SelectedSettingsSection = SettingsSection.About;
        SelectedTheme = null;
    }

    private async Task CheckForUpdatesAfterStartupAsync(
        CancellationToken cancellationToken)
    {
        if (updateService is null) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            await CheckForUpdatesAsync(userInitiated: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task CheckForUpdatesAsync(bool userInitiated)
    {
        if (updateService is null || IsCheckingForUpdates) return;
        IsCheckingForUpdates = true;
        try
        {
            var result = await updateService.CheckAsync(
                forceRefresh: userInitiated,
                lifetime.Token);
            if (!result.IsSuccess)
            {
                if (userInitiated) Notify("检查失败，请重试", "Error");
                return;
            }

            availableUpdate = result.Value!.IsUpdateAvailable
                ? result.Value.Release
                : null;
            OnPropertyChanged(nameof(IsUpdateAvailable));
            if (availableUpdate is null)
            {
                if (userInitiated) Notify("已是最新版本", "Success");
                return;
            }

            if (userInitiated && updateDialogs is not null)
            {
                await updateDialogs.ShowReleaseAsync(
                    appVersion,
                    availableUpdate,
                    () => openExternalUrl(availableUpdate.ReleaseUri.AbsoluteUri),
                    DownloadVerifiedUpdateAsync,
                    lifetime.Token);
            }
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    private async Task<OperationResult<UpdateInstallResult>>
        DownloadVerifiedUpdateAsync(
            IProgress<UpdateDownloadProgress> progress,
            CancellationToken cancellationToken)
    {
        if (updateService is null || availableUpdate is null)
        {
            return OperationResult<UpdateInstallResult>.Failure(
                OperationErrorCode.Conflict,
                "更新信息已失效，请重新检查。",
                "update.release.missing");
        }

        if (IsBusy)
        {
            return OperationResult<UpdateInstallResult>.Failure(
                OperationErrorCode.Conflict,
                "当前有操作正在进行，请完成后重试。",
                "update.preflight.busy");
        }

        if (Editor?.HasDraft == true)
        {
            return OperationResult<UpdateInstallResult>.Failure(
                OperationErrorCode.Conflict,
                "存在未保存的编辑草稿，请先保存或取消。",
                "update.preflight.unsaved_draft");
        }

        var zipBytes = availableUpdate.Assets
            .FirstOrDefault(static asset => asset.Name.EndsWith(".zip", StringComparison.Ordinal))
            ?.Size ?? 0;
        var preflight = updatePreflight?.Invoke(zipBytes) ?? OperationResult.Success();
        if (!preflight.IsSuccess)
        {
            return OperationResult<UpdateInstallResult>.Failure(preflight.Error!);
        }

        var staged = await updateService.DownloadAndStageAsync(
            availableUpdate,
            progress,
            cancellationToken);
        if (!staged.IsSuccess)
        {
            return OperationResult<UpdateInstallResult>.Failure(staged.Error!);
        }

        if (updateInstaller is null)
        {
            return OperationResult<UpdateInstallResult>.Failure(
                OperationErrorCode.NotImplemented,
                "自动安装组件当前不可用；未修改任何程序文件。",
                "update.installer.unavailable");
        }

        var install = await updateInstaller.StartAsync(staged.Value!, cancellationToken);
        if (install.IsSuccess && install.Value!.Outcome == UpdateInstallOutcome.Started)
        {
            requestApplicationShutdown?.Invoke();
        }

        return install;
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
        activeDiagnosticOperation = GetDiagnosticOperation(operation);
        activeDiagnosticCorrelationId = Guid.NewGuid();
        activeDiagnosticOperationFailed = false;
        await WriteDiagnosticAsync(
            DiagnosticLevel.Information,
            "desktop.operation.started",
            DiagnosticOutcome.Started,
            activeDiagnosticOperation,
            activeDiagnosticCorrelationId);
        try
        {
            await action(lifetime.Token);
            if (!activeDiagnosticOperationFailed)
            {
                await WriteDiagnosticAsync(
                    DiagnosticLevel.Information,
                    "desktop.operation.succeeded",
                    DiagnosticOutcome.Succeeded,
                    activeDiagnosticOperation,
                    activeDiagnosticCorrelationId);
            }
        }
        catch (OperationCanceledException)
        {
            Notify("操作已取消。", "Info");
        }
        finally
        {
            activeDiagnosticOperation = "desktop.background";
            activeDiagnosticCorrelationId = null;
            activeDiagnosticOperationFailed = false;
            OperationText = string.Empty;
            IsBusy = false;
            if (isWindowActive)
            {
                StartPresenceMonitor();
            }
        }
    }

    private void NotifyError(OperationError error)
    {
        activeDiagnosticOperationFailed = true;
        _ = WriteDiagnosticAsync(
            DiagnosticLevel.Error,
            "desktop.operation.failed",
            DiagnosticOutcome.Failed,
            activeDiagnosticOperation,
            activeDiagnosticCorrelationId,
            error);
        Notify($"{error.UserMessage} 请检查状态后重试。", "Error");
    }

    private async Task WriteDiagnosticAsync(
        DiagnosticLevel level,
        string eventName,
        DiagnosticOutcome outcome,
        string operation,
        Guid? correlationId,
        OperationError? error = null)
    {
        if (diagnosticSink is null)
        {
            return;
        }

        var write = await diagnosticSink.WriteAsync(
            DiagnosticEventFactory.Create(
                DiagnosticSource.Desktop,
                level,
                eventName,
                outcome,
                diagnosticSessionId,
                appVersion,
                operation,
                correlationId,
                error,
                runtimeStatus?.CodexVersion,
                codexExecutableSha256),
            lifetime.Token);
        if (!write.IsSuccess)
        {
            DiagnosticHealth = DiagnosticHealth.Unavailable;
            DiagnosticStatusText = "诊断日志写入失败";
            DiagnosticLogStatusText =
                $"日志状态：{write.Error!.DiagnosticCode ?? write.Error.Code.ToString()}";
        }
    }

    private static string GetDiagnosticOperation(string displayText) =>
        displayText switch
        {
            var text when text.Contains("迁移", StringComparison.Ordinal) =>
                "storage.migrate",
            var text when text.Contains("导入", StringComparison.Ordinal) =>
                "theme.import",
            var text when text.Contains("导出", StringComparison.Ordinal) &&
                text.Contains("诊断", StringComparison.Ordinal) =>
                "diagnostics.export",
            var text when text.Contains("导出", StringComparison.Ordinal) =>
                "theme.export",
            var text when text.Contains("持久", StringComparison.Ordinal) =>
                "theme.persist",
            var text when text.Contains("临时", StringComparison.Ordinal) =>
                "theme.apply",
            var text when text.Contains("还原", StringComparison.Ordinal) =>
                "theme.restore",
            var text when text.Contains("删除", StringComparison.Ordinal) =>
                "theme.delete",
            var text when text.Contains("保存", StringComparison.Ordinal) =>
                "theme.save",
            _ => "desktop.operation",
        };

    private void Notify(string message, string kind)
    {
        if (string.Equals(kind, "Error", StringComparison.Ordinal) &&
            activeDiagnosticCorrelationId is not null &&
            !activeDiagnosticOperationFailed)
        {
            activeDiagnosticOperationFailed = true;
            _ = WriteDiagnosticAsync(
                DiagnosticLevel.Error,
                "desktop.operation.failed",
                DiagnosticOutcome.Failed,
                activeDiagnosticOperation,
                activeDiagnosticCorrelationId,
                new OperationError(
                    OperationErrorCode.InternalError,
                    "操作报告了错误状态。",
                    "desktop.operation.reported_error"));
        }

        NotificationMessage = message;
        NotificationKind = kind;
        IsNotificationVisible = true;
        var version = ++notificationVersion;
        _ = DismissNotificationAsync(version);
    }

    private async Task DismissNotificationAsync(int version)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), lifetime.Token);
            if (version == notificationVersion)
            {
                IsNotificationVisible = false;
            }
        }
        catch (OperationCanceledException)
        {
            // The window is closing; there is no notification left to dismiss.
        }
    }

    private bool CanMutateSelection() =>
        CurrentPage != LibraryPage.Trash &&
        SelectedTheme is not null &&
        !SelectedTheme.Summary.IsSourceReadOnly &&
        !IsBusy;

    private bool CanDeleteSelection() =>
        CanMutateSelection() &&
        SelectedTheme is { IsPersistent: false, IsTemporary: false };

    private bool CanUseActiveSelection() =>
        CurrentPage != LibraryPage.Trash &&
        SelectedTheme is not null &&
        !IsBusy;

    private bool CanToggleFavorite(ThemeCardViewModel theme) =>
        CurrentPage != LibraryPage.Trash &&
        Themes.Contains(theme) &&
        !IsBusy;

    private bool CanUseTrashSelection() =>
        CurrentPage == LibraryPage.Trash &&
        SelectedTheme is not null &&
        !IsBusy;

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
        RestoreDeletedCommand.NotifyCanExecuteChanged();
        PermanentlyDeleteCommand.NotifyCanExecuteChanged();
        EmptyTrashCommand.NotifyCanExecuteChanged();
        ImportCommand.NotifyCanExecuteChanged();
        MigrateStorageCommand.NotifyCanExecuteChanged();
        ApplyTemporaryCommand.NotifyCanExecuteChanged();
        SetPersistentCommand.NotifyCanExecuteChanged();
        RestoreCommand.NotifyCanExecuteChanged();
        SelectCodexExecutableCommand.NotifyCanExecuteChanged();
        RedetectCodexCommand.NotifyCanExecuteChanged();
        ResetCodexTargetCommand.NotifyCanExecuteChanged();
        RefreshDiagnosticsCommand.NotifyCanExecuteChanged();
        OpenDiagnosticLogCommand.NotifyCanExecuteChanged();
        CopyDiagnosticSummaryCommand.NotifyCanExecuteChanged();
        ExportDiagnosticBundleCommand.NotifyCanExecuteChanged();
    }

    private static ThemePackage CreateStarterTheme(Guid id, string name) =>
        new(
            1,
            id,
            name,
            ThemeVariant.Auto,
            new ThemePalette(
                "#111111",
                "#1C1C1CE6",
                "#3B82F6",
                "#F5F5F5",
                "#A3A3A3",
                "#30303080"),
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
                0,
                10));

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

    private enum PresenceCheckResult
    {
        Continue,
        Complete,
    }
}
