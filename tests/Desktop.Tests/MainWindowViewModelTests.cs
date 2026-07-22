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
    private readonly ITestOutputHelper output;

    public MainWindowViewModelTests(ITestOutputHelper output)
    {
        this.output = output;
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
    public async Task Apply_DisablesConflictingCommandsUntilAsyncOperationCompletes()
    {
        using var fixture = new ViewModelFixture(themeCount: 3);
        await fixture.ViewModel.InitializeAsync();
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
    public void Editor_HomeTaskSidebarAndNarrowPreview_AreIndependent()
    {
        using var fixture = new ViewModelFixture(themeCount: 1);
        var theme = fixture.Repository.Themes[fixture.Repository.Summaries[0].ThemeId];
        fixture.Editor.Begin(theme, newTheme: false);
        var homeOpacity = fixture.Editor.PreviewOpacity;

        fixture.Editor.IsTaskPreview = true;
        fixture.Editor.IsSidebarVisible = false;
        fixture.Editor.IsNarrowPreview = true;

        Assert.Equal(theme.Art.TaskOpacity, fixture.Editor.PreviewOpacity);
        Assert.NotEqual(homeOpacity, fixture.Editor.PreviewOpacity);
        Assert.False(fixture.Editor.IsSidebarVisible);
        Assert.Equal(420, fixture.Editor.PreviewWidth);
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
    public ViewModelFixture(int themeCount)
    {
        Repository = new FakeThemeRepository(themeCount);
        Runtime = new FakeRuntimeService();
        Persistence = new FakePersistenceService();
        Editor = new ThemeEditorViewModel(
            Repository,
            new FakeImagePipeline(),
            new FakeThemeAssetStore(),
            _ => null);
        ViewModel = new MainWindowViewModel(
            Repository,
            Runtime,
            Persistence,
            new FakeStorageLocationService(),
            new FakeDialogs(),
            _ => null,
            new FakeThemePackageService(),
            Editor);
    }

    public FakeThemeRepository Repository { get; }

    public FakeRuntimeService Runtime { get; }

    public FakePersistenceService Persistence { get; }

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
                ThemeCompatibilityStatus.Unknown,
                ThemeApplyResult.NeverApplied,
                null);
            Summaries.Add(summary);
            Themes[id] = CreateTheme(id, name);
        }
    }

    public List<ThemeSummary> Summaries { get; } = [];

    public Dictionary<Guid, ThemePackage> Themes { get; } = [];

    public Task<OperationResult<IReadOnlyList<ThemeSummary>>> ListAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(
            OperationResult<IReadOnlyList<ThemeSummary>>.Success(
                Summaries.Where(item => Themes.ContainsKey(item.ThemeId)).ToArray()));

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
        Themes.Remove(themeId);
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
            ThemeCompatibilityStatus.Unknown,
            ThemeApplyResult.NeverApplied,
            null);
}

internal sealed class FakeRuntimeService : ICodexThemeRuntime
{
    public ThemeRuntimeStatus Status { get; set; } =
        new(
            ThemeRuntimeState.NotRunning,
            null,
            false,
            null,
            DateTimeOffset.UtcNow,
            "Codex 未运行。");

    public TaskCompletionSource ApplyEntered { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource? ApplyGate { get; set; }

    public async Task<OperationResult<ThemeRuntimeStatus>> ApplyTemporaryAsync(
        ThemePackage theme,
        CancellationToken cancellationToken)
    {
        ApplyEntered.TrySetResult();
        if (ApplyGate is not null)
        {
            await ApplyGate.Task.WaitAsync(cancellationToken);
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
        Status = Status with
        {
            State = ThemeRuntimeState.Default,
            ThemeId = null,
            UserMessage = "Codex 外观已还原。",
        };
        return Task.FromResult(OperationResult<ThemeRuntimeStatus>.Success(Status));
    }

    public Task<OperationResult<ThemeRuntimeStatus>> GetStatusAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<ThemeRuntimeStatus>.Success(Status));
}

internal sealed class FakePersistenceService : IPersistenceService
{
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
    public Task<string?> RequestTextAsync(
        string title,
        string prompt,
        string initialValue,
        CancellationToken cancellationToken) =>
        Task.FromResult<string?>(initialValue);

    public Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText,
        CancellationToken cancellationToken) =>
        Task.FromResult(true);

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
