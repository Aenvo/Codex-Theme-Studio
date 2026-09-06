using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Desktop.MacOS.Services;
using CodexThemeStudio.Desktop.MacOS.ViewModels;
using CodexThemeStudio.Desktop.MacOS.Views;

namespace CodexThemeStudio.Desktop.MacOS.Tests.Headless;

public sealed class MainWindowHeadlessTests
{
    [Fact]
    public void InvalidLocalPreviewImageFailsWithoutEscapingTheUiBoundary()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"cts-invalid-preview-{Guid.NewGuid():N}.png");
        File.WriteAllText(path, "not an image");

        try
        {
            var converter = new AssetUriImageConverter();

            var converted = converter.Convert(
                new Uri(path),
                typeof(Avalonia.Media.IImage),
                null,
                System.Globalization.CultureInfo.InvariantCulture);

            Assert.Null(converted);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public Task EditorColorPickerRendersAndPreservesCssAlphaOrder() =>
        AvaloniaTestHost.DispatchAsync(async () =>
        {
            var window = new EditorColorWindow(
                "#11223380",
                ["#111111", "#2563EB"],
                ["#F5F5F5", "#11223380"]);
            window.Show();

            Capture(window, "25-editor-color-picker.png");

            var hexInput = window.FindControl<TextBox>("HexInput");
            Assert.NotNull(hexInput);
            Assert.Equal("#112233", hexInput.Text);
            Assert.Equal("50", window.FindControl<TextBox>(
                "HexOpacityInput")!.Text);
            var format = window.FindControl<ComboBox>("FormatSelector")!;
            format.SelectedIndex = 1;
            Assert.True(window.FindControl<Grid>("RgbInputs")!.IsVisible);
            format.SelectedIndex = 2;
            var cssInput = window.FindControl<TextBox>("CssInput")!;
            Assert.True(cssInput.IsVisible);
            cssInput.Text = "rgba(17, 34, 51, 0.25)";
            await Task.Delay(20);
            format.SelectedIndex = 0;
            Assert.Equal("#112233", hexInput.Text);
            Assert.Equal(
                "25",
                window.FindControl<TextBox>("HexOpacityInput")!.Text);
            Assert.Contains(
                window.GetVisualDescendants().OfType<Button>(),
                button => string.Equals(
                    button.Tag as string,
                    "#2563EB",
                    StringComparison.Ordinal));
            AssertKeyControlInside(window, "HexInput");
            AssertKeyControlInside(window, "SaturationValueSurface");
            AssertKeyControlInside(window, "HueSlider");
            AssertKeyControlInside(window, "SaturationSlider");
            AssertKeyControlInside(window, "AlphaSlider");
            AssertKeyControlInside(window, "ApplyColorButton");
            window.Close();
        });

    [Fact]
    public Task FixtureEditorOpensAndRendersAtSupportedWindowSizes() =>
        AvaloniaTestHost.DispatchAsync(async () =>
        {
            var viewModel = await CreateInitializedViewModelAsync();
            var window = CreateWindow(viewModel, 1240, 780);
            window.Show();
            viewModel.SelectTheme(viewModel.VisibleThemes[0]);

            viewModel.EditCommand.Execute(null);
            Capture(window, "22-editor-fixture-1240x780.png");

            Assert.True(viewModel.IsEditorPage);
            AssertKeyControlInside(window, "ThemeEditorPage");
            Assert.NotNull(window.FindControl<Button>("EditorImageButton"));
            Assert.NotNull(window.FindControl<EditorColorField>(
                "EditorBackgroundColorField"));
            Assert.NotNull(window.FindControl<EditorColorField>(
                "EditorPanelColorField"));
            Assert.NotNull(window.FindControl<EditorColorField>(
                "EditorAccentColorField"));
            Assert.NotNull(window.FindControl<EditorColorField>(
                "EditorTextColorField"));
            Assert.NotNull(window.FindControl<EditorColorField>(
                "EditorMutedColorField"));
            Assert.NotNull(window.FindControl<EditorColorField>(
                "EditorBorderColorField"));
            Assert.Equal(
                viewModel.SelectedTheme!.DisplayName,
                viewModel.Editor!.Name);

            var artSizeComboBox =
                window.FindControl<ComboBox>("EditorArtSizeComboBox")!;
            artSizeComboBox.SelectedItem = ThemeEditorViewModel.ArtContain;
            viewModel.Editor.ShowTaskPreviewCommand.Execute(null);
            await Task.Delay(20);
            Capture(window, "24-editor-task-1240x780.png");
            Assert.True(viewModel.Editor.IsTaskPreview);
            Assert.False(viewModel.Editor.IsCropMode);
            Assert.Equal(
                ThemeEditorViewModel.ArtContain,
                artSizeComboBox.SelectedItem);
            Assert.Equal(
                ThemeEditorViewModel.ArtContain,
                viewModel.Editor.ArtSize);

            viewModel.Editor.ShowHomePreviewCommand.Execute(null);

            window.Width = 960;
            window.Height = 620;
            Capture(window, "23-editor-fixture-960x620.png");
            AssertKeyControlInside(window, "ThemeEditorPage");

            viewModel.EditorCancelConfirmationRequested +=
                (_, request) => request.Complete(true);
            await viewModel.CancelEditorCommand.ExecuteAsync();
            Assert.True(viewModel.IsWorkbenchPage);
            Assert.Null(viewModel.Editor);
            window.Close();
        });

    [Fact]
    public Task WideTaskPreviewRestoresWindowsInformationHierarchy() =>
        AvaloniaTestHost.DispatchAsync(async () =>
        {
            var viewModel = await CreateInitializedViewModelAsync();
            var window = CreateWindow(viewModel, 1800, 980);
            window.Show();
            viewModel.SelectTheme(viewModel.VisibleThemes[0]);
            viewModel.EditCommand.Execute(null);
            viewModel.Editor!.ShowTaskPreviewCommand.Execute(null);

            Capture(window, "26-editor-task-wide-1800x980.png");

            var environment = window.FindControl<Border>(
                "EditorTaskEnvironmentPanel");
            Assert.NotNull(environment);
            Assert.True(environment.IsVisible);
            Assert.Contains(
                window.GetVisualDescendants().OfType<TextBlock>(),
                text => string.Equals(
                    text.Text,
                    "已编辑 2 个文件",
                    StringComparison.Ordinal));
            Assert.Contains(
                window.GetVisualDescendants().OfType<TextBlock>(),
                text => string.Equals(
                    text.Text,
                    "仅模拟，不连接",
                    StringComparison.Ordinal));
            window.Close();
        });

    [Fact]
    public Task MinimumAndCommonLayoutsRenderWithoutClippingKeyControls() =>
        AvaloniaTestHost.DispatchAsync(async () =>
        {
            var viewModel = await CreateInitializedViewModelAsync();
            var window = CreateWindow(viewModel, 960, 620);
            window.Show();

            Capture(window, "07-cards-960x620.png");

            Assert.Equal(2, viewModel.CardColumnCount);
            AssertKeyControlInside(window, "SearchBox");
            AssertKeyControlInside(window, "ScopeFilter");
            AssertKeyControlInside(window, "TagFilter");
            AssertKeyControlInside(window, "SortFilter");
            AssertKeyControlInside(window, "RightActionPanel");
            AssertKeyControlInside(window, "SidebarWorkbenchButton");
            AssertKeyControlInside(window, "SidebarFavoritesButton");
            AssertKeyControlInside(window, "SidebarTrashButton");
            AssertKeyControlInside(window, "SidebarSettingsButton");
            AssertKeyControlInside(window, "SelectedThemeSidebarCard");
            Assert.InRange(
                window.FindControl<Button>("SidebarWorkbenchButton")!.Bounds.Width,
                206.5,
                208.5);
            Assert.InRange(
                window.FindControl<Button>("SidebarSettingsButton")!.Bounds.Width,
                206.5,
                208.5);
            AssertHorizontalGap(window, "SearchBox", "ScopeFilter", 12);
            AssertHorizontalGap(window, "ScopeFilter", "TagFilter", 12);
            AssertHorizontalGap(window, "TagFilter", "SortFilter", 12);
            Assert.InRange(
                window.FindControl<ComboBox>("ScopeFilter")!.Bounds.Width,
                149.5,
                150.5);
            Assert.InRange(
                window.FindControl<ComboBox>("TagFilter")!.Bounds.Width,
                149.5,
                150.5);
            Assert.InRange(
                window.FindControl<ComboBox>("SortFilter")!.Bounds.Width,
                149.5,
                150.5);

            window.Width = 1240;
            window.Height = 780;
            Capture(window, "01-library-default-1240x780.png");

            Assert.Equal(3, viewModel.CardColumnCount);
            Assert.True(window.FindControl<ListBox>("CardThemeList")!.IsVisible);
            Assert.False(
                window.FindControl<ListBox>("CompactThemeList")!.IsVisible);
            Assert.Null(viewModel.SelectedTheme);
            Assert.False(viewModel.CanApply);
            window.Close();
        });

    [Fact]
    public Task WindowsAlignedSidebarAndTrashStateRenderAtBothSizes() =>
        AvaloniaTestHost.DispatchAsync(async () =>
        {
            var viewModel = await CreateInitializedViewModelAsync();
            var window = CreateWindow(viewModel, 1240, 780);
            window.Show();

            Capture(window, "17-sidebar-library-1240x780.png");
            Assert.True(viewModel.IsWorkbenchPage);
            Assert.Equal("未选择主题", viewModel.SelectedThemeName);

            viewModel.ShowFavoritesPageCommand.Execute(null);
            Capture(window, "18-sidebar-favorites-1240x780.png");
            Assert.True(viewModel.IsFavoritesPage);
            Assert.All(
                viewModel.VisibleThemes,
                theme => Assert.True(theme.IsFavorite));

            viewModel.ShowTrashCommand.Execute(null);
            Capture(window, "19-sidebar-trash-1240x780.png");
            Assert.True(viewModel.IsTrashPage);
            Assert.False(viewModel.IsThemeLibraryPage);

            window.Width = 960;
            window.Height = 620;
            Capture(window, "20-sidebar-trash-960x620.png");
            AssertKeyControlInside(window, "SidebarTrashButton");
            AssertKeyControlInside(window, "SidebarSettingsButton");

            window.Close();
        });

    [Fact]
    public Task LibrarySelectionFilteringAndLayoutStatesRender() =>
        AvaloniaTestHost.DispatchAsync(async () =>
        {
            var viewModel = await CreateInitializedViewModelAsync();
            var window = CreateWindow(viewModel, 1240, 780);
            window.Show();

            viewModel.SelectTheme(viewModel.VisibleThemes[0]);
            Capture(window, "02-library-selected-1240x780.png");
            Capture(window, "06-cards-1240x780.png");
            Capture(window, "10-background-capability.png");
            Assert.True(viewModel.CanApply);
            var actionPanel = window.FindControl<Border>("RightActionPanel");
            Assert.NotNull(actionPanel);
            Assert.InRange(actionPanel.Bounds.Height, 70, 82);
            var actionButtons = actionPanel
                .GetVisualDescendants()
                .OfType<Button>()
                .ToArray();
            var applyButton = Assert.Single(
                actionButtons,
                button => string.Equals(
                    button.Content as string,
                    "临时应用",
                    StringComparison.Ordinal));
            var persistentButton = Assert.Single(
                actionButtons,
                button => string.Equals(
                    button.Content as string,
                    "设为持久主题",
                    StringComparison.Ordinal));
            var restoreButton = Assert.Single(
                actionButtons,
                button => string.Equals(
                    button.Content as string,
                    "还原外观",
                    StringComparison.Ordinal));
            Assert.True(applyButton.IsEnabled);
            Assert.False(persistentButton.IsEnabled);
            Assert.False(restoreButton.IsEnabled);
            Assert.Contains(
                actionPanel
                    .GetVisualDescendants()
                    .OfType<TextBlock>(),
                textBlock => string.Equals(
                    textBlock.Text,
                    "已检测到 ChatGPT (Codex)",
                    StringComparison.Ordinal));
            Assert.Contains(
                actionPanel
                    .GetVisualDescendants()
                    .OfType<TextBlock>(),
                textBlock => string.Equals(
                    textBlock.Text,
                    "未知版本 · 能力探测兼容",
                    StringComparison.Ordinal));
            Assert.DoesNotContain(
                actionPanel
                    .GetVisualDescendants()
                    .OfType<TextBlock>(),
                textBlock =>
                    textBlock.Text?.Contains(
                        "背景仅在 Studio 内预览",
                        StringComparison.Ordinal) == true ||
                    textBlock.Text?.Contains(
                        "首次应用需资格验证",
                        StringComparison.Ordinal) == true);
            Assert.DoesNotContain(
                window.GetVisualDescendants().OfType<TextBlock>(),
                textBlock => string.Equals(
                    textBlock.Text,
                    "背景仅预览",
                    StringComparison.Ordinal));
            Assert.Contains(
                window.GetVisualDescendants().OfType<ToggleButton>(),
                button => button.Classes.Contains("theme-favorite"));
            Assert.Contains(
                window.GetVisualDescendants().OfType<TextBlock>(),
                textBlock => string.Equals(
                    textBlock.Text,
                    "本地主题",
                    StringComparison.Ordinal));

            var selectedCard = window
                .GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(button =>
                    button.Classes.Contains("card") &&
                    button.Classes.Contains("selected"));
            Assert.NotNull(selectedCard);
            selectedCard.Focus();
            Capture(window, "09-card-selected-hover-focus.png");

            viewModel.SearchText = "不存在的主题";
            window.Width = 960;
            window.Height = 620;
            Capture(window, "03-library-filter-empty-960x620.png");
            Assert.True(viewModel.IsFilterEmpty);
            Assert.Null(viewModel.SelectedTheme);

            viewModel.SearchText = string.Empty;
            window.Width = 1240;
            window.Height = 780;
            viewModel.ShowListCommand.Execute(null);
            window.FindControl<TextBox>("SearchBox")!.Focus();
            Capture(window, "05-layout-switch-focus-1240x780.png");
            Capture(window, "08-list-1240x780.png");
            Assert.False(
                window.FindControl<ListBox>("CardThemeList")!.IsVisible);
            Assert.True(
                window.FindControl<ListBox>("CompactThemeList")!.IsVisible);
            window.Close();
        });

    [Fact]
    public Task LoadingEmptyAndErrorStatesRemainDistinct() =>
        AvaloniaTestHost.DispatchAsync(async () =>
        {
            var loadingViewModel = new MainWindowViewModel(
                new FakeMacThemeStudioClient(operationDelay: TimeSpan.Zero));
            var loading = CreateWindow(loadingViewModel, 960, 620);
            loading.Show();
            Capture(loading, "04a-library-loading-960x620.png");
            Assert.True(loadingViewModel.IsLibraryLoading);
            loading.Close();

            var emptyViewModel = new MainWindowViewModel(
                new FakeMacThemeStudioClient(
                    themes: [],
                    operationDelay: TimeSpan.Zero));
            await emptyViewModel.InitializeAsync();
            var empty = CreateWindow(emptyViewModel, 960, 620);
            empty.Show();
            Capture(empty, "04b-library-empty-960x620.png");
            Assert.True(emptyViewModel.IsLibraryEmpty);
            empty.Close();

            var errorViewModel = new MainWindowViewModel(
                new LoadFailureThemeStudioClient());
            await errorViewModel.InitializeAsync();
            var error = CreateWindow(errorViewModel, 960, 620);
            error.Show();
            Capture(error, "04c-library-error-960x620.png");
            Assert.True(errorViewModel.IsLibraryError);
            error.Close();
        });

    [Fact]
    public Task OperationAndRecoveryStatesRenderAsProductGuidance() =>
        AvaloniaTestHost.DispatchAsync(async () =>
        {
            var busyClient = new FakeMacThemeStudioClient(
                operationDelay: TimeSpan.FromMilliseconds(120));
            var busyViewModel = new MainWindowViewModel(busyClient);
            await busyViewModel.InitializeAsync();
            busyViewModel.SelectTheme(busyViewModel.VisibleThemes[0]);
            var busy = CreateWindow(busyViewModel, 1240, 780);
            busy.Show();
            var apply = busyViewModel.ApplyCommand.ExecuteAsync();
            Capture(busy, "11-apply-busy-1240x780.png");
            Assert.True(busyViewModel.IsOperationActive);
            await apply;
            busy.Close();

            var cleanupClient = new FakeMacThemeStudioClient(
                operationDelay: TimeSpan.Zero);
            cleanupClient.SetNextApplyOutcome(new MacThemeOperationOutcome(
                false,
                MacOperationStage.Failed,
                MacProofState.Pending,
                "renderer.apply_failed",
                null));
            var cleanupViewModel = new MainWindowViewModel(cleanupClient);
            await cleanupViewModel.InitializeAsync();
            cleanupViewModel.SelectTheme(cleanupViewModel.VisibleThemes[0]);
            await cleanupViewModel.ApplyCommand.ExecuteAsync();
            var cleanup = CreateWindow(cleanupViewModel, 960, 620);
            cleanup.Show();
            Capture(cleanup, "12-safe-cleanup-960x620.png");
            Assert.True(cleanupViewModel.IsSafeCleanup);
            cleanup.Close();

            var failureClient = new FakeMacThemeStudioClient(
                operationDelay: TimeSpan.Zero);
            failureClient.SetNextApplyOutcome(new MacThemeOperationOutcome(
                false,
                MacOperationStage.Failed,
                MacProofState.Unverified,
                "renderer.apply_failed",
                "inspector.close_failed"));
            var failureViewModel = new MainWindowViewModel(failureClient);
            await failureViewModel.InitializeAsync();
            failureViewModel.SelectTheme(
                failureViewModel.VisibleThemes[0]);
            await failureViewModel.ApplyCommand.ExecuteAsync();
            var failure = CreateWindow(failureViewModel, 1240, 780);
            failure.Show();
            Capture(
                failure,
                "13-primary-and-recovery-error-1240x780.png");
            Assert.True(failureViewModel.HasError);
            Assert.True(failureViewModel.HasRecoveryError);
            Assert.DoesNotContain(
                failureViewModel.PrimaryError!,
                failureViewModel.PrimaryErrorMessage,
                StringComparison.Ordinal);
            failure.Close();
        });

    [Fact]
    public Task SettingsAboutAndFocusOrderRenderWithFixtureOnlyData() =>
        AvaloniaTestHost.DispatchAsync(() =>
        {
            var settings = new SettingsWindow();
            settings.Show();
            Capture(settings, "14a-settings-general.png");
            var viewModel = Assert.IsType<SettingsWindowViewModel>(
                settings.DataContext);
            viewModel.ShowDiagnosticsCommand.Execute(null);
            Capture(settings, "14b-settings-diagnostics.png");
            settings.Close();

            var about = new AboutWindow();
            about.Show();
            Capture(about, "15-about-window-retina.png");
            about.Close();
        });

    [Fact]
    public Task HundredThemeViewRealizesOnlyVisibleCardRows() =>
        AvaloniaTestHost.DispatchAsync(async () =>
        {
            var fixtures = Enumerable.Range(0, 100)
                .Select(CreateTheme)
                .ToArray();
            var viewModel = new MainWindowViewModel(
                new FakeMacThemeStudioClient(
                    fixtures,
                    operationDelay: TimeSpan.Zero));
            await viewModel.InitializeAsync();
            var window = CreateWindow(viewModel, 1240, 780);
            window.Show();
            Capture(window, "16-focus-order.png");

            var realizedCards = window
                .GetVisualDescendants()
                .OfType<Button>()
                .Count(button => button.Classes.Contains("card"));
            Assert.InRange(realizedCards, 1, 20);
            Assert.Equal(100, viewModel.VisibleThemes.Count);
            window.Close();
        });

    private static async Task<MainWindowViewModel>
        CreateInitializedViewModelAsync()
    {
        var viewModel = new MainWindowViewModel(
            new FakeMacThemeStudioClient(
                operationDelay: TimeSpan.Zero));
        await viewModel.InitializeAsync();
        return viewModel;
    }

    private static MainWindow CreateWindow(
        MainWindowViewModel viewModel,
        double width,
        double height) =>
        new()
        {
            DataContext = viewModel,
            Width = width,
            Height = height,
        };

    private static void AssertKeyControlInside(
        Window window,
        string controlName)
    {
        var control = window.FindControl<Control>(controlName);
        Assert.NotNull(control);
        var bounds = GetBoundsInWindow(window, control);
        Assert.True(bounds.Left >= 0, $"{controlName} exceeds left edge.");
        Assert.True(bounds.Top >= 0, $"{controlName} exceeds top edge.");
        Assert.True(
            bounds.Right <= window.ClientSize.Width + 0.5,
            $"{controlName} exceeds right edge.");
        Assert.True(
            bounds.Bottom <= window.ClientSize.Height + 0.5,
            $"{controlName} exceeds bottom edge.");
    }

    private static void AssertHorizontalGap(
        Window window,
        string leftControlName,
        string rightControlName,
        double expectedGap)
    {
        var left = window.FindControl<Control>(leftControlName);
        var right = window.FindControl<Control>(rightControlName);
        Assert.NotNull(left);
        Assert.NotNull(right);
        var leftBounds = GetBoundsInWindow(window, left);
        var rightBounds = GetBoundsInWindow(window, right);
        Assert.InRange(
            rightBounds.Left - leftBounds.Right,
            expectedGap - 0.5,
            expectedGap + 0.5);
    }

    private static Rect GetBoundsInWindow(
        Window window,
        Control control)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window);
        Assert.NotNull(origin);
        return new Rect(origin.Value, control.Bounds.Size);
    }

    private static void Capture(Window window, string fileName)
    {
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var directory = Environment.GetEnvironmentVariable(
            "CTS_UI_AUDIT_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        frame.Save(
            Path.Combine(directory, fileName),
            PngBitmapEncoderOptions.Default);
    }

    private static MacThemeLibraryItem CreateTheme(int index) =>
        new(
            Guid.NewGuid(),
            $"主题 {index:000}",
            "虚拟化 fixture",
            ["本地", index % 2 == 0 ? "冷色" : "暖色"],
            new ThemePalette(
                "#111111",
                "#181818",
                "#2563EB",
                "#F5F5F5",
                "#A3A3A3",
                "#303030"),
            index % 3 == 0,
            DateTimeOffset.UtcNow.AddDays(-index),
            true,
            new Uri(
                "avares://CodexThemeStudio.Desktop.MacOS/" +
                "Assets/Fixtures/twilight-lake.png"));

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
