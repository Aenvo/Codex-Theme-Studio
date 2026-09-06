using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Desktop.MacOS.Services;
using CodexThemeStudio.Desktop.MacOS.ViewModels;

namespace CodexThemeStudio.Desktop.MacOS.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task InitializeLoadsFixturesWithoutImplicitSelection()
    {
        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        Assert.Equal(MacLibraryState.Ready, viewModel.LibraryState);
        Assert.Equal(6, viewModel.VisibleThemes.Count);
        Assert.Null(viewModel.SelectedTheme);
        Assert.Equal(
            MacRuntimeState.ReadyUnqualified,
            viewModel.RuntimeState);
        Assert.False(viewModel.CanApply);
        Assert.False(viewModel.CanRestore);
    }

    [Fact]
    public async Task ExplicitSelectionEnablesApply()
    {
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        viewModel.SelectTheme(viewModel.VisibleThemes[0]);

        Assert.NotNull(viewModel.SelectedTheme);
        Assert.True(viewModel.CanApply);
    }

    [Fact]
    public async Task ExplicitSelectionOpensFixtureEditorWithoutMutatingTheme()
    {
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();
        Assert.False(viewModel.EditCommand.CanExecute(null));

        var selected = viewModel.VisibleThemes[0];
        var originalName = selected.DisplayName;
        viewModel.SelectTheme(selected);

        Assert.True(viewModel.EditCommand.CanExecute(null));
        viewModel.EditCommand.Execute(null);

        Assert.True(viewModel.IsEditorPage);
        Assert.False(viewModel.IsThemeLibraryPage);
        Assert.NotNull(viewModel.Editor);
        Assert.Equal(originalName, viewModel.Editor.Name);
        viewModel.Editor.Name = "仅存在于草稿";
        viewModel.EditorCancelConfirmationRequested +=
            (_, request) => request.Complete(true);

        await viewModel.CancelEditorCommand.ExecuteAsync();

        Assert.True(viewModel.IsWorkbenchPage);
        Assert.Null(viewModel.Editor);
        Assert.Equal(originalName, selected.DisplayName);
        Assert.Same(selected, viewModel.SelectedTheme);
    }

    [Fact]
    public async Task EditorExposesWindowsParameterSetAndLivePreviewModes()
    {
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();
        viewModel.SelectTheme(viewModel.VisibleThemes[0]);
        viewModel.EditCommand.Execute(null);
        var editor = Assert.IsType<ThemeEditorViewModel>(viewModel.Editor);

        editor.ArtSize = ThemeEditorViewModel.ArtCrop;
        editor.CropScale = 4;
        editor.FocusX = -1;
        editor.FocusY = 2;
        editor.HomeOpacity = 0.44;
        editor.HomeOverlay = 0.33;
        editor.TaskMode = ThemeEditorViewModel.TaskHidden;
        editor.TaskOpacity = 0.55;
        editor.TaskOverlay = 0.66;
        editor.Blur = 18;
        editor.PanelBlur = 22;

        Assert.True(editor.IsCropMode);
        Assert.Equal(3, editor.CropScale);
        Assert.Equal(0, editor.FocusX);
        Assert.Equal(1, editor.FocusY);
        Assert.False(editor.IsTaskOverlayEnabled);
        Assert.True(editor.IsDirty);
        Assert.Equal(6, editor.ThemePaletteColors.Count);
        Assert.Contains(editor.AccentColor, editor.ThemePaletteColors);
        Assert.Equal("今天想创建什么？", editor.PreviewTitle);
        Assert.Equal("描述你想完成的任务", editor.PreviewSubtitle);
        editor.RecordColor("#11223380");
        editor.RecordColor("#AABBCC");
        editor.RecordColor("#11223380");
        Assert.Equal(
            ["#11223380", "#AABBCC"],
            editor.ColorHistory);
        editor.ShowTaskPreviewCommand.Execute(null);
        Assert.True(editor.IsTaskPreview);
        Assert.Equal(0, editor.PreviewOpacity);
        Assert.Equal("示例任务", editor.PreviewTitle);
        Assert.Equal(
            "已完成的任务和变更会显示在这里。",
            editor.PreviewSubtitle);
        editor.ShowHomePreviewCommand.Execute(null);
        Assert.True(editor.IsHomePreview);
        Assert.Equal(0.44, editor.PreviewOpacity);
    }

    [Fact]
    public async Task EditorSaveAndSaveCopyUpdateFixtureLibrary()
    {
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();
        var selected = viewModel.VisibleThemes[0];
        viewModel.SelectTheme(selected);
        viewModel.EditCommand.Execute(null);
        viewModel.Editor!.Name = "已编辑主题";
        viewModel.Editor.AccentColor = "#FF3366";

        viewModel.SaveEditorCommand.Execute(null);

        Assert.True(viewModel.IsWorkbenchPage);
        Assert.Equal("已编辑主题", selected.DisplayName);
        Assert.Equal("#FF3366", selected.Palette.Accent);
        Assert.Same(selected, viewModel.SelectedTheme);

        viewModel.EditCommand.Execute(null);
        Assert.Equal("#FF3366", viewModel.Editor!.AccentColor);
        viewModel.Editor.ArtSize = ThemeEditorViewModel.ArtCrop;
        viewModel.Editor.CropScale = 1.8;
        viewModel.SaveEditorCommand.Execute(null);
        viewModel.EditCommand.Execute(null);
        Assert.Equal(ThemeEditorViewModel.ArtCrop, viewModel.Editor!.ArtSize);
        Assert.Equal(1.8, viewModel.Editor.CropScale);

        viewModel.EditorCopyNameRequested +=
            (_, request) => request.Complete("已编辑主题副本");
        await viewModel.SaveCopyEditorCommand.ExecuteAsync();

        Assert.Equal(7, viewModel.VisibleThemes.Count);
        Assert.Equal("已编辑主题副本", viewModel.SelectedTheme!.DisplayName);
        Assert.Equal("#FF3366", viewModel.SelectedTheme.Palette.Accent);
    }

    [Fact]
    public async Task CardsUseWindowsSubtitleAndFavoriteToggleSemantics()
    {
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();
        Assert.All(
            viewModel.VisibleThemes,
            theme => Assert.Equal("本地主题", theme.Subtitle));

        var theme = viewModel.VisibleThemes.First(item => !item.IsFavorite);
        viewModel.SelectTheme(theme);
        Assert.True(theme.ShowFavoriteButton);
        Assert.True(theme.IsNotFavorite);

        theme.ToggleFavoriteCommand.Execute(null);

        Assert.True(theme.IsFavorite);
        Assert.False(theme.IsNotFavorite);
        viewModel.ShowFavoritesPageCommand.Execute(null);
        Assert.Contains(theme, viewModel.VisibleThemes);

        theme.ToggleFavoriteCommand.Execute(null);

        Assert.False(theme.IsFavorite);
        Assert.DoesNotContain(theme, viewModel.VisibleThemes);
        Assert.Null(viewModel.SelectedTheme);
    }

    [Fact]
    public async Task SearchKeepsExplicitSelectionWhenStillVisible()
    {
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();
        var selected = viewModel.VisibleThemes[0];
        viewModel.SelectTheme(selected);

        viewModel.SearchText = selected.DisplayName;

        Assert.Single(viewModel.VisibleThemes);
        Assert.Same(selected, viewModel.SelectedTheme);
    }

    [Fact]
    public async Task SearchClearsSelectionWhenSelectedThemeIsHidden()
    {
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();
        viewModel.SelectTheme(viewModel.VisibleThemes[0]);

        viewModel.SearchText = "不存在的主题";

        Assert.Empty(viewModel.VisibleThemes);
        Assert.Null(viewModel.SelectedTheme);
        Assert.False(viewModel.CanApply);
        Assert.True(viewModel.IsFilterEmpty);
    }

    [Fact]
    public async Task ScopeTagAndSortRemainIndependent()
    {
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        viewModel.SelectedScope = MainWindowViewModel.ScopeFavorites;
        Assert.All(viewModel.VisibleThemes, theme => Assert.True(theme.IsFavorite));

        viewModel.SelectedScope = MainWindowViewModel.ScopeAll;
        viewModel.SelectedTag = "暖色";
        Assert.Single(viewModel.VisibleThemes);
        Assert.Contains("暖色", viewModel.VisibleThemes[0].TagItems);

        viewModel.SelectedTag = MainWindowViewModel.TagAll;
        viewModel.SelectedSort = MainWindowViewModel.SortName;
        Assert.Equal(
            viewModel.VisibleThemes.OrderBy(
                theme => theme.DisplayName,
                StringComparer.CurrentCulture),
            viewModel.VisibleThemes);
    }

    [Fact]
    public async Task SidebarNavigationMatchesWorkbenchFavoritesAndTrash()
    {
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsWorkbenchPage);
        Assert.Equal("主题资料库", viewModel.PageTitle);
        Assert.Equal(MainWindowViewModel.ScopeAll, viewModel.SelectedScope);

        viewModel.ShowFavoritesPageCommand.Execute(null);

        Assert.True(viewModel.IsFavoritesPage);
        Assert.Equal("我的收藏", viewModel.PageTitle);
        Assert.Equal(
            MainWindowViewModel.ScopeFavorites,
            viewModel.SelectedScope);
        Assert.All(
            viewModel.VisibleThemes,
            theme => Assert.True(theme.IsFavorite));

        viewModel.ShowTrashCommand.Execute(null);

        Assert.True(viewModel.IsTrashPage);
        Assert.False(viewModel.IsThemeLibraryPage);
        Assert.Equal("回收站", viewModel.PageTitle);
        Assert.Equal("0 个已删除主题", viewModel.PageSubtitle);

        viewModel.ShowWorkbenchCommand.Execute(null);

        Assert.True(viewModel.IsWorkbenchPage);
        Assert.Equal(MainWindowViewModel.ScopeAll, viewModel.SelectedScope);
    }

    [Fact]
    public void SidebarSettingsRaisesNativeWindowRequest()
    {
        var viewModel = CreateViewModel();
        var requestCount = 0;
        viewModel.SettingsRequested += (_, _) => requestCount++;

        viewModel.ShowSettingsCommand.Execute(null);

        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task SidebarSelectionCardUsesExplicitSelectionOnly()
    {
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        Assert.Equal("未选择主题", viewModel.SelectedThemeName);

        var selected = viewModel.VisibleThemes[0];
        viewModel.SelectTheme(selected);

        Assert.Equal(selected.DisplayName, viewModel.SelectedThemeName);
    }

    [Fact]
    public async Task CardRowsRespondToOneTwoAndThreeColumns()
    {
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        viewModel.SetCardColumnCount(1);
        Assert.Equal(6, viewModel.VisibleThemeRows.Count);

        viewModel.SetCardColumnCount(2);
        Assert.Equal(3, viewModel.VisibleThemeRows.Count);
        Assert.All(
            viewModel.VisibleThemeRows,
            row => Assert.Equal(2, row.Items.Count));

        viewModel.SetCardColumnCount(3);
        Assert.Equal(2, viewModel.VisibleThemeRows.Count);
        Assert.All(
            viewModel.VisibleThemeRows,
            row => Assert.Equal(3, row.Items.Count));
    }

    [Fact]
    public async Task CardAndListCommandsPreserveFiltersAndSelection()
    {
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();
        var selected = viewModel.VisibleThemes[0];
        viewModel.SelectTheme(selected);
        viewModel.SelectedSort = MainWindowViewModel.SortName;

        viewModel.ShowListCommand.Execute(null);
        Assert.True(viewModel.IsListView);
        Assert.Same(selected, viewModel.SelectedTheme);
        Assert.Equal(MainWindowViewModel.SortName, viewModel.SelectedSort);

        viewModel.ShowCardsCommand.Execute(null);
        Assert.True(viewModel.IsCardView);
        Assert.Same(selected, viewModel.SelectedTheme);
    }

    [Fact]
    public async Task ApplyThenRestoreUsesNonCancelableCompletion()
    {
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();
        viewModel.SelectTheme(viewModel.VisibleThemes[0]);
        Assert.Equal(
            "已检测到 ChatGPT (Codex)",
            viewModel.ActionStatusText);
        Assert.Equal(
            "未知版本 · 能力探测兼容",
            viewModel.ActionStatusDetail);
        Assert.True(viewModel.IsActionStatusSuccess);
        Assert.False(viewModel.IsActionStatusDanger);

        await viewModel.ApplyCommand.ExecuteAsync();

        Assert.Equal(
            MacOperationStage.TemporaryApplied,
            viewModel.OperationStage);
        Assert.Equal(MacProofState.Verified, viewModel.ProofState);
        Assert.Equal("临时主题已应用", viewModel.ActionStatusText);
        Assert.Equal(
            "未知版本 · 能力探测兼容",
            viewModel.ActionStatusDetail);
        Assert.True(viewModel.CanRestore);
        Assert.False(viewModel.CanApply);

        await viewModel.RestoreCommand.ExecuteAsync();

        Assert.Equal(MacOperationStage.Idle, viewModel.OperationStage);
        Assert.Equal(MacProofState.Verified, viewModel.ProofState);
        Assert.Equal(
            "已检测到 ChatGPT (Codex)",
            viewModel.ActionStatusText);
        Assert.Equal(
            "未知版本 · 能力探测兼容",
            viewModel.ActionStatusDetail);
        Assert.False(viewModel.CanRestore);
        Assert.True(viewModel.CanApply);
    }

    [Fact]
    public async Task ApplyFailureSeparatesPrimaryAndRecoveryGuidance()
    {
        var client = new FakeMacThemeStudioClient(
            operationDelay: TimeSpan.Zero);
        client.SetNextApplyOutcome(new MacThemeOperationOutcome(
            false,
            MacOperationStage.Failed,
            MacProofState.Unverified,
            "renderer.apply_failed",
            "inspector.close_failed"));
        var viewModel = new MainWindowViewModel(client);
        await viewModel.InitializeAsync();
        viewModel.SelectTheme(viewModel.VisibleThemes[0]);

        await viewModel.ApplyCommand.ExecuteAsync();

        Assert.Equal(MacOperationStage.Failed, viewModel.OperationStage);
        Assert.Equal("renderer.apply_failed", viewModel.PrimaryError);
        Assert.Equal("inspector.close_failed", viewModel.RecoveryError);
        Assert.Equal("操作未完成", viewModel.PrimaryErrorTitle);
        Assert.Equal("无法确认 Inspector 已关闭", viewModel.RecoveryErrorTitle);
        Assert.Equal(
            "无法确认 Inspector 已关闭",
            viewModel.ActionStatusText);
        Assert.Contains(
            "主操作：操作未完成",
            viewModel.ActionStatusDetail,
            StringComparison.Ordinal);
        Assert.Contains(
            "恢复：无法确认 Inspector 已关闭",
            viewModel.ActionStatusDetail,
            StringComparison.Ordinal);
        Assert.True(viewModel.IsActionStatusDanger);
        Assert.True(viewModel.RequiresManualCodexRestart);
        Assert.False(viewModel.CanApply);
        Assert.False(viewModel.CanRestore);
    }

    [Fact]
    public async Task PendingCleanupUsesSafeCleanupState()
    {
        var client = new FakeMacThemeStudioClient(
            operationDelay: TimeSpan.Zero);
        client.SetNextApplyOutcome(new MacThemeOperationOutcome(
            false,
            MacOperationStage.Failed,
            MacProofState.Pending,
            "renderer.apply_failed",
            null));
        var viewModel = new MainWindowViewModel(client);
        await viewModel.InitializeAsync();
        viewModel.SelectTheme(viewModel.VisibleThemes[0]);

        await viewModel.ApplyCommand.ExecuteAsync();

        Assert.True(viewModel.IsSafeCleanup);
        Assert.Equal("正在安全收尾", viewModel.ActionStatusText);
        Assert.True(viewModel.IsActionStatusWarning);
        Assert.False(viewModel.IsActionStatusDanger);
        Assert.Contains("不可中断", viewModel.OperationDetail);
    }

    [Fact]
    public async Task DeferredCloseKeepsOperationVisibleUntilCompletion()
    {
        var client = new FakeMacThemeStudioClient(
            operationDelay: TimeSpan.FromMilliseconds(50));
        var viewModel = new MainWindowViewModel(client);
        await viewModel.InitializeAsync();
        viewModel.SelectTheme(viewModel.VisibleThemes[0]);

        var operation = viewModel.ApplyCommand.ExecuteAsync();
        viewModel.NotifyCloseDeferred();

        Assert.True(viewModel.CloseDeferred);
        Assert.Contains("不会被中断", viewModel.StatusMessage);

        await operation;
        Assert.True(viewModel.IsSafeToClose);
    }

    [Fact]
    public async Task EmptyLibraryProducesDedicatedState()
    {
        var client = new FakeMacThemeStudioClient(
            themes: [],
            operationDelay: TimeSpan.Zero);
        var viewModel = new MainWindowViewModel(client);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsLibraryEmpty);
        Assert.False(viewModel.IsFilterEmpty);
        Assert.Null(viewModel.SelectedTheme);
        Assert.False(viewModel.CanApply);
    }

    [Fact]
    public async Task OneThemeStillRequiresExplicitSelection()
    {
        var client = new FakeMacThemeStudioClient(
            [CreateTheme(1)],
            operationDelay: TimeSpan.Zero);
        var viewModel = new MainWindowViewModel(client);

        await viewModel.InitializeAsync();

        Assert.Single(viewModel.VisibleThemes);
        Assert.Null(viewModel.SelectedTheme);
        Assert.False(viewModel.CanApply);
    }

    [Fact]
    public async Task OneHundredThemesRemainFilterableAndRowVirtualizable()
    {
        var fixtures = Enumerable
            .Range(0, 100)
            .Select(index => CreateTheme(index))
            .ToArray();
        var client = new FakeMacThemeStudioClient(
            fixtures,
            operationDelay: TimeSpan.Zero);
        var viewModel = new MainWindowViewModel(client);
        await viewModel.InitializeAsync();
        viewModel.SetCardColumnCount(2);

        Assert.Equal(100, viewModel.VisibleThemes.Count);
        Assert.Equal(50, viewModel.VisibleThemeRows.Count);

        viewModel.SearchText = "主题 042";

        Assert.Single(viewModel.VisibleThemes);
        Assert.Single(viewModel.VisibleThemeRows);
        Assert.Null(viewModel.SelectedTheme);
    }

    [Fact]
    public async Task LibraryFailureProducesFriendlyErrorWithoutPrivateDetails()
    {
        var viewModel = new MainWindowViewModel(
            new LoadFailureThemeStudioClient());

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsLibraryError);
        Assert.Equal("library.load_failed", viewModel.PrimaryError);
        Assert.Equal("无法载入主题资料库", viewModel.PrimaryErrorTitle);
        Assert.DoesNotContain(
            "fixture failure",
            viewModel.PrimaryErrorMessage,
            StringComparison.Ordinal);
        Assert.Empty(viewModel.VisibleThemes);
        Assert.False(viewModel.CanApply);
    }

    [Fact]
    public async Task IdentityChangeDisablesApplyAfterSelection()
    {
        var client = new FakeMacThemeStudioClient(
            operationDelay: TimeSpan.Zero);
        var viewModel = new MainWindowViewModel(client);
        await viewModel.InitializeAsync();
        viewModel.SelectTheme(viewModel.VisibleThemes[0]);
        client.SetRuntime(new MacRuntimeSnapshot(
            MacRuntimeState.IdentityChanged,
            false,
            "Codex 身份已变化。"));

        await viewModel.RefreshCommand.ExecuteAsync();

        Assert.Equal(
            MacRuntimeState.IdentityChanged,
            viewModel.RuntimeState);
        Assert.False(viewModel.CanApply);
    }

    [Fact]
    public async Task ActiveOperationDisablesConcurrentActionsUntilCompletion()
    {
        var client = new FakeMacThemeStudioClient(
            operationDelay: TimeSpan.FromMilliseconds(50));
        var viewModel = new MainWindowViewModel(client);
        await viewModel.InitializeAsync();
        viewModel.SelectTheme(viewModel.VisibleThemes[0]);

        var apply = viewModel.ApplyCommand.ExecuteAsync();

        Assert.True(viewModel.IsOperationActive);
        Assert.False(viewModel.ApplyCommand.CanExecute(null));
        Assert.False(viewModel.RestoreCommand.CanExecute(null));

        await apply;

        Assert.False(viewModel.IsOperationActive);
        Assert.True(viewModel.CanRestore);
    }

    [Fact]
    public void SettingsSectionsAreMutuallyExclusive()
    {
        var viewModel = new SettingsWindowViewModel();
        Assert.True(viewModel.IsGeneralSelected);

        viewModel.ShowDiagnosticsCommand.Execute(null);

        Assert.True(viewModel.IsDiagnosticsSelected);
        Assert.False(viewModel.IsGeneralSelected);
        Assert.False(viewModel.ShowDiagnosticsCommand.CanExecute(null));
    }

    [Fact]
    public void HandleRejectsEmptyOperationIdentity()
    {
        var completion = Task.FromResult(new MacThemeOperationOutcome(
            true,
            MacOperationStage.Idle,
            MacProofState.Verified,
            null,
            null));

        Assert.Throws<ArgumentException>(() =>
            new MacThemeOperationHandle(Guid.Empty, false, completion));
    }

    private static MainWindowViewModel CreateViewModel() =>
        new(new FakeMacThemeStudioClient(
            operationDelay: TimeSpan.Zero));

    private static MacThemeLibraryItem CreateTheme(int index) =>
        new(
            Guid.NewGuid(),
            $"主题 {index:000}",
            "fixture",
            ["本地"],
            new ThemePalette(
                "#111111",
                "#181818",
                "#2563EB",
                "#F5F5F5",
                "#A3A3A3",
                "#303030"),
            index % 3 == 0,
            DateTimeOffset.UtcNow.AddDays(-index),
            index % 2 == 0);

    private sealed class LoadFailureThemeStudioClient : IMacThemeStudioClient
    {
        public Task<MacThemeLibrarySnapshot> LoadLibraryAsync(
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("fixture failure");

        public Task<MacRuntimeSnapshot> RefreshRuntimeAsync(
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("fixture failure");

        public ValueTask<MacThemeOperationHandle> BeginApplyTemporaryAsync(
            Guid themeId,
            CancellationToken admissionCancellationToken) =>
            throw new InvalidOperationException("fixture failure");

        public ValueTask<MacThemeOperationHandle> BeginRestoreAsync(
            CancellationToken admissionCancellationToken) =>
            throw new InvalidOperationException("fixture failure");
    }
}
