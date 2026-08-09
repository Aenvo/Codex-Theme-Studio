using System.IO;
using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;
using CodexThemeStudio.Desktop.Services;
using CodexThemeStudio.Desktop.ViewModels;
using Xunit.Abstractions;

namespace CodexThemeStudio.Desktop.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task UpdateIndicator_ChecksWithoutLeavingCurrentPage()
    {
        var release = FakeUpdateService.CreateRelease("1.3.0");
        var updates = new FakeUpdateService(
            OperationResult<UpdateCheckResult>.Success(
                new UpdateCheckResult("1.2.2", true, release, false)));
        var updateDialogs = new FakeUpdateDialogs();
        using var fixture = new ViewModelFixture(
            0,
            updateService: updates,
            updateDialogs: updateDialogs);
        fixture.ViewModel.NavigateCommand.Execute("Favorites");

        fixture.ViewModel.CheckUpdatesCommand.Execute(null);
        await WaitUntilAsync(() => updateDialogs.ShowCalls == 1);

        Assert.True(fixture.ViewModel.IsUpdateAvailable);
        Assert.Equal(1, updates.CheckCalls);
        Assert.True(updates.LastForceRefresh);
        Assert.Equal(LibraryPage.Favorites, fixture.ViewModel.CurrentPage);
        Assert.NotEqual(SettingsSection.About, fixture.ViewModel.SelectedSettingsSection);
    }

    [Fact]
    public async Task UpdateIndicator_FailureKeepsIndicatorAndCanRetry()
    {
        var release = FakeUpdateService.CreateRelease("1.3.0");
        var updates = new FakeUpdateService(
            OperationResult<UpdateCheckResult>.Success(
                new UpdateCheckResult("1.2.2", true, release, false)));
        var updateDialogs = new FakeUpdateDialogs();
        using var fixture = new ViewModelFixture(
            0,
            updateService: updates,
            updateDialogs: updateDialogs);

        fixture.ViewModel.CheckUpdatesCommand.Execute(null);
        await WaitUntilAsync(() => updateDialogs.ShowCalls == 1);
        updates.SetCheckResult(OperationResult<UpdateCheckResult>.Failure(
            OperationErrorCode.InvalidResponse,
            "check failed"));

        fixture.ViewModel.CheckUpdatesCommand.Execute(null);
        await WaitUntilAsync(() =>
            fixture.ViewModel.NotificationMessage == "检查失败，请重试");

        Assert.True(fixture.ViewModel.IsUpdateAvailable);
        Assert.True(fixture.ViewModel.CheckUpdatesCommand.CanExecute(null));
        updates.SetCheckResult(OperationResult<UpdateCheckResult>.Success(
            new UpdateCheckResult("1.2.2", true, release, false)));
        fixture.ViewModel.CheckUpdatesCommand.Execute(null);
        await WaitUntilAsync(() => updateDialogs.ShowCalls == 2);

        Assert.Equal(3, updates.CheckCalls);
        Assert.All(updates.ForceRefreshArguments, Assert.True);
    }

    [Fact]
    public async Task CheckUpdates_NoNewReleaseUsesTemporaryNotification()
    {
        var updates = new FakeUpdateService(
            OperationResult<UpdateCheckResult>.Success(
                new UpdateCheckResult("1.2.2", false, null, false)));
        using var fixture = new ViewModelFixture(0, updateService: updates);

        fixture.ViewModel.CheckUpdatesCommand.Execute(null);
        await WaitUntilAsync(() => fixture.ViewModel.IsNotificationVisible);

        Assert.Equal("已是最新版本", fixture.ViewModel.NotificationMessage);
        Assert.False(fixture.ViewModel.IsUpdateAvailable);
    }

    private readonly ITestOutputHelper output;

    public MainWindowViewModelTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void LibraryLayout_DefaultsToCardsAndSwitchesBothWays()
    {
        using var fixture = new ViewModelFixture(themeCount: 2);

        Assert.Equal(ThemeLibraryLayout.Cards, fixture.ViewModel.SelectedLibraryLayout);
        Assert.True(fixture.ViewModel.IsCardView);
        Assert.False(fixture.ViewModel.IsListView);

        fixture.ViewModel.SetLibraryLayoutCommand.Execute("List");

        Assert.Equal(ThemeLibraryLayout.List, fixture.ViewModel.SelectedLibraryLayout);
        Assert.False(fixture.ViewModel.IsCardView);
        Assert.True(fixture.ViewModel.IsListView);

        fixture.ViewModel.SetLibraryLayoutCommand.Execute("Cards");

        Assert.True(fixture.ViewModel.IsCardView);
        Assert.False(fixture.ViewModel.IsListView);
    }

    [Fact]
    public async Task Workspace_DefaultsToAllAndScopeFiltersCurrentTheme()
    {
        using var fixture = new ViewModelFixture(themeCount: 5);
        var temporaryId = fixture.Repository.Summaries[1].ThemeId;
        fixture.Runtime.Status = Status(
            ThemeRuntimeState.Temporary,
            temporaryId,
            persistenceEnabled: false);

        await fixture.ViewModel.InitializeAsync();

        Assert.Equal("主题资料库", fixture.ViewModel.PageTitle);
        Assert.Equal(LibraryPage.All, fixture.ViewModel.CurrentPage);
        Assert.Equal(ThemeScope.All, fixture.ViewModel.SelectedScope);
        Assert.Equal(5, fixture.ViewModel.Themes.Count);

        fixture.ViewModel.SelectedScope = ThemeScope.Current;

        var current = Assert.Single(fixture.ViewModel.Themes);
        Assert.Equal(temporaryId, current.ThemeId);

        fixture.ViewModel.NavigateCommand.Execute("All");

        Assert.Equal(LibraryPage.All, fixture.ViewModel.CurrentPage);
        Assert.Equal(ThemeScope.All, fixture.ViewModel.SelectedScope);
        Assert.Equal(5, fixture.ViewModel.Themes.Count);

        fixture.ViewModel.NavigateCommand.Execute("Current");

        Assert.Equal(LibraryPage.All, fixture.ViewModel.CurrentPage);
        Assert.Equal(ThemeScope.Current, fixture.ViewModel.SelectedScope);
        Assert.Single(fixture.ViewModel.Themes);
    }

    [Fact]
    public async Task InitializeAndNavigate_DoNotSelectAThemeUntilUserChoosesOne()
    {
        using var fixture = new ViewModelFixture(themeCount: 3);

        await fixture.ViewModel.InitializeAsync();

        Assert.Null(fixture.ViewModel.SelectedTheme);
        Assert.False(fixture.ViewModel.ApplyTemporaryCommand.CanExecute(null));

        fixture.ViewModel.SelectedTheme = fixture.ViewModel.Themes[1];
        Assert.True(fixture.ViewModel.ApplyTemporaryCommand.CanExecute(null));

        fixture.ViewModel.NavigateCommand.Execute("Favorites");

        Assert.Equal("我的收藏", fixture.ViewModel.PageTitle);
        Assert.Null(fixture.ViewModel.SelectedTheme);
    }

    [Fact]
    public async Task Initialize_ShowsCachedStatusWhileLiveConfirmationRuns()
    {
        using var fixture = new ViewModelFixture(themeCount: 1);
        fixture.Runtime.CachedCompatibility =
            new CodexCachedCompatibilityStatus(
                "26.715.4045.0",
                "OpenAI.Codex_26.715.4045.0_x64__2p2nqsd0c76g0",
                @"C:\Program Files\WindowsApps\OpenAI.Codex\ChatGPT.exe",
                new string('a', 64),
                CodexCompatibilityLevel.Verified,
                CodexIdentityAssessment.TrustedStore,
                CodexInstallationSource.StoreAutomatic,
                DateTimeOffset.Parse("2026-07-24T06:00:00Z"),
                true);
        fixture.Runtime.StatusGate =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedTheme = fixture.ViewModel.Themes[0];

        Assert.Equal(CodexDetectionPhase.Confirming, fixture.ViewModel.CodexStatusPhase);
        Assert.Equal("请稍作等待，程序加载中", fixture.ViewModel.CodexStatusText);
        Assert.True(fixture.ViewModel.ApplyTemporaryCommand.CanExecute(null));

        fixture.Runtime.StatusGate.SetResult();
        await fixture.ViewModel.WaitForBackgroundInitializationAsync();

        Assert.Equal(CodexDetectionPhase.Live, fixture.ViewModel.CodexStatusPhase);
        Assert.True(fixture.ViewModel.ApplyTemporaryCommand.CanExecute(null));
        Assert.Equal(1, fixture.Runtime.GetStatusCalls);
    }

    [Fact]
    public async Task SettingsTabs_LoadDiagnosticsAndPreserveSelection()
    {
        using var fixture = new ViewModelFixture(themeCount: 1);

        Assert.Equal(
            SettingsSection.General,
            fixture.ViewModel.SelectedSettingsSection);

        fixture.ViewModel.SelectedSettingsSection = SettingsSection.Diagnostics;
        await WaitUntilAsync(() => fixture.Diagnostics.ReadCalls == 1);

        Assert.Equal(
            DiagnosticHealth.Normal,
            fixture.ViewModel.DiagnosticHealth);
        Assert.Equal("诊断记录正常", fixture.ViewModel.DiagnosticStatusText);
        var item = Assert.Single(fixture.ViewModel.DiagnosticEvents);
        Assert.Equal("持久主题运行正常", item.Title);

        fixture.ViewModel.SelectedSettingsSection = SettingsSection.About;
        fixture.ViewModel.NavigateCommand.Execute("Settings");

        Assert.Equal(
            SettingsSection.About,
            fixture.ViewModel.SelectedSettingsSection);
    }

    [Fact]
    public void OpenGitHubRepository_UsesConfiguredRepositoryUrl()
    {
        using var fixture = new ViewModelFixture(themeCount: 1);

        fixture.ViewModel.OpenGitHubRepositoryCommand.Execute(null);

        Assert.Equal(
            "https://github.com/Aenvo/Codex-Theme-Studio",
            fixture.OpenedExternalUrl);
    }

    [Fact]
    public async Task DiagnosticSummary_IsCopiedFromSanitizedBundleService()
    {
        using var fixture = new ViewModelFixture(themeCount: 1);

        fixture.ViewModel.CopyDiagnosticSummaryCommand.Execute(null);
        await WaitUntilAsync(() => fixture.Diagnostics.DraftCalls == 1);

        Assert.Equal("safe issue summary", fixture.CopiedText);
        Assert.Equal("Success", fixture.ViewModel.NotificationKind);
        Assert.Contains("报告 ID", fixture.ViewModel.NotificationMessage);
    }

    [Fact]
    public async Task CodexDetection_DistinguishesMissingFromCapabilityBlocked()
    {
        using var missing = new ViewModelFixture(themeCount: 1);
        missing.Runtime.Status = Status(
            ThemeRuntimeState.NotInstalled,
            themeId: null,
            persistenceEnabled: false) with
        {
            UserMessage = "未检测到 Codex；可以在设置中手动选择可执行文件。",
        };

        await missing.ViewModel.InitializeAsync();

        Assert.False(missing.ViewModel.IsCodexDetected);
        Assert.Equal("未检测到 ChatGPT (Codex)", missing.ViewModel.CodexStatusText);
        Assert.False(missing.ViewModel.IsNotificationVisible);

        using var blocked = new ViewModelFixture(themeCount: 1);
        blocked.Runtime.Status = Status(
            ThemeRuntimeState.Unsupported,
            themeId: null,
            persistenceEnabled: false) with
        {
            UserMessage = "Codex 缺少主题运行所需能力，未执行注入。",
            CompatibilityLevel = CodexCompatibilityLevel.Incompatible,
            IsPersistenceEligible = false,
        };

        await blocked.ViewModel.InitializeAsync();

        Assert.True(blocked.ViewModel.IsCodexDetected);
        Assert.Equal("已检测到 ChatGPT (Codex)", blocked.ViewModel.CodexStatusText);
        Assert.True(blocked.ViewModel.IsNotificationVisible);
        Assert.Equal("Warning", blocked.ViewModel.NotificationKind);
        Assert.Contains("所需能力", blocked.ViewModel.NotificationMessage);
        Assert.False(blocked.ViewModel.SetPersistentCommand.CanExecute(null));
        Assert.Contains("先临时应用", blocked.ViewModel.PersistenceEligibilityMessage);
    }

    [Fact]
    public async Task CompatibilityStatus_MapsVerifiedProbeSourceWarningAndIncompatible()
    {
        var cases = new[]
        {
            (
                CodexCompatibilityLevel.Verified,
                CodexIdentityAssessment.TrustedStore,
                "已验证版本 · 能力探测通过"),
            (
                CodexCompatibilityLevel.CompatibleByProbe,
                CodexIdentityAssessment.TrustedStore,
                "未知版本 · 能力探测兼容"),
            (
                CodexCompatibilityLevel.CompatibleByProbe,
                CodexIdentityAssessment.UnverifiedSource,
                "来源未验证 · 能力探测兼容"),
            (
                CodexCompatibilityLevel.Incompatible,
                CodexIdentityAssessment.UnverifiedSource,
                "能力不兼容"),
        };

        foreach (var (level, identity, expected) in cases)
        {
            using var fixture = new ViewModelFixture(themeCount: 1);
            fixture.Runtime.Status = Status(
                ThemeRuntimeState.NotRunning,
                themeId: null,
                persistenceEnabled: false) with
            {
                CompatibilityLevel = level,
                IdentityAssessment = identity,
            };

            await fixture.ViewModel.InitializeAsync();

            Assert.Equal(expected, fixture.ViewModel.CodexCompatibilityText);
        }
    }

    [Fact]
    public async Task Initialize_WithOneHundredThemes_FiltersChineseEmojiAndTags()
    {
        using var fixture = new ViewModelFixture(themeCount: 100);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await fixture.ViewModel.InitializeAsync();
        stopwatch.Stop();
        output.WriteLine(
            "100 themes initialize: {0:F3} ms",
            stopwatch.Elapsed.TotalMilliseconds);

        Assert.Equal(100, fixture.ViewModel.Themes.Count);

        fixture.ViewModel.SearchText = "夜航";
        Assert.Single(fixture.ViewModel.Themes);
        Assert.Contains("🌙", fixture.ViewModel.Themes[0].DisplayName, StringComparison.Ordinal);

        fixture.ViewModel.SearchText = string.Empty;
        fixture.ViewModel.SelectedTag = "暖色";
        Assert.Equal(25, fixture.ViewModel.Themes.Count);

        fixture.ViewModel.NavigateCommand.Execute("Favorites");
        Assert.All(fixture.ViewModel.Themes, theme => Assert.True(theme.IsFavorite));
    }

    [Fact]
    public async Task Initialize_DistinguishesTemporaryPersistentAndSelectedThemes()
    {
        using var fixture = new ViewModelFixture(themeCount: 5);
        var temporaryId = fixture.Repository.Summaries[1].ThemeId;
        var persistentId = fixture.Repository.Summaries[2].ThemeId;
        fixture.Runtime.Status = Status(
            ThemeRuntimeState.Temporary,
            temporaryId,
            persistenceEnabled: false);
        fixture.Repository.Summaries[2] = fixture.Repository.Summaries[2] with
        {
            IsCurrentPersistent = true,
        };

        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedTheme = fixture.ViewModel.Themes[0];

        Assert.True(fixture.ViewModel.Themes.Single(x => x.ThemeId == temporaryId).IsTemporary);
        Assert.True(fixture.ViewModel.Themes.Single(x => x.ThemeId == persistentId).IsPersistent);
        Assert.False(fixture.ViewModel.SelectedTheme.IsTemporary);
        Assert.False(fixture.ViewModel.SelectedTheme.IsPersistent);
    }

    [Fact]
    public async Task Initialize_PreservesTemporaryRuntimeThemeWhenPersistenceIsConfigured()
    {
        using var fixture = new ViewModelFixture(
            themeCount: 2,
            managedPersistenceEnabled: true);
        var temporaryId = fixture.Repository.Summaries[0].ThemeId;
        var persistentId = fixture.Repository.Summaries[1].ThemeId;
        fixture.Repository.Summaries[1] = fixture.Repository.Summaries[1] with
        {
            IsCurrentPersistent = true,
        };
        fixture.Runtime.Status = Status(
            ThemeRuntimeState.Temporary,
            temporaryId,
            persistenceEnabled: true);
        fixture.Persistence.Status = fixture.Runtime.Status with
        {
            SelectedThemeId = persistentId,
        };

        await fixture.ViewModel.InitializeAsync();

        var temporaryTheme = fixture.ViewModel.Themes.Single(
            theme => theme.ThemeId == temporaryId);
        Assert.True(temporaryTheme.IsTemporary);
        Assert.False(temporaryTheme.IsPersistent);
        Assert.True(fixture.ViewModel.Themes.Single(
            theme => theme.ThemeId == persistentId).IsPersistent);
    }

    [Fact]
    public async Task ApplyTemporary_OverridesTheActiveBadgeWithoutClearingPersistentConfiguration()
    {
        using var fixture = new ViewModelFixture(
            themeCount: 2,
            managedPersistenceEnabled: true);
        var persistentId = fixture.Repository.Summaries[^1].ThemeId;
        fixture.Repository.Summaries[^1] = fixture.Repository.Summaries[^1] with
        {
            IsCurrentPersistent = true,
        };
        await fixture.ViewModel.InitializeAsync();

        var otherTheme = fixture.ViewModel.Themes.Single(
            theme => theme.ThemeId != persistentId);
        fixture.ViewModel.SelectedTheme = otherTheme;
        fixture.ViewModel.ApplyTemporaryCommand.Execute(null);
        await WaitUntilAsync(() => !fixture.ViewModel.IsBusy);

        Assert.True(otherTheme.IsTemporary);
        Assert.False(otherTheme.IsPersistent);
        Assert.True(fixture.ViewModel.Themes.Single(
            theme => theme.ThemeId == persistentId).IsPersistent);

        var persistentTheme = fixture.ViewModel.Themes.Single(
            theme => theme.ThemeId == persistentId);
        fixture.ViewModel.SelectedTheme = persistentTheme;
        fixture.ViewModel.ApplyTemporaryCommand.Execute(null);
        await WaitUntilAsync(() => !fixture.ViewModel.IsBusy);

        Assert.True(persistentTheme.IsTemporary);
        Assert.False(persistentTheme.IsPersistent);
        Assert.True(fixture.Repository.Summaries.Single(
            theme => theme.ThemeId == persistentId).IsCurrentPersistent);
    }

    [Fact]
    public async Task ToggleFavorite_UsesClickedCardWithoutChangingSelection()
    {
        using var fixture = new ViewModelFixture(themeCount: 3);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedTheme = fixture.ViewModel.Themes[0];
        var selectedId = fixture.ViewModel.SelectedTheme.ThemeId;
        var clicked = fixture.ViewModel.Themes[1];
        var expectedFavorite = !clicked.IsFavorite;

        fixture.ViewModel.ToggleFavoriteCommand.Execute(clicked);
        await WaitUntilAsync(() => !fixture.ViewModel.IsBusy);

        Assert.Equal(selectedId, fixture.ViewModel.SelectedTheme?.ThemeId);
        Assert.Equal(
            expectedFavorite,
            fixture.Repository.Summaries.Single(item => item.ThemeId == clicked.ThemeId).IsFavorite);
        Assert.Equal(
            expectedFavorite ? "已加入收藏。" : "已取消收藏。",
            fixture.ViewModel.NotificationMessage);
    }

    [Fact]
    public async Task Apply_DisablesConflictingCommandsUntilAsyncOperationCompletes()
    {
        using var fixture = new ViewModelFixture(themeCount: 3);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedTheme = fixture.ViewModel.Themes[0];
        fixture.Runtime.ApplyGate =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        fixture.ViewModel.ApplyTemporaryCommand.Execute(null);
        await fixture.Runtime.ApplyEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(fixture.ViewModel.IsBusy);
        Assert.False(fixture.ViewModel.RestoreCommand.CanExecute(null));
        Assert.False(fixture.ViewModel.SetPersistentCommand.CanExecute(null));

        fixture.Runtime.ApplyGate.SetResult();
        await WaitUntilAsync(() => !fixture.ViewModel.IsBusy);
        Assert.Contains(
            "已临时应用",
            fixture.ViewModel.NotificationMessage,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TemporaryApply_RetriesInspectorContentionWithoutShowingFailure()
    {
        using var fixture = new ViewModelFixture(themeCount: 1);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedTheme = Assert.Single(fixture.ViewModel.Themes);
        fixture.Runtime.ApplyResults.Enqueue(
            OperationResult<ThemeRuntimeStatus>.Failure(
                OperationErrorCode.Conflict,
                "另一个 Codex Inspector 操作正在执行，请稍后重试。",
                "injector.operation_busy"));

        fixture.ViewModel.ApplyTemporaryCommand.Execute(null);
        await WaitUntilAsync(() => !fixture.ViewModel.IsBusy);

        Assert.Equal(2, fixture.Runtime.ApplyCalls);
        Assert.True(fixture.ViewModel.SelectedTheme.IsTemporary);
        Assert.Equal("Success", fixture.ViewModel.NotificationKind);
        Assert.DoesNotContain(
            "另一个 Codex Inspector 操作",
            fixture.ViewModel.NotificationMessage,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TemporaryApply_QualificationTimeoutShowsTimeoutNotCancellation()
    {
        using var fixture = new ViewModelFixture(themeCount: 1);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedTheme = Assert.Single(fixture.ViewModel.Themes);
        fixture.Runtime.ApplyResults.Enqueue(
            OperationResult<ThemeRuntimeStatus>.Failure(
                OperationErrorCode.Timeout,
                "首次兼容验证超时；已执行安全恢复，请重试。",
                "compatibility.qualification_timeout"));

        fixture.ViewModel.ApplyTemporaryCommand.Execute(null);
        await WaitUntilAsync(() => !fixture.ViewModel.IsBusy);

        Assert.Equal("Error", fixture.ViewModel.NotificationKind);
        Assert.Contains("首次兼容验证超时", fixture.ViewModel.NotificationMessage);
        Assert.DoesNotContain("操作已取消", fixture.ViewModel.NotificationMessage);
    }

    [Fact]
    public async Task OfflineActionsRemainClickableAndExplainUnavailableWork()
    {
        using var fixture = new ViewModelFixture(themeCount: 1);
        fixture.Runtime.Status = Status(
            ThemeRuntimeState.NotRunning,
            themeId: null,
            persistenceEnabled: false) with
        {
            IsPersistenceEligible = false,
            UserMessage = "Codex 未运行；请先启动 Codex。",
        };
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.WaitForBackgroundInitializationAsync();
        fixture.ViewModel.SelectedTheme = Assert.Single(fixture.ViewModel.Themes);

        Assert.True(fixture.ViewModel.ApplyTemporaryCommand.CanExecute(null));
        Assert.True(fixture.ViewModel.SetPersistentCommand.CanExecute(null));
        Assert.True(fixture.ViewModel.RestoreCommand.CanExecute(null));

        fixture.ViewModel.ApplyTemporaryCommand.Execute(null);
        Assert.Contains("请先启动 Codex", fixture.ViewModel.NotificationMessage);
        Assert.False(fixture.Runtime.ApplyEntered.Task.IsCompleted);

        fixture.ViewModel.SetPersistentCommand.Execute(null);
        Assert.Contains("尚未取得持久化资格", fixture.ViewModel.NotificationMessage);
        Assert.Equal(0, fixture.Persistence.EnableCalls);

        fixture.ViewModel.RestoreCommand.Execute(null);
        await WaitUntilAsync(() => fixture.ViewModel.NotificationMessage.Contains(
            "当前已是官方外观",
            StringComparison.Ordinal));
        Assert.Equal(0, fixture.Runtime.RestoreCalls);
    }

    [Fact]
    public async Task OfflineQualifiedThemeEnablesPersistenceWithoutRuntimeProcess()
    {
        using var fixture = new ViewModelFixture(themeCount: 1);
        fixture.Runtime.Status = Status(
            ThemeRuntimeState.NotRunning,
            themeId: null,
            persistenceEnabled: false) with
        {
            IsPersistenceEligible = true,
        };
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.WaitForBackgroundInitializationAsync();
        fixture.ViewModel.SelectedTheme = Assert.Single(fixture.ViewModel.Themes);

        Assert.Equal(
            "Codex 应用可永远保持主题持久化，直到还原外观",
            fixture.ViewModel.PersistenceEligibilityMessage);
        fixture.ViewModel.SetPersistentCommand.Execute(null);
        await WaitUntilAsync(() => !fixture.ViewModel.IsBusy);

        Assert.Equal(1, fixture.Persistence.EnableCalls);
        Assert.True(fixture.ViewModel.IsPersistenceEnabled);
    }

    [Fact]
    public async Task WindowActivationDetectsCodexStartedAfterThemeStudio()
    {
        using var fixture = new ViewModelFixture(
            themeCount: 1,
            enablePresenceDiscovery: true);
        fixture.Runtime.Status = Status(
            ThemeRuntimeState.NotRunning,
            themeId: null,
            persistenceEnabled: false);
        fixture.Discovery.Processes = [];
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.WaitForBackgroundInitializationAsync();

        fixture.Runtime.Status = Status(
            ThemeRuntimeState.Ready,
            themeId: null,
            persistenceEnabled: false);
        fixture.Discovery.Processes =
        [
            new CodexProcessInfo(
                1234,
                DateTimeOffset.Parse("2026-07-26T08:00:00Z"),
                FakeCodexDiscoveryService.ExecutablePath,
                null),
        ];
        fixture.ViewModel.OnWindowActivated();
        await fixture.ViewModel.WaitForPresenceMonitorAsync();
        fixture.ViewModel.SelectedTheme = Assert.Single(fixture.ViewModel.Themes);
        fixture.ViewModel.ApplyTemporaryCommand.Execute(null);
        await fixture.Runtime.ApplyEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(fixture.Runtime.GetStatusCalls >= 2);
        Assert.True(fixture.Discovery.DiscoverCalls >= 1);
    }

    [Fact]
    public async Task StartupActivationWaitsForBackgroundRefreshBeforePresenceMonitor()
    {
        using var fixture = new ViewModelFixture(
            themeCount: 1,
            enablePresenceDiscovery: true);
        fixture.Runtime.StatusGate =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Discovery.Processes =
        [
            new CodexProcessInfo(
                1234,
                DateTimeOffset.Parse("2026-07-26T08:00:00Z"),
                FakeCodexDiscoveryService.ExecutablePath,
                null),
        ];

        fixture.ViewModel.OnWindowActivated();
        await fixture.ViewModel.InitializeAsync();
        await WaitUntilAsync(() => fixture.Runtime.GetStatusCalls == 1);

        Assert.Equal(0, fixture.Discovery.DiscoverCalls);

        fixture.Runtime.StatusGate.SetResult();
        await fixture.ViewModel.WaitForBackgroundInitializationAsync();
        await fixture.ViewModel.WaitForPresenceMonitorAsync();

        Assert.Equal(1, fixture.Discovery.DiscoverCalls);
        Assert.Equal(2, fixture.Runtime.GetStatusCalls);
    }

    [Fact]
    public async Task WindowDeactivationCancelsPresenceRetry()
    {
        using var fixture = new ViewModelFixture(
            themeCount: 1,
            enablePresenceDiscovery: true);
        fixture.Runtime.Status = Status(
            ThemeRuntimeState.NotRunning,
            themeId: null,
            persistenceEnabled: false);
        fixture.Discovery.Processes = [];
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.WaitForBackgroundInitializationAsync();

        fixture.ViewModel.OnWindowActivated();
        await WaitUntilAsync(() => fixture.Discovery.DiscoverCalls > 0);
        fixture.ViewModel.OnWindowDeactivated();
        await fixture.ViewModel.WaitForPresenceMonitorAsync()
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(fixture.Discovery.DiscoverCalls >= 1);
    }

    [Fact]
    public async Task CurrentTheme_CannotBeDeleted()
    {
        using var fixture = new ViewModelFixture(themeCount: 2);
        fixture.Runtime.Status = Status(
            ThemeRuntimeState.Temporary,
            fixture.Repository.Summaries[0].ThemeId,
            persistenceEnabled: false);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedTheme = fixture.ViewModel.Themes[0];

        Assert.False(fixture.ViewModel.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public async Task Trash_RestoresSoftDeletedThemeToWorkspace()
    {
        using var fixture = new ViewModelFixture(themeCount: 3);
        await fixture.ViewModel.InitializeAsync();
        var deletedId = fixture.ViewModel.Themes[0].ThemeId;
        fixture.ViewModel.SelectedTheme = fixture.ViewModel.Themes[0];

        fixture.ViewModel.DeleteCommand.Execute(null);
        await WaitUntilAsync(
            () =>
                !fixture.ViewModel.IsBusy &&
                fixture.Repository.DeletedThemeIds.Contains(deletedId));

        Assert.Equal(2, fixture.ViewModel.Themes.Count);
        fixture.ViewModel.NavigateCommand.Execute("Trash");

        Assert.True(fixture.ViewModel.IsTrashVisible);
        Assert.False(fixture.ViewModel.IsLibraryVisible);
        Assert.Equal("回收站", fixture.ViewModel.PageTitle);
        Assert.Equal("1 个已删除主题", fixture.ViewModel.ThemeCountText);
        fixture.ViewModel.SelectedTheme = Assert.Single(fixture.ViewModel.Themes);
        Assert.Equal(deletedId, fixture.ViewModel.SelectedTheme.ThemeId);

        fixture.ViewModel.RestoreDeletedCommand.Execute(null);
        await WaitUntilAsync(
            () =>
                !fixture.ViewModel.IsBusy &&
                !fixture.Repository.DeletedThemeIds.Contains(deletedId));

        Assert.Empty(fixture.ViewModel.Themes);
        fixture.ViewModel.NavigateCommand.Execute("All");
        Assert.Equal(3, fixture.ViewModel.Themes.Count);
    }

    [Fact]
    public async Task Trash_PermanentDeleteRequiresConfirmation()
    {
        using var fixture = new ViewModelFixture(themeCount: 2);
        var deletedId = fixture.Repository.Summaries[0].ThemeId;
        fixture.Repository.DeletedThemeIds.Add(deletedId);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.NavigateCommand.Execute("Trash");
        fixture.ViewModel.SelectedTheme = Assert.Single(fixture.ViewModel.Themes);
        fixture.Dialogs.ConfirmResult = false;

        fixture.ViewModel.PermanentlyDeleteCommand.Execute(null);
        await WaitUntilAsync(() => fixture.Dialogs.Confirmations.Count == 1);

        Assert.Equal(0, fixture.Repository.PermanentDeleteCalls);
        Assert.Single(fixture.ViewModel.Themes);
        Assert.Equal("永久删除主题", fixture.Dialogs.Confirmations[0].Title);
        Assert.Contains("无法在应用内还原", fixture.Dialogs.Confirmations[0].Message);

        fixture.Dialogs.ConfirmResult = true;
        fixture.ViewModel.PermanentlyDeleteCommand.Execute(null);
        await WaitUntilAsync(
            () =>
                !fixture.ViewModel.IsBusy &&
                fixture.Repository.PermanentDeleteCalls == 1);

        Assert.Empty(fixture.ViewModel.Themes);
        Assert.False(fixture.Repository.Themes.ContainsKey(deletedId));
    }

    [Fact]
    public async Task Trash_EmptyRequiresConfirmationAndPurgesEveryDeletedTheme()
    {
        using var fixture = new ViewModelFixture(themeCount: 3);
        fixture.Repository.DeletedThemeIds.Add(fixture.Repository.Summaries[0].ThemeId);
        fixture.Repository.DeletedThemeIds.Add(fixture.Repository.Summaries[1].ThemeId);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.NavigateCommand.Execute("Trash");

        fixture.ViewModel.EmptyTrashCommand.Execute(null);
        await WaitUntilAsync(
            () =>
                !fixture.ViewModel.IsBusy &&
                fixture.Repository.PermanentDeleteCalls == 2);

        Assert.Empty(fixture.ViewModel.Themes);
        var confirmation = Assert.Single(fixture.Dialogs.Confirmations);
        Assert.Equal("清空回收站", confirmation.Title);
        Assert.Contains("2 个主题", confirmation.Message);
    }

    [Fact]
    public async Task Restore_DisablesExternalPersistenceAndClearsExternalBadges()
    {
        using var fixture = new ViewModelFixture(themeCount: 1, externalThemeActive: true);
        await fixture.ViewModel.InitializeAsync();
        Assert.True(Assert.Single(fixture.ViewModel.Themes).IsExternalActive);

        fixture.ViewModel.RestoreCommand.Execute(null);
        await WaitUntilAsync(() => !fixture.ViewModel.IsBusy);

        var confirmation = Assert.Single(fixture.Dialogs.Confirmations);
        Assert.Contains("OkkSkin", confirmation.Message);
        Assert.Contains("主题、图片和缓存不会删除", confirmation.Message);
        Assert.Equal(1, fixture.ExternalPersistence.DisableCalls);
        Assert.Equal(1, fixture.Runtime.RestoreCalls);
        var doro = Assert.Single(fixture.ViewModel.Themes);
        Assert.False(doro.IsExternalActive);
        Assert.False(doro.IsExternalPersistent);
        Assert.False(doro.ShowPersistentStatus);
        Assert.False(doro.ShowTemporaryStatus);
        Assert.Contains("下次启动仍保持官方外观", fixture.ViewModel.CodexStatusDetail);
    }

    [Fact]
    public async Task Restore_TemporaryThemeDoesNotRequireConfirmation()
    {
        using var fixture = new ViewModelFixture(themeCount: 1);
        var themeId = fixture.Repository.Summaries[0].ThemeId;
        fixture.Runtime.Status = fixture.Runtime.Status with
        {
            State = ThemeRuntimeState.Temporary,
            ThemeId = themeId,
        };
        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.RestoreCommand.Execute(null);
        await WaitUntilAsync(() => !fixture.ViewModel.IsBusy);

        Assert.Empty(fixture.Dialogs.Confirmations);
        Assert.Equal(1, fixture.Runtime.RestoreCalls);
        Assert.Equal(0, fixture.Persistence.DisableCalls);
        Assert.Equal(0, fixture.ExternalPersistence.DisableCalls);
    }

    [Fact]
    public async Task Restore_ManagedPersistenceRequiresConfirmationAndDisablesAgent()
    {
        using var fixture = new ViewModelFixture(
            themeCount: 1,
            managedPersistenceEnabled: true);
        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.RestoreCommand.Execute(null);
        await WaitUntilAsync(() => !fixture.ViewModel.IsBusy);

        var confirmation = Assert.Single(fixture.Dialogs.Confirmations);
        Assert.Contains("Theme Studio", confirmation.Message);
        Assert.Equal(1, fixture.Persistence.DisableCalls);
        Assert.Equal(1, fixture.Runtime.RestoreCalls);
        Assert.False(fixture.ViewModel.IsPersistenceEnabled);
    }

    [Fact]
    public async Task Restore_CancelledConfirmationLeavesAllPersistenceUnchanged()
    {
        using var fixture = new ViewModelFixture(
            themeCount: 1,
            externalThemeActive: true,
            managedPersistenceEnabled: true);
        fixture.Dialogs.ConfirmResult = false;
        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.RestoreCommand.Execute(null);
        await WaitUntilAsync(() => !fixture.ViewModel.IsBusy);

        Assert.Single(fixture.Dialogs.Confirmations);
        Assert.Equal(0, fixture.Persistence.DisableCalls);
        Assert.Equal(0, fixture.ExternalPersistence.DisableCalls);
        Assert.Equal(0, fixture.Runtime.RestoreCalls);
    }

    [Fact]
    public async Task Restore_IgnoresSelectedCardAndDisablesBothProviders()
    {
        using var fixture = new ViewModelFixture(
            themeCount: 2,
            externalThemeActive: true,
            managedPersistenceEnabled: true);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedTheme = fixture.ViewModel.Themes[1];
        Assert.False(fixture.ViewModel.SelectedTheme.Summary.IsSourceReadOnly);

        fixture.ViewModel.RestoreCommand.Execute(null);
        await WaitUntilAsync(() => !fixture.ViewModel.IsBusy);

        var confirmation = Assert.Single(fixture.Dialogs.Confirmations);
        Assert.Contains("Theme Studio", confirmation.Message);
        Assert.Contains("OkkSkin", confirmation.Message);
        Assert.Equal(1, fixture.Persistence.DisableCalls);
        Assert.Equal(1, fixture.ExternalPersistence.DisableCalls);
        Assert.Equal(1, fixture.Runtime.RestoreCalls);
    }

    [Fact]
    public async Task Restore_ExternalFailureReportsPartialAndDoesNotClaimSuccess()
    {
        using var fixture = new ViewModelFixture(themeCount: 1, externalThemeActive: true);
        fixture.ExternalPersistence.DisableResult =
            OperationResult<ExternalPersistenceDisableResult>.Failure(
                OperationErrorCode.AccessDenied,
                "没有权限移除 OkkSkin 启动项。",
                "test.external.access_denied");
        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.RestoreCommand.Execute(null);
        await WaitUntilAsync(() => !fixture.ViewModel.IsBusy);

        Assert.Equal("Warning", fixture.ViewModel.NotificationKind);
        Assert.Contains("持久化残留", fixture.ViewModel.NotificationMessage);
        Assert.Contains("没有权限", fixture.ViewModel.NotificationMessage);
        Assert.DoesNotContain(
            "下次启动仍保持官方外观",
            fixture.ViewModel.NotificationMessage);
    }

    [Fact]
    public async Task Restore_ManagedFailureReportsPartialAndStillRestoresCurrentAppearance()
    {
        using var fixture = new ViewModelFixture(
            themeCount: 1,
            managedPersistenceEnabled: true);
        fixture.Persistence.DisableResult =
            OperationResult<ThemeRuntimeStatus>.Failure(
                OperationErrorCode.AccessDenied,
                "没有权限移除 Theme Studio 启动项。",
                "test.persistence.access_denied");
        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.RestoreCommand.Execute(null);
        await WaitUntilAsync(() => !fixture.ViewModel.IsBusy);

        Assert.Equal(1, fixture.Persistence.DisableCalls);
        Assert.Equal(1, fixture.Runtime.RestoreCalls);
        Assert.Equal("Warning", fixture.ViewModel.NotificationKind);
        Assert.Contains("Theme Studio", fixture.ViewModel.NotificationMessage);
        Assert.True(fixture.ViewModel.IsPersistenceEnabled);
    }

    [Fact]
    public async Task CardSubtitle_UsesLocalLabelButPreservesExternalTags()
    {
        using var localFixture = new ViewModelFixture(themeCount: 1);
        await localFixture.ViewModel.InitializeAsync();
        var local = Assert.Single(localFixture.ViewModel.Themes);
        Assert.Equal("本地主题", local.CardSubtitleText);
        Assert.NotEqual(local.TagsText, local.CardSubtitleText);

        using var externalFixture = new ViewModelFixture(
            themeCount: 1,
            externalThemeActive: true);
        await externalFixture.ViewModel.InitializeAsync();
        var external = Assert.Single(externalFixture.ViewModel.Themes);
        Assert.Equal(external.TagsText, external.CardSubtitleText);
    }

    [Fact]
    public async Task ThemeCards_ExposePaletteColorsForLocalAndExternalThemes()
    {
        using var localFixture = new ViewModelFixture(themeCount: 1);
        await localFixture.ViewModel.InitializeAsync();
        var local = Assert.Single(localFixture.ViewModel.Themes);
        var localPackage = localFixture.Repository.Themes[local.ThemeId];

        Assert.True(local.HasPalette);
        Assert.Equal(
            [
                localPackage.Palette.Background,
                localPackage.Palette.Panel,
                localPackage.Palette.Accent,
                localPackage.Palette.Text,
                localPackage.Palette.Muted,
                localPackage.Palette.Border,
            ],
            local.PaletteColors);

        using var externalFixture = new ViewModelFixture(
            themeCount: 1,
            externalThemeActive: true);
        await externalFixture.ViewModel.InitializeAsync();
        var external = Assert.Single(externalFixture.ViewModel.Themes);

        Assert.True(external.HasPalette);
        Assert.Equal(6, external.PaletteColors.Count);
    }

    [Fact]
    public async Task ExternalPersistentTheme_AllowsPersistenceActionWithoutStartingSecondAgent()
    {
        using var fixture = new ViewModelFixture(themeCount: 1, externalThemeActive: true);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedTheme = Assert.Single(fixture.ViewModel.Themes);

        Assert.True(fixture.ViewModel.SetPersistentCommand.CanExecute(null));

        fixture.ViewModel.SetPersistentCommand.Execute(null);

        Assert.Contains("已由 OkkSkin 持久化", fixture.ViewModel.NotificationMessage);
        Assert.Equal(0, fixture.Persistence.EnableCalls);
    }

    [Fact]
    public async Task CreateTheme_CreatesUnnamedDraftWithoutPrompt()
    {
        using var fixture = new ViewModelFixture(themeCount: 1);

        fixture.ViewModel.CreateCommand.Execute(null);
        await WaitUntilAsync(() => fixture.ViewModel.CurrentPage == LibraryPage.Editor);

        Assert.True(fixture.Editor.HasDraft);
        Assert.True(fixture.Editor.IsNew);
        Assert.Equal("未命名主题", fixture.Editor.Name);
        Assert.Equal(0, fixture.Dialogs.RequestTextCalls);
    }

    [Fact]
    public async Task Editor_CancelSaveAndSaveCopy_HaveDistinctDraftSemantics()
    {
        using var fixture = new ViewModelFixture(themeCount: 1);
        var id = fixture.Repository.Summaries[0].ThemeId;
        var original = fixture.Repository.Themes[id];
        fixture.Editor.Begin(original, newTheme: false);
        fixture.Editor.Name = "未保存草稿";
        fixture.Editor.Cancel();
        Assert.Equal(original, fixture.Repository.Themes[id]);

        fixture.Editor.Begin(original, newTheme: false);
        fixture.Editor.Name = "已保存主题";
        var saved = await fixture.Editor.SaveAsync(
            saveCopy: false,
            copyName: null,
            CancellationToken.None);
        Assert.True(saved.IsSuccess);
        Assert.Equal("已保存主题", fixture.Repository.Themes[id].Name);

        var copy = await fixture.Editor.SaveAsync(
            saveCopy: true,
            copyName: "独立副本",
            CancellationToken.None);
        Assert.True(copy.IsSuccess);
        Assert.NotEqual(id, copy.Value!.Id);
        Assert.Equal("独立副本", fixture.Repository.Themes[copy.Value.Id].Name);
    }

    [Fact]
    public void Editor_HomeAndTaskPreview_AreIndependent()
    {
        using var fixture = new ViewModelFixture(themeCount: 1);
        var theme = fixture.Repository.Themes[fixture.Repository.Summaries[0].ThemeId];
        fixture.Editor.Begin(theme, newTheme: false);
        var homeOpacity = fixture.Editor.PreviewOpacity;

        Assert.Equal(theme.Art.HomeOverlay, fixture.Editor.PreviewOverlay);
        Assert.Equal(0, fixture.Editor.TaskContentOverlay);

        fixture.Editor.IsTaskPreview = true;

        Assert.Equal(theme.Art.TaskOpacity, fixture.Editor.PreviewOpacity);
        Assert.Equal(0, fixture.Editor.PreviewOverlay);
        Assert.Equal(theme.Art.TaskOverlay, fixture.Editor.TaskContentOverlay);
        Assert.True(fixture.Editor.IsTaskOverlayEnabled);
        Assert.NotEqual(homeOpacity, fixture.Editor.PreviewOpacity);

        fixture.Editor.TaskMode = ThemeTaskMode.Hidden;

        Assert.Equal(1, fixture.Editor.TaskContentOverlay);
        Assert.False(fixture.Editor.IsTaskOverlayEnabled);
    }

    [Fact]
    public void Editor_BeginResetsToHomePreviewAndNeutralDefaults()
    {
        using var fixture = new ViewModelFixture(themeCount: 1);
        var theme = fixture.Repository.Themes[fixture.Repository.Summaries[0].ThemeId];
        fixture.Editor.Begin(theme, newTheme: false);
        fixture.Editor.IsTaskPreview = true;

        fixture.Editor.Begin(theme, newTheme: true);
        fixture.Editor.ResetDefaults();

        Assert.False(fixture.Editor.IsTaskPreview);
        Assert.Equal("#111111", fixture.Editor.BackgroundColor);
        Assert.Equal("#1C1C1CE6", fixture.Editor.PanelColor);
        Assert.Equal("#3B82F6", fixture.Editor.AccentColor);
        Assert.Equal("#F5F5F5", fixture.Editor.TextColor);
        Assert.Equal("#A3A3A3", fixture.Editor.MutedColor);
        Assert.Equal("#30303080", fixture.Editor.BorderColor);
        Assert.Equal(10, fixture.Editor.PanelBlur);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10, 0.972)]
    [InlineData(32, 0.9104)]
    [InlineData(64, 0.8208)]
    public void Editor_PanelBlur_UpdatesRuntimeMatchedSurfaceOpacity(
        double panelBlur,
        double expectedOpacity)
    {
        using var fixture = new ViewModelFixture(themeCount: 1);
        var theme = fixture.Repository.Themes[fixture.Repository.Summaries[0].ThemeId];
        fixture.Editor.Begin(theme, newTheme: false);

        fixture.Editor.PanelBlur = panelBlur;

        Assert.Equal(panelBlur, fixture.Editor.PanelBlur);
        Assert.Equal(expectedOpacity, fixture.Editor.PanelSurfaceOpacity, precision: 4);
    }

    [Fact]
    public void Editor_PanelBlur_IsClampedToContractRange()
    {
        using var fixture = new ViewModelFixture(themeCount: 1);
        var theme = fixture.Repository.Themes[fixture.Repository.Summaries[0].ThemeId];
        fixture.Editor.Begin(theme, newTheme: false);

        fixture.Editor.PanelBlur = 100;

        Assert.Equal(64, fixture.Editor.PanelBlur);
        Assert.Equal(0.8208, fixture.Editor.PanelSurfaceOpacity, precision: 4);
    }

    [Fact]
    public void Editor_FocusControls_AreEnabledOnlyForCropMode()
    {
        using var fixture = new ViewModelFixture(themeCount: 1);
        var theme = fixture.Repository.Themes[fixture.Repository.Summaries[0].ThemeId];
        fixture.Editor.Begin(theme, newTheme: false);

        Assert.False(fixture.Editor.IsCropMode);

        fixture.Editor.ArtSize = ThemeArtSize.Crop;
        fixture.Editor.FocusX = 0.2;
        fixture.Editor.FocusY = 0.8;
        fixture.Editor.CropScale = 1.6;

        Assert.True(fixture.Editor.IsCropMode);
        Assert.Equal(0.2, fixture.Editor.FocusX);
        Assert.Equal(0.8, fixture.Editor.FocusY);
        Assert.Equal(1.6, fixture.Editor.CropScale);

        fixture.Editor.CropScale = 4;

        Assert.Equal(3, fixture.Editor.CropScale);
    }

    [Fact]
    public async Task Initialize_WithFiveHundredThemes_CompletesAndFilters()
    {
        using var fixture = new ViewModelFixture(themeCount: 500);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await fixture.ViewModel.InitializeAsync();
        var initializeElapsed = stopwatch.Elapsed;
        stopwatch.Restart();
        fixture.ViewModel.SearchText = "夜航";
        var filterElapsed = stopwatch.Elapsed;

        Assert.Equal(500, fixture.Repository.Summaries.Count);
        Assert.Single(fixture.ViewModel.Themes);
        output.WriteLine(
            "500 themes initialize: {0:F3} ms; filter: {1:F3} ms",
            initializeElapsed.TotalMilliseconds,
            filterElapsed.TotalMilliseconds);
    }

    private static ThemeRuntimeStatus Status(
        ThemeRuntimeState state,
        Guid? themeId,
        bool persistenceEnabled) =>
        new(
            state,
            themeId,
            persistenceEnabled,
            state is ThemeRuntimeState.NotRunning or ThemeRuntimeState.NotInstalled
                ? null
                : 1234,
            DateTimeOffset.UtcNow,
            state == ThemeRuntimeState.Temporary ? "主题已临时应用。" : "状态已刷新。",
            themeId,
            Evidence: ThemeRuntimeEvidence.RuntimeMarkers);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}

internal sealed class ViewModelFixture : IDisposable
{
    public ViewModelFixture(
        int themeCount,
        bool externalThemeActive = false,
        bool managedPersistenceEnabled = false,
        bool enablePresenceDiscovery = false,
        IUpdateService? updateService = null,
        IUpdateDialogService? updateDialogs = null,
        string appVersion = "1.1.7")
    {
        Repository = new FakeThemeRepository(themeCount);
        Runtime = new FakeRuntimeService();
        Persistence = new FakePersistenceService();
        ExternalPersistence = new FakeExternalPersistenceService(
            externalThemeActive);
        Dialogs = new FakeDialogs();
        Diagnostics = new FakeDiagnosticService();
        Discovery = new FakeCodexDiscoveryService();
        Editor = new ThemeEditorViewModel(
            Repository,
            new FakeImagePipeline(),
            new FakeThemeAssetStore(),
            _ => null);
        ExternalThemeDescriptor? externalTheme = null;
        if (externalThemeActive && Repository.Summaries.Count > 0)
        {
            const string sourceIdentifier = "okkskin:doro-q";
            var summary = Repository.Summaries[0];
            Repository.Summaries[0] = summary with
            {
                SourceType = ThemeSourceType.RemoteSnapshot,
                SourceIdentifier = sourceIdentifier,
                IsSourceReadOnly = true,
            };
            externalTheme = new ExternalThemeDescriptor(
                "OkkSkin",
                sourceIdentifier,
                Repository.Themes[summary.ThemeId],
                "bg.jpg",
                new string('a', 64),
                true,
                true,
                1234,
                "Doro 当前由 OkkSkin 持久应用。");
        }

        if (managedPersistenceEnabled && Repository.Summaries.Count > 0)
        {
            var persistentThemeId = Repository.Summaries[^1].ThemeId;
            Persistence.Status = Persistence.Status with
            {
                State = ThemeRuntimeState.Persistent,
                ThemeId = persistentThemeId,
                IsPersistenceEnabled = true,
                CodexProcessId = 1234,
            };
            Runtime.Status = Runtime.Status with
            {
                State = ThemeRuntimeState.Persistent,
                ThemeId = persistentThemeId,
                IsPersistenceEnabled = true,
            };
        }

        ViewModel = new MainWindowViewModel(
            Repository,
            Runtime,
            Persistence,
            new FakeStorageLocationService(),
            Dialogs,
            _ => null,
            new FakeThemePackageService(),
            Editor,
            externalTheme,
            ExternalPersistence,
            diagnosticSink: Diagnostics,
            diagnosticQuery: Diagnostics,
            diagnosticBundle: Diagnostics,
            copyText: text => CopiedText = text,
            appVersion: appVersion,
            diagnosticSessionId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            openExternalUrl: url => OpenedExternalUrl = url,
            codexDiscovery: enablePresenceDiscovery ? Discovery : null,
            updateService: updateService,
            updateDialogs: updateDialogs);
    }

    public FakeThemeRepository Repository { get; }

    public FakeRuntimeService Runtime { get; }

    public FakePersistenceService Persistence { get; }

    public FakeExternalPersistenceService ExternalPersistence { get; }

    public FakeDialogs Dialogs { get; }

    public FakeDiagnosticService Diagnostics { get; }

    public FakeCodexDiscoveryService Discovery { get; }

    public string? CopiedText { get; private set; }

    public string? OpenedExternalUrl { get; private set; }

    public ThemeEditorViewModel Editor { get; }

    public MainWindowViewModel ViewModel { get; }

    public void Dispose() => ViewModel.Dispose();
}

internal sealed class FakeThemeRepository : IThemeRepository
{
    public FakeThemeRepository(int themeCount)
    {
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < themeCount; index++)
        {
            var id = Guid.NewGuid();
            var name = index == 42
                ? "🌙 夜航 · 这是一个用于验证超长主题名称省略显示但仍保留完整工具提示的中文主题"
                : $"测试主题 {index + 1:000}";
            var tags = index % 4 == 0 ? new[] { "暖色", "精选" } : new[] { "冷色" };
            var summary = new ThemeSummary(
                id,
                name,
                1,
                now.AddDays(-index),
                now.AddMinutes(-index),
                index % 3 == 0 ? now.AddHours(-index) : null,
                index % 5 == 0,
                index,
                tags,
                ThemeSourceType.LocalCreated,
                null,
                false,
                $"themes/{id:D}",
                null,
                new string('a', 64),
                false,
                ThemeApplyResult.NeverApplied,
                null);
            Summaries.Add(summary);
            Themes[id] = CreateTheme(id, name);
        }
    }

    public List<ThemeSummary> Summaries { get; } = [];

    public Dictionary<Guid, ThemePackage> Themes { get; } = [];

    public HashSet<Guid> DeletedThemeIds { get; } = [];

    public int PermanentDeleteCalls { get; private set; }

    public Task<OperationResult<IReadOnlyList<ThemeSummary>>> ListAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(
            OperationResult<IReadOnlyList<ThemeSummary>>.Success(
                Summaries
                    .Where(item =>
                        Themes.ContainsKey(item.ThemeId) &&
                        !DeletedThemeIds.Contains(item.ThemeId))
                    .ToArray()));

    public Task<OperationResult<IReadOnlyList<ThemeSummary>>> ListDeletedAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(
            OperationResult<IReadOnlyList<ThemeSummary>>.Success(
                Summaries
                    .Where(item =>
                        Themes.ContainsKey(item.ThemeId) &&
                        DeletedThemeIds.Contains(item.ThemeId))
                    .ToArray()));

    public Task<OperationResult<ThemePackage>> GetAsync(
        Guid themeId,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            Themes.TryGetValue(themeId, out var theme)
                ? OperationResult<ThemePackage>.Success(theme)
                : OperationResult<ThemePackage>.Failure(
                    OperationErrorCode.NotFound,
                    "主题不存在。"));

    public Task<OperationResult> SaveAsync(
        ThemePackage theme,
        ThemeCreateOptions? createOptions,
        CancellationToken cancellationToken)
    {
        Themes[theme.Id] = theme;
        Summaries.Add(ToSummary(theme, createOptions?.Tags ?? []));
        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult<ThemePackage>> CopyAsync(
        Guid sourceThemeId,
        Guid newThemeId,
        string newName,
        CancellationToken cancellationToken)
    {
        var copy = Themes[sourceThemeId] with { Id = newThemeId, Name = newName };
        Themes[newThemeId] = copy;
        Summaries.Add(ToSummary(copy, []));
        return Task.FromResult(OperationResult<ThemePackage>.Success(copy));
    }

    public Task<OperationResult> RenameAsync(
        Guid themeId,
        string newName,
        CancellationToken cancellationToken)
    {
        Themes[themeId] = Themes[themeId] with { Name = newName };
        var index = Summaries.FindIndex(item => item.ThemeId == themeId);
        Summaries[index] = Summaries[index] with { DisplayName = newName };
        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> SetFavoriteAsync(
        Guid themeId,
        bool isFavorite,
        CancellationToken cancellationToken)
    {
        var index = Summaries.FindIndex(item => item.ThemeId == themeId);
        Summaries[index] = Summaries[index] with { IsFavorite = isFavorite };
        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> SetSortOrderAsync(
        Guid themeId,
        int sortOrder,
        CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult.Success());

    public Task<OperationResult> SetTagsAsync(
        Guid themeId,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult.Success());

    public Task<OperationResult> SetCurrentPersistentAsync(
        Guid? themeId,
        CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult.Success());

    public Task<OperationResult> RecordApplyResultAsync(
        Guid themeId,
        ThemeApplyResult result,
        string? userMessage,
        CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult.Success());

    public Task<OperationResult> DeleteAsync(
        Guid themeId,
        CancellationToken cancellationToken)
    {
        DeletedThemeIds.Add(themeId);
        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> RestoreDeletedAsync(
        Guid themeId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(
            DeletedThemeIds.Remove(themeId)
                ? OperationResult.Success()
                : OperationResult.Failure(
                    OperationErrorCode.NotFound,
                    "回收站中不存在该主题。"));
    }

    public Task<OperationResult> PermanentlyDeleteAsync(
        Guid themeId,
        CancellationToken cancellationToken)
    {
        if (!DeletedThemeIds.Remove(themeId))
        {
            return Task.FromResult(
                OperationResult.Failure(
                    OperationErrorCode.NotFound,
                    "回收站中不存在该主题。"));
        }

        PermanentDeleteCalls++;
        Themes.Remove(themeId);
        Summaries.RemoveAll(item => item.ThemeId == themeId);
        return Task.FromResult(OperationResult.Success());
    }

    private static ThemePackage CreateTheme(Guid id, string name) =>
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
                0.8,
                0.2,
                ThemeTaskMode.Ambient,
                0.2,
                0.6,
                0));

    private static ThemeSummary ToSummary(
        ThemePackage theme,
        IReadOnlyList<string> tags) =>
        new(
            theme.Id,
            theme.Name,
            theme.SchemaVersion,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            false,
            0,
            tags,
            ThemeSourceType.LocalCreated,
            null,
            false,
            $"themes/{theme.Id:D}",
            null,
            new string('a', 64),
            false,
            ThemeApplyResult.NeverApplied,
            null);
}

internal sealed class FakeRuntimeService : ICodexThemeRuntime
{
    public CodexCachedCompatibilityStatus? CachedCompatibility { get; set; }

    public ThemeRuntimeStatus Status { get; set; } =
        new(
            ThemeRuntimeState.Ready,
            null,
            false,
            1234,
            DateTimeOffset.UtcNow,
            "Codex 已就绪。");

    public TaskCompletionSource ApplyEntered { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource? ApplyGate { get; set; }

    public TaskCompletionSource? StatusGate { get; set; }

    public int GetStatusCalls { get; private set; }

    public int RestoreCalls { get; private set; }

    public int ApplyCalls { get; private set; }

    public Queue<OperationResult<ThemeRuntimeStatus>> ApplyResults { get; } = new();

    public async Task<OperationResult<ThemeRuntimeStatus>> ApplyTemporaryAsync(
        ThemePackage theme,
        CancellationToken cancellationToken)
    {
        ApplyCalls++;
        ApplyEntered.TrySetResult();
        if (ApplyGate is not null)
        {
            await ApplyGate.Task.WaitAsync(cancellationToken);
        }

        if (ApplyResults.TryDequeue(out var queuedResult))
        {
            return queuedResult;
        }

        Status = Status with
        {
            State = ThemeRuntimeState.Temporary,
            ThemeId = theme.Id,
            UserMessage = "主题已临时应用。",
        };
        return OperationResult<ThemeRuntimeStatus>.Success(Status);
    }

    public Task<OperationResult<ThemeRuntimeStatus>> SwitchTemporaryAsync(
        ThemePackage theme,
        CancellationToken cancellationToken) =>
        ApplyTemporaryAsync(theme, cancellationToken);

    public Task<OperationResult<ThemeRuntimeStatus>> RestoreAsync(
        CancellationToken cancellationToken)
    {
        RestoreCalls++;
        Status = Status with
        {
            State = ThemeRuntimeState.Default,
            ThemeId = null,
            UserMessage = "Codex 外观已还原。",
        };
        return Task.FromResult(OperationResult<ThemeRuntimeStatus>.Success(Status));
    }

    public async Task<OperationResult<ThemeRuntimeStatus>> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        GetStatusCalls++;
        if (StatusGate is not null)
        {
            await StatusGate.Task.WaitAsync(cancellationToken);
        }

        return OperationResult<ThemeRuntimeStatus>.Success(Status);
    }

    public Task<OperationResult<ThemeRuntimeStatus>> GetStatusAsync(
        CodexStatusRefreshMode refreshMode,
        CancellationToken cancellationToken) =>
        GetStatusAsync(cancellationToken);

    public Task<OperationResult<CodexCachedCompatibilityStatus?>>
        GetCachedCompatibilityAsync(CancellationToken cancellationToken) =>
        Task.FromResult(
            OperationResult<CodexCachedCompatibilityStatus?>.SuccessOptional(
                CachedCompatibility));
}

internal sealed class FakeCodexDiscoveryService : ICodexDiscoveryService
{
    public const string ExecutablePath =
        @"C:\Program Files\WindowsApps\OpenAI.Codex\ChatGPT.exe";

    public IReadOnlyList<CodexProcessInfo> Processes { get; set; } = [];

    public int DiscoverCalls { get; private set; }

    public Task<OperationResult<CodexDiscoverySnapshot>> DiscoverAsync(
        CancellationToken cancellationToken)
    {
        DiscoverCalls++;
        return Task.FromResult(
            OperationResult<CodexDiscoverySnapshot>.Success(
                new CodexDiscoverySnapshot(
                    new CodexInstallationInfo(
                        "OpenAI.Codex_2p2nqsd0c76g0",
                        "OpenAI.Codex_26.721.3404.0_x64__2p2nqsd0c76g0",
                        "26.721.3404.0",
                        ExecutablePath,
                        ExecutableSha256: new string('a', 64)),
                    Processes,
                    DateTimeOffset.UtcNow)));
    }
}

internal sealed class FakePersistenceService : IPersistenceService
{
    public int EnableCalls { get; private set; }

    public int DisableCalls { get; private set; }

    public OperationResult<ThemeRuntimeStatus>? DisableResult { get; set; }

    public ThemeRuntimeStatus Status { get; set; } =
        new(
            ThemeRuntimeState.Default,
            null,
            false,
            null,
            DateTimeOffset.UtcNow,
            "持久化未启用。");

    public Task<OperationResult<ThemeRuntimeStatus>> EnableAsync(
        ThemePackage theme,
        CancellationToken cancellationToken) =>
        EnableAsync(theme, new PersistenceOptions(), cancellationToken);

    public Task<OperationResult<ThemeRuntimeStatus>> EnableAsync(
        ThemePackage theme,
        PersistenceOptions options,
        CancellationToken cancellationToken)
    {
        EnableCalls++;
        Status = Status with
        {
            State = ThemeRuntimeState.Persistent,
            ThemeId = theme.Id,
            IsPersistenceEnabled = true,
            UserMessage = "持久化已启用。",
        };
        return Task.FromResult(OperationResult<ThemeRuntimeStatus>.Success(Status));
    }

    public Task<OperationResult<ThemeRuntimeStatus>> SwitchAsync(
        ThemePackage theme,
        CancellationToken cancellationToken) =>
        EnableAsync(theme, cancellationToken);

    public Task<OperationResult<ThemeRuntimeStatus>> DisableAsync(
        CancellationToken cancellationToken)
    {
        DisableCalls++;
        if (DisableResult is not null)
        {
            return Task.FromResult(DisableResult);
        }

        Status = Status with
        {
            State = ThemeRuntimeState.Default,
            ThemeId = null,
            IsPersistenceEnabled = false,
            UserMessage = "持久化已停用。",
        };
        return Task.FromResult(OperationResult<ThemeRuntimeStatus>.Success(Status));
    }

    public Task<OperationResult<ThemeRuntimeStatus>> GetStatusAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<ThemeRuntimeStatus>.Success(Status));
}

internal sealed class FakeExternalPersistenceService(
    bool persistenceEnabled) : IExternalPersistenceService
{
    public int DisableCalls { get; private set; }

    public ExternalPersistenceStatus Status { get; private set; } =
        new(
            "OkkSkin",
            persistenceEnabled,
            persistenceEnabled,
            persistenceEnabled,
            true,
            [],
            persistenceEnabled
                ? "检测到 OkkSkin 持久化。"
                : "OkkSkin 持久化未启用。");

    public OperationResult<ExternalPersistenceDisableResult>? DisableResult { get; set; }

    public Task<OperationResult<ExternalPersistenceStatus>> GetStatusAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(
            OperationResult<ExternalPersistenceStatus>.Success(Status));

    public Task<OperationResult<ExternalPersistenceDisableResult>> DisableAsync(
        CancellationToken cancellationToken)
    {
        DisableCalls++;
        if (DisableResult is not null)
        {
            return Task.FromResult(DisableResult);
        }

        Status = Status with
        {
            IsConfigured = false,
            IsStartupRegistered = false,
            IsAgentRunning = false,
            Residuals = [],
            UserMessage = "OkkSkin 持久化已停用。",
        };
        return Task.FromResult(
            OperationResult<ExternalPersistenceDisableResult>.Success(
                new ExternalPersistenceDisableResult(
                    ExternalPersistenceDisableOutcome.Success,
                    Status,
                    Status.UserMessage)));
    }
}

internal sealed class FakeStorageLocationService : IStorageLocationService
{
    private static readonly StorageLocationStatus Status =
        new(
            @"C:\Test Data\Codex Theme Studio",
            false,
            false,
            null,
            "外置磁盘当前未连接；不会创建替代空库。");

    public StorageMigrationReport? LastMigrationReport => null;

    public Task<OperationResult<StorageLocationStatus>> InitializeAsync(
        string defaultDataRoot,
        CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<StorageLocationStatus>.Success(Status));

    public Task<OperationResult<StorageLocationStatus>> GetStatusAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<StorageLocationStatus>.Success(Status));

    public Task<OperationResult<StorageLocationStatus>> MigrateAsync(
        string destinationDataRoot,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed class FakeDialogs : IUserDialogService
{
    public bool ConfirmResult { get; set; } = true;

    public List<(string Title, string Message)> Confirmations { get; } = [];

    public int RequestTextCalls { get; private set; }

    public Task<string?> RequestTextAsync(
        string title,
        string prompt,
        string initialValue,
        CancellationToken cancellationToken)
    {
        RequestTextCalls++;
        return Task.FromResult<string?>(initialValue);
    }

    public Task<bool> ConfirmAsync(
        string title,
        string message,
        CancellationToken cancellationToken)
    {
        Confirmations.Add((title, message));
        return Task.FromResult(ConfirmResult);
    }

    public Task<string?> PickOpenFileAsync(
        string title,
        string filter,
        CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);

    public Task<string?> PickSaveFileAsync(
        string title,
        string filter,
        string defaultExtension,
        string suggestedFileName,
        CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);

    public Task<string?> PickFolderAsync(
        string title,
        string? initialDirectory,
        CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);

    public void ShowInformation(string title, string message)
    {
    }
}

internal sealed class FakeDiagnosticService :
    IDiagnosticEventSink,
    IDiagnosticQueryService,
    IDiagnosticBundleService
{
    private static readonly Guid ReportId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly DiagnosticSnapshot snapshot;

    public FakeDiagnosticService()
    {
        var item = new DiagnosticEvent(
            DiagnosticEvent.CurrentSchemaVersion,
            new DateTimeOffset(2026, 7, 24, 1, 2, 3, TimeSpan.Zero),
            DiagnosticSource.Agent,
            DiagnosticLevel.Information,
            "agent.state.persistent",
            "persistence.agent",
            DiagnosticOutcome.State,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            null,
            null,
            null,
            "0.2.0",
            "test-version",
            new string('a', 64),
            null);
        snapshot = new DiagnosticSnapshot(
            DiagnosticHealth.Normal,
            "诊断记录正常",
            item.TimestampUtc,
            null,
            null,
            [new DiagnosticEventGroup(
                item,
                3,
                item.TimestampUtc.AddMinutes(-30),
                item.TimestampUtc)],
            Path.GetTempPath(),
            true,
            0,
            null);
    }

    public int ReadCalls { get; private set; }

    public int DraftCalls { get; private set; }

    public List<DiagnosticEvent> WrittenEvents { get; } = [];

    public Task<OperationResult> WriteAsync(
        DiagnosticEvent diagnosticEvent,
        CancellationToken cancellationToken)
    {
        WrittenEvents.Add(diagnosticEvent);
        return Task.FromResult(OperationResult.Success());
    }

    public OperationResult WriteCritical(DiagnosticEvent diagnosticEvent)
    {
        WrittenEvents.Add(diagnosticEvent);
        return OperationResult.Success();
    }

    public Task<OperationResult<DiagnosticSnapshot>> ReadAsync(
        int maximumEventGroups,
        CancellationToken cancellationToken)
    {
        ReadCalls++;
        return Task.FromResult(
            OperationResult<DiagnosticSnapshot>.Success(snapshot));
    }

    public Task<OperationResult<DiagnosticIssueDraft>> CreateIssueDraftAsync(
        DiagnosticIssueContext context,
        CancellationToken cancellationToken)
    {
        DraftCalls++;
        return Task.FromResult(
            OperationResult<DiagnosticIssueDraft>.Success(
                new DiagnosticIssueDraft(
                    ReportId,
                    "safe issue summary",
                    snapshot)));
    }

    public Task<OperationResult<DiagnosticBundleResult>> ExportAsync(
        string destinationPath,
        DiagnosticIssueContext context,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            OperationResult<DiagnosticBundleResult>.Success(
                new DiagnosticBundleResult(
                    ReportId,
                    destinationPath,
                    4,
                    1024,
                    new string('b', 64))));
}

internal sealed class FakeImagePipeline : IImagePipeline
{
    public Task<OperationResult<ProcessedImageSet>> ProcessAsync(
        Stream source,
        string sourceFileName,
        Guid themeId,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            OperationResult<ProcessedImageSet>.Failure(
                OperationErrorCode.NotImplemented,
                "测试图片管线未启用。"));
}

internal sealed class FakeThemeAssetStore : IThemeAssetStore
{
    public Task<OperationResult<string>> SaveAsync(
        Guid themeId,
        string fileName,
        Stream content,
        CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<string>.Success($"themes/{themeId:D}/{fileName}"));

    public Task<OperationResult<Stream>> OpenReadAsync(
        string relativePath,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            OperationResult<Stream>.Success(new MemoryStream([1, 2, 3])));
}

internal sealed class FakeThemePackageService : IThemePackageService
{
    public Task<OperationResult<string>> ExportAsync(
        Guid themeId,
        string destinationFile,
        CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<string>.Success(destinationFile));

    public Task<OperationResult<ThemePackageImportResult>> ImportAsync(
        string packageFile,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            OperationResult<ThemePackageImportResult>.Failure(
                OperationErrorCode.NotImplemented,
                "测试导入未启用。"));
}

internal sealed class FakeUpdateService(
    OperationResult<UpdateCheckResult> checkResult) : IUpdateService
{
    private OperationResult<UpdateCheckResult> currentCheckResult = checkResult;

    public int CheckCalls { get; private set; }

    public bool LastForceRefresh { get; private set; }

    public List<bool> ForceRefreshArguments { get; } = [];

    public void SetCheckResult(OperationResult<UpdateCheckResult> result) =>
        currentCheckResult = result;

    public Task<OperationResult<UpdateCheckResult>> CheckAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        CheckCalls++;
        LastForceRefresh = forceRefresh;
        ForceRefreshArguments.Add(forceRefresh);
        return Task.FromResult(currentCheckResult);
    }

    public Task<OperationResult<StagedUpdate>> DownloadAndStageAsync(
        UpdateReleaseInfo release,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<StagedUpdate>.Failure(
            OperationErrorCode.NotImplemented,
            "测试下载未启用。"));

    internal static UpdateReleaseInfo CreateRelease(string version) =>
        new(
            version,
            $"v{version}",
            new Uri($"https://github.com/Aenvo/Codex-Theme-Studio/releases/tag/v{version}"),
            DateTimeOffset.UtcNow,
            "release notes",
            []);
}

internal sealed class FakeUpdateDialogs : IUpdateDialogService
{
    public int ShowCalls { get; private set; }

    public Task ShowReleaseAsync(
        string currentVersion,
        UpdateReleaseInfo release,
        Action openGitHub,
        Func<IProgress<UpdateDownloadProgress>, CancellationToken,
            Task<OperationResult<UpdateInstallResult>>> downloadAndInstall,
        CancellationToken cancellationToken)
    {
        ShowCalls++;
        return Task.CompletedTask;
    }
}
