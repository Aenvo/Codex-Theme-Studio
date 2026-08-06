using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using CodexThemeStudio.Desktop.Controls;
using CodexThemeStudio.Desktop.ViewModels;

namespace CodexThemeStudio.Desktop.Tests;

public sealed class MainWindowScreenshotTests
{
    [Fact]
    public void MainWindow_RendersAtMinimumAndStandardSizes()
    {
        WpfTestHost.Invoke(() =>
        {
            using var fixture = new ViewModelFixture(themeCount: 100, externalThemeActive: true);
            fixture.Runtime.Status = new(
                Contracts.Models.ThemeRuntimeState.Temporary,
                fixture.Repository.Summaries[0].ThemeId,
                false,
                1234,
                DateTimeOffset.UtcNow,
                "已通过运行时标记确认主题状态。",
                fixture.Repository.Summaries[0].ThemeId,
                Evidence: Contracts.Models.ThemeRuntimeEvidence.RuntimeMarkers);
            fixture.ViewModel.InitializeAsync().GetAwaiter().GetResult();
            fixture.ViewModel.WaitForBackgroundInitializationAsync().GetAwaiter().GetResult();

            RenderWindow(
                fixture.ViewModel,
                960,
                620,
                120,
                Environment.GetEnvironmentVariable("CTS_MINIMUM_SCREENSHOT_PATH"));
            fixture.ViewModel.SelectedTheme = fixture.ViewModel.Themes[0];
            RenderWindow(fixture.ViewModel, 960, 620, 144, screenshotPath: null);
            RenderWindow(fixture.ViewModel, 960, 620, 192, screenshotPath: null);
            RenderWindow(
                fixture.ViewModel,
                1240,
                780,
                96,
                Environment.GetEnvironmentVariable("CTS_SCREENSHOT_PATH"));

            using var warningFixture = new ViewModelFixture(themeCount: 3);
            warningFixture.Runtime.Status = new(
                Contracts.Models.ThemeRuntimeState.Unsupported,
                null,
                false,
                1234,
                DateTimeOffset.UtcNow,
                "Codex 缺少主题运行所需能力，未执行注入。",
                CodexVersion: "test-version",
                CompatibilityLevel: Contracts.Models.CodexCompatibilityLevel.Incompatible,
                IsPersistenceEligible: false);
            warningFixture.ViewModel.InitializeAsync().GetAwaiter().GetResult();
            warningFixture.ViewModel.WaitForBackgroundInitializationAsync().GetAwaiter().GetResult();
            warningFixture.ViewModel.SelectedTheme = warningFixture.ViewModel.Themes[0];
            RenderWindow(
                warningFixture.ViewModel,
                1240,
                780,
                96,
                Environment.GetEnvironmentVariable("CTS_TOAST_SCREENSHOT_PATH"));

            foreach (var identity in new[]
                     {
                         Contracts.Models.CodexIdentityAssessment.TrustedStore,
                         Contracts.Models.CodexIdentityAssessment.UnverifiedSource,
                     })
            {
                using var probeFixture = new ViewModelFixture(themeCount: 1);
                probeFixture.Runtime.Status = new(
                    Contracts.Models.ThemeRuntimeState.NotRunning,
                    null,
                    false,
                    null,
                    DateTimeOffset.UtcNow,
                    "能力探测已通过。",
                    CodexVersion: "future-version",
                    CompatibilityLevel: Contracts.Models.CodexCompatibilityLevel.CompatibleByProbe,
                    IdentityAssessment: identity,
                    IsPersistenceEligible: false);
                probeFixture.ViewModel.InitializeAsync().GetAwaiter().GetResult();
                probeFixture.ViewModel.WaitForBackgroundInitializationAsync().GetAwaiter().GetResult();
                probeFixture.ViewModel.NavigateCommand.Execute("Settings");
                RenderWindow(probeFixture.ViewModel, 1240, 780, 96, screenshotPath: null);
            }

            using var missingFixture = new ViewModelFixture(themeCount: 3);
            missingFixture.Runtime.Status = new(
                Contracts.Models.ThemeRuntimeState.NotInstalled,
                null,
                false,
                null,
                DateTimeOffset.UtcNow,
                "未检测到当前用户安装的官方 Microsoft Store Codex。");
            missingFixture.ViewModel.InitializeAsync().GetAwaiter().GetResult();
            missingFixture.ViewModel.WaitForBackgroundInitializationAsync().GetAwaiter().GetResult();
            missingFixture.ViewModel.SelectedTheme = missingFixture.ViewModel.Themes[0];
            RenderWindow(
                missingFixture.ViewModel,
                1240,
                780,
                96,
                Environment.GetEnvironmentVariable("CTS_UNDETECTED_SCREENSHOT_PATH"));

            fixture.ViewModel.SetLibraryLayoutCommand.Execute("List");
            RenderWindow(
                fixture.ViewModel,
                1240,
                780,
                96,
                Environment.GetEnvironmentVariable("CTS_LIST_SCREENSHOT_PATH"));
            fixture.ViewModel.SetLibraryLayoutCommand.Execute("Cards");
            fixture.Editor.Begin(
                fixture.Repository.Themes[fixture.Repository.Summaries[0].ThemeId],
                newTheme: false,
                thumbnailPath:
                    "/CodexThemeStudio.Desktop;component/Assets/app-icon.png");
            fixture.ViewModel.NavigateCommand.Execute("Editor");
            RenderWindow(
                fixture.ViewModel,
                1240,
                780,
                96,
                screenshotPath: null);
            fixture.Editor.ArtSize = Contracts.Models.ThemeArtSize.Crop;
            fixture.Editor.FocusX = 0.2;
            fixture.Editor.FocusY = 0.8;
            fixture.Editor.IsTaskPreview = false;
            RenderWindow(
                fixture.ViewModel,
                1240,
                780,
                96,
                Environment.GetEnvironmentVariable("CTS_EDITOR_HOME_SCREENSHOT_PATH"));
            fixture.Editor.IsTaskPreview = true;
            RenderWindow(
                fixture.ViewModel,
                1240,
                780,
                96,
                Environment.GetEnvironmentVariable("CTS_EDITOR_SCREENSHOT_PATH"));
            RenderWindow(fixture.ViewModel, 2560, 1440, 96, screenshotPath: null);
            fixture.ViewModel.NavigateCommand.Execute("Settings");
            RenderWindow(
                fixture.ViewModel,
                1240,
                780,
                96,
                Environment.GetEnvironmentVariable("CTS_MIGRATION_SCREENSHOT_PATH"));
            fixture.ViewModel.SelectedSettingsSection = SettingsSection.Diagnostics;
            RenderWindow(
                fixture.ViewModel,
                1240,
                780,
                96,
                Environment.GetEnvironmentVariable("CTS_DIAGNOSTICS_SCREENSHOT_PATH"));
            RenderWindow(
                fixture.ViewModel,
                960,
                620,
                192,
                screenshotPath: null);
            fixture.ViewModel.SelectedSettingsSection = SettingsSection.About;
            RenderWindow(
                fixture.ViewModel,
                1240,
                780,
                96,
                Environment.GetEnvironmentVariable("CTS_ABOUT_SCREENSHOT_PATH"));

            using var trashFixture = new ViewModelFixture(themeCount: 3);
            trashFixture.Repository.DeletedThemeIds.Add(
                trashFixture.Repository.Summaries[0].ThemeId);
            trashFixture.ViewModel.InitializeAsync().GetAwaiter().GetResult();
            trashFixture.ViewModel.WaitForBackgroundInitializationAsync().GetAwaiter().GetResult();
            trashFixture.ViewModel.NavigateCommand.Execute("Trash");
            RenderWindow(
                trashFixture.ViewModel,
                1240,
                780,
                96,
                Environment.GetEnvironmentVariable("CTS_TRASH_SCREENSHOT_PATH"));
            trashFixture.ViewModel.SetLibraryLayoutCommand.Execute("List");
            RenderWindow(
                trashFixture.ViewModel,
                1240,
                780,
                96,
                Environment.GetEnvironmentVariable("CTS_TRASH_LIST_SCREENSHOT_PATH"));

            using var emptyTrashFixture = new ViewModelFixture(themeCount: 0);
            emptyTrashFixture.ViewModel.InitializeAsync().GetAwaiter().GetResult();
            emptyTrashFixture.ViewModel.WaitForBackgroundInitializationAsync().GetAwaiter().GetResult();
            emptyTrashFixture.ViewModel.NavigateCommand.Execute("Trash");
            RenderWindow(
                emptyTrashFixture.ViewModel,
                960,
                620,
                120,
                Environment.GetEnvironmentVariable("CTS_EMPTY_TRASH_SCREENSHOT_PATH"));

            var updateRelease = FakeUpdateService.CreateRelease("1.3.0");
            using var updateFixture = new ViewModelFixture(
                3,
                updateService: new FakeUpdateService(
                    Contracts.Results.OperationResult<Contracts.Models.UpdateCheckResult>.Success(
                        new Contracts.Models.UpdateCheckResult(
                            "1.2.2",
                            true,
                            updateRelease,
                            false))),
                updateDialogs: new FakeUpdateDialogs(),
                appVersion: "1.3.0");
            updateFixture.ViewModel.CheckUpdatesCommand.Execute(null);
            Assert.True(updateFixture.ViewModel.IsUpdateAvailable);
            updateFixture.ViewModel.NavigateToUpdateCommand.Execute(null);
            RenderWindow(
                updateFixture.ViewModel,
                1240,
                780,
                96,
                Environment.GetEnvironmentVariable("CTS_UPDATE_ABOUT_SCREENSHOT_PATH"));
            RenderWindow(
                updateFixture.ViewModel,
                960,
                620,
                96,
                Environment.GetEnvironmentVariable("CTS_UPDATE_ABOUT_MIN_SCREENSHOT_PATH"));
        });
    }

    [Fact]
    public void UpdateDialog_RendersReleaseNotesAndDownloadProgress()
    {
        WpfTestHost.Invoke(() =>
        {
            var release = FakeUpdateService.CreateRelease("1.3.0") with
            {
                ReleaseNotes = string.Join('\n', Enumerable.Range(1, 40).Select(
                    index => $"{index}. 安全更新说明与回滚验证内容。")),
            };
            var dialog = new UpdateDialogWindow(
                "1.2.2",
                release,
                () => { },
                (progress, _) =>
                {
                    progress.Report(new Contracts.Models.UpdateDownloadProgress(
                        42,
                        100,
                        42,
                        "正在下载更新"));
                    return Task.FromResult(
                        Contracts.Results.OperationResult<Contracts.Models.UpdateInstallResult>.Failure(
                            Contracts.Results.OperationErrorCode.InvalidResponse,
                            "测试校验失败。"));
                },
                CancellationToken.None);
            try
            {
                dialog.Show();
                dialog.UpdateLayout();
                CaptureElement(
                    Assert.IsAssignableFrom<FrameworkElement>(dialog.Content),
                    Environment.GetEnvironmentVariable("CTS_UPDATE_DIALOG_SCREENSHOT_PATH"));
                var download = FindVisualChildren<Button>(dialog)
                    .Single(button => Equals(button.Content, "下载更新"));
                download.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                dialog.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                dialog.UpdateLayout();
                CaptureElement(
                    Assert.IsAssignableFrom<FrameworkElement>(dialog.Content),
                    Environment.GetEnvironmentVariable("CTS_UPDATE_PROGRESS_SCREENSHOT_PATH"));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static void CaptureElement(FrameworkElement element, string? screenshotPath)
    {
        if (string.IsNullOrWhiteSpace(screenshotPath)) return;
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath)!);
        using var stream = File.Create(screenshotPath);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static void RenderWindow(
        MainWindowViewModel viewModel,
        int width,
        int height,
        double dpi,
        string? screenshotPath)
    {
        var window = new MainWindow(viewModel)
        {
            Width = width,
            Height = height,
        };
        var root = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
        root.DataContext = viewModel;
        if (root is Panel panel)
        {
            panel.Background = Assert.IsAssignableFrom<Brush>(
                Application.Current.Resources["WindowBrush"]);
        }
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();

        Assert.NotNull(window.Icon);
        var updateButtons = FindVisualChildren<Button>(root)
            .Where(button => AutomationProperties.GetName(button) == "有新的更新可用")
            .ToArray();
        Assert.Equal(4, updateButtons.Length);
        Assert.All(updateButtons, button =>
        {
            Assert.Equal(36, button.Width);
            Assert.Equal(36, button.Height);
            Assert.Equal("有新的更新可用", button.ToolTip);
            Assert.True(button.Focusable);
            Assert.Same(viewModel.NavigateToUpdateCommand, button.Command);
            Assert.Equal(
                viewModel.IsUpdateAvailable ? Visibility.Visible : Visibility.Collapsed,
                button.Visibility);
        });
        var productIcon = Assert.Single(
            FindVisualChildren<Image>(root),
            image => AutomationProperties.GetName(image) == "Codex Theme Studio 产品图标");
        Assert.Equal(36, productIcon.Width);
        Assert.Equal(36, productIcon.Height);
        Assert.NotNull(productIcon.Source);
        Assert.Contains(
            FindVisualChildren<TextBlock>(root),
            textBlock => textBlock.Text == "Codex 主题配置工作台");

        var navigationNames = FindVisualChildren<Button>(root)
            .Select(AutomationProperties.GetName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        Assert.Contains("工作台", navigationNames);
        Assert.Contains("回收站", navigationNames);
        Assert.DoesNotContain("导入", navigationNames);
        Assert.DoesNotContain("当前主题", navigationNames);

        var busyProgress = Assert.Single(
            FindVisualChildren<ProgressBar>(root),
            progress => AutomationProperties.GetName(progress) == "主题应用进度");
        busyProgress.ApplyTemplate();
        Assert.True(
            double.IsNaN(busyProgress.Width),
            $"进度控件应使用自动宽度，当前 Width={busyProgress.Width}。");
        Assert.Equal(83, busyProgress.MinWidth);
        Assert.Equal(16, busyProgress.Height);
        Assert.Equal(HorizontalAlignment.Stretch, busyProgress.HorizontalAlignment);
        Assert.Equal(Visibility.Hidden, busyProgress.Visibility);
        var busySegments = new[]
        {
            "BusySegmentOne",
            "BusySegmentTwo",
            "BusySegmentThree",
            "BusySegmentFour",
            "BusySegmentFive",
            "BusySegmentSix",
            "BusySegmentSeven",
            "BusySegmentEight",
            "BusySegmentNine",
            "BusySegmentTen",
            "BusySegmentEleven",
            "BusySegmentTwelve",
            "BusySegmentThirteen",
            "BusySegmentFourteen",
            "BusySegmentFifteen",
            "BusySegmentSixteen",
            "BusySegmentSeventeen",
            "BusySegmentEighteen",
            "BusySegmentNineteen",
            "BusySegmentTwenty",
            "BusySegmentTwentyOne",
            "BusySegmentTwentyTwo",
            "BusySegmentTwentyThree",
            "BusySegmentTwentyFour",
        };
        Assert.All(
            busySegments,
            segmentName =>
            {
                var segment = Assert.IsType<Border>(
                    busyProgress.Template.FindName(segmentName, busyProgress));
                Assert.Equal(new CornerRadius(0), segment.CornerRadius);
                Assert.Equal(2, segment.Width);
                Assert.Equal(16, segment.Height);
            });
        var busyTrigger = Assert.Single(
            busyProgress.Template.Triggers.OfType<Trigger>(),
            trigger =>
                trigger.Property == ProgressBar.IsIndeterminateProperty &&
                Equals(trigger.Value, true));
        var beginStoryboard = Assert.IsType<BeginStoryboard>(
            Assert.Single(busyTrigger.EnterActions));
        Assert.Equal(24, beginStoryboard.Storyboard.Children.Count);
        if (viewModel.SelectedTheme is not null && viewModel.IsLibraryVisible)
        {
            var applyButton = Assert.Single(
                FindVisualChildren<Button>(root),
                button => Equals(button.Content, "临时应用"));
            var temporaryToolTip = Assert.IsType<ToolTip>(applyButton.ToolTip);
            Assert.Equal(PlacementMode.Custom, temporaryToolTip.Placement);
            var placementCallback = Assert.IsType<CustomPopupPlacementCallback>(
                temporaryToolTip.CustomPopupPlacementCallback);
            var placements = placementCallback(
                new Size(220, 30),
                new Size(100, 44),
                new Point());
            var placement = Assert.Single(placements);
            Assert.Equal(new Point(-60, -38), placement.Point);
            Assert.Equal(PopupPrimaryAxis.Horizontal, placement.PrimaryAxis);
            Assert.Equal(
                "关闭当前 Codex 应用后失效",
                Assert.IsType<string>(temporaryToolTip.Content));
            Assert.Equal(350, ToolTipService.GetInitialShowDelay(applyButton));
            Assert.Equal(5000, ToolTipService.GetShowDuration(applyButton));
            var persistentButton = Assert.Single(
                FindVisualChildren<Button>(root),
                button => Equals(button.Content, "设为持久主题"));
            var persistentToolTip = Assert.IsType<ToolTip>(persistentButton.ToolTip);
            Assert.Equal(PlacementMode.Custom, persistentToolTip.Placement);
            var persistentPlacementCallback = Assert.IsType<CustomPopupPlacementCallback>(
                persistentToolTip.CustomPopupPlacementCallback);
            var persistentPlacements = persistentPlacementCallback(
                new Size(300, 30),
                new Size(120, 44),
                new Point());
            var persistentPlacement = Assert.Single(persistentPlacements);
            Assert.Equal(new Point(-90, -38), persistentPlacement.Point);
            Assert.Equal(PopupPrimaryAxis.Horizontal, persistentPlacement.PrimaryAxis);
            Assert.Equal(
                "Codex 应用可永远保持主题持久化，直到还原外观",
                persistentToolTip.Content);
            Assert.Equal(350, ToolTipService.GetInitialShowDelay(persistentButton));
            Assert.Equal(5000, ToolTipService.GetShowDuration(persistentButton));
            var progressPositionBeforeLoading = busyProgress.TranslatePoint(new Point(), root);
            var applyPositionBeforeLoading = applyButton.TranslatePoint(new Point(), root);
            var progressSizeBeforeLoading = busyProgress.RenderSize;
            busyProgress.Visibility = Visibility.Visible;
            root.UpdateLayout();
            var firstSegment = Assert.IsType<Border>(
                busyProgress.Template.FindName("BusySegmentOne", busyProgress));
            var lastSegment = Assert.IsType<Border>(
                busyProgress.Template.FindName("BusySegmentTwentyFour", busyProgress));
            var segmentSpan = lastSegment.TranslatePoint(new Point(1, 0), busyProgress).X -
                              firstSegment.TranslatePoint(new Point(1, 0), busyProgress).X;
            Assert.True(
                busyProgress.ActualWidth >= busyProgress.MinWidth,
                $"进度控件实际宽度应不小于 {busyProgress.MinWidth}，当前为 {busyProgress.ActualWidth}。");
            Assert.True(
                segmentSpan >= busyProgress.ActualWidth * 0.9,
                $"24 个进度格应铺满可用宽度，实际跨度为 {segmentSpan:F1}/{busyProgress.ActualWidth:F1}。 ");
            Assert.Equal(
                progressPositionBeforeLoading,
                busyProgress.TranslatePoint(new Point(), root));
            Assert.Equal(progressSizeBeforeLoading, busyProgress.RenderSize);
            Assert.Equal(
                applyPositionBeforeLoading,
                applyButton.TranslatePoint(new Point(), root));
            busyProgress.Visibility = Visibility.Hidden;
            root.UpdateLayout();
        }
        var detectionText = Assert.Single(
            FindVisualChildren<TextBlock>(root),
            textBlock =>
                System.Windows.Data.BindingOperations
                    .GetBindingExpression(textBlock, TextBlock.TextProperty)?
                    .ParentBinding.Path.Path == "CodexStatusText");
        Assert.Equal(viewModel.CodexStatusText, detectionText.Text);
        var toastMessage = Assert.Single(
            FindVisualChildren<TextBlock>(root),
            textBlock =>
                System.Windows.Data.BindingOperations
                    .GetBindingExpression(textBlock, TextBlock.TextProperty)?
                    .ParentBinding.Path.Path == "NotificationMessage");
        Assert.Equal(viewModel.NotificationMessage, toastMessage.Text);
        var toast = Assert.Single(
            FindVisualChildren<Border>(root),
            border => AutomationProperties.GetName(border) == "Sonner 消息通知");
        Assert.Equal(
            viewModel.IsNotificationVisible ? Visibility.Visible : Visibility.Collapsed,
            toast.Visibility);
        Assert.DoesNotContain("全部主题", navigationNames);
        var applicationSurface = Assert.Single(
            FindVisualChildren<Grid>(root),
            grid => AutomationProperties.GetName(grid) == "应用主布局");
        var applicationSidebar = Assert.Single(
            FindVisualChildren<Border>(root),
            border => AutomationProperties.GetName(border) == "应用侧栏");
        Assert.Equal(
            viewModel.IsEditorVisible ? 0 : 240,
            applicationSurface.ColumnDefinitions[0].ActualWidth);
        Assert.Equal(
            viewModel.IsEditorVisible ? Visibility.Collapsed : Visibility.Visible,
            applicationSidebar.Visibility);

        if (viewModel.IsEditorVisible)
        {
            var editorText = FindVisualChildren<TextBlock>(root)
                .Select(textBlock => textBlock.Text)
                .ToArray();
            var editorButtons = FindVisualChildren<Button>(root)
                .Select(button => button.Content as string)
                .Where(content => content is not null)
                .ToArray();

            Assert.DoesNotContain("外观", editorText);
            Assert.DoesNotContain("自动外观预览", editorText);
            Assert.DoesNotContain("浅色预览", editorText);
            Assert.DoesNotContain("深色预览", editorText);
            Assert.DoesNotContain("浅色", editorButtons);
            Assert.DoesNotContain("深色", editorButtons);
            var editor = Assert.IsType<ThemeEditorViewModel>(viewModel.Editor);
            var editorScrollViewer = Assert.Single(
                FindVisualChildren<ScrollViewer>(root),
                scrollViewer => AutomationProperties.GetName(scrollViewer) == "编辑面板滚动区域");
            Assert.Equal(editor.IsColorPickerOpen, EditorScrollLockBehavior.GetIsLocked(editorScrollViewer));
            Assert.Equal(ScrollBarVisibility.Disabled, editorScrollViewer.HorizontalScrollBarVisibility);
            var editorContentPresenter = Assert.Single(
                FindVisualChildren<ScrollContentPresenter>(editorScrollViewer),
                presenter => ReferenceEquals(
                    presenter.TemplatedParent,
                    editorScrollViewer));
            Assert.InRange(
                Math.Abs(editorContentPresenter.ActualWidth - editorScrollViewer.ActualWidth),
                0,
                1);
            var editorScrollBar = Assert.Single(
                FindVisualChildren<ScrollBar>(editorScrollViewer),
                scrollBar =>
                    scrollBar.Orientation == Orientation.Vertical &&
                    ReferenceEquals(
                        scrollBar.TemplatedParent,
                        editorScrollViewer));
            Assert.Equal(3, editorScrollBar.Width);
            Assert.Equal(3, editorScrollBar.MinWidth);
            Assert.Equal(3, editorScrollBar.MaxWidth);
            Assert.Equal(0, editorScrollBar.Opacity);
            Assert.Equal(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), ((SolidColorBrush)editorScrollBar.Background).Color);
            Assert.DoesNotContain("安全区", editorText);
            var cropFocusSliders = FindVisualChildren<Slider>(root)
                .Where(slider =>
                    (AutomationProperties.GetName(slider) is
                        "裁切水平位置" or "裁切垂直位置" or "裁切图片放缩") &&
                    IsLayoutVisible(slider, root))
                .ToArray();
            Assert.Equal(editor.IsCropMode ? 3 : 0, cropFocusSliders.Length);
            if (editor.IsCropMode)
            {
                var previewImage = Assert.Single(
                    FindVisualChildren<Image>(root),
                    image => AutomationProperties.GetName(image) == "模拟预览背景图");
                Assert.NotNull(previewImage.Source);
                var before = Assert.IsType<TranslateTransform>(
                    previewImage.RenderTransform);
                var previousOffsetY = before.Y;
                editor.FocusY = editor.FocusY < 0.5 ? 0.8 : 0.2;
                root.UpdateLayout();
                var after = Assert.IsType<TranslateTransform>(
                    previewImage.RenderTransform);
                Assert.NotEqual(previousOffsetY, after.Y);
            }
            Assert.Contains("主题编辑器", editorText);
            Assert.DoesNotContain(
                "所有修改先进入草稿；模拟预览不等同于真实 Codex 渲染验证。",
                editorText);
            Assert.Contains("主题资料库", editorText);
            var editorBreadcrumb = Assert.Single(
                FindVisualChildren<StackPanel>(root),
                panel => AutomationProperties.GetName(panel) == "编辑器面包屑");
            var breadcrumbText = FindVisualChildren<TextBlock>(editorBreadcrumb)
                .Select(textBlock => textBlock.Text)
                .ToArray();
            Assert.Equal(["主题资料库", "/", editor.Name], breadcrumbText);
            var returnToLibrary = Assert.Single(
                FindVisualChildren<Button>(editorBreadcrumb),
                button => AutomationProperties.GetName(button) == "返回主题资料库");
            Assert.Same(viewModel.CancelDraftCommand, returnToLibrary.Command);
            Assert.Equal("返回主题资料库", returnToLibrary.ToolTip);
            Assert.Equal(
                DependencyProperty.UnsetValue,
                returnToLibrary.ReadLocalValue(Control.ForegroundProperty));
            Assert.InRange(editorBreadcrumb.ActualHeight, 14, 32);
            Assert.DoesNotContain("模拟预览", editorText);
            Assert.DoesNotContain(
                FindVisualChildren<Button>(root),
                button => button.Content as string == "选择图片…");
            Assert.Single(
                FindVisualChildren<Button>(root),
                button => AutomationProperties.GetName(button) == "上传背景图片");
            var imageUploadOverlay = Assert.Single(
                FindVisualChildren<Border>(root),
                border => AutomationProperties.GetName(border) == "背景图片上传遮罩");
            Assert.Equal(Visibility.Collapsed, imageUploadOverlay.Visibility);
            var homePreviewButton = Assert.Single(
                FindVisualChildren<Button>(root),
                button => button.Content as string == "首页预览");
            var taskPreviewButton = Assert.Single(
                FindVisualChildren<Button>(root),
                button => button.Content as string == "任务页预览");
            var accentBrush = Assert.IsType<SolidColorBrush>(
                Application.Current.Resources["AccentBrush"]);
            var borderBrush = Assert.IsType<SolidColorBrush>(
                Application.Current.Resources["BorderBrush"]);
            Assert.Equal(
                editor.IsTaskPreview ? borderBrush.Color : accentBrush.Color,
                Assert.IsType<SolidColorBrush>(homePreviewButton.BorderBrush).Color);
            Assert.Equal(new Thickness(2), homePreviewButton.BorderThickness);
            Assert.Equal(new Thickness(2), taskPreviewButton.BorderThickness);
            var homePreviewPosition = homePreviewButton.TranslatePoint(new Point(), root);
            var homePreviewSize = homePreviewButton.RenderSize;
            var taskPreviewPosition = taskPreviewButton.TranslatePoint(new Point(), root);
            var taskPreviewSize = taskPreviewButton.RenderSize;
            var initialTaskPreview = editor.IsTaskPreview;
            editor.IsTaskPreview = !initialTaskPreview;
            root.UpdateLayout();
            Assert.Equal(homePreviewPosition, homePreviewButton.TranslatePoint(new Point(), root));
            Assert.Equal(homePreviewSize, homePreviewButton.RenderSize);
            Assert.Equal(taskPreviewPosition, taskPreviewButton.TranslatePoint(new Point(), root));
            Assert.Equal(taskPreviewSize, taskPreviewButton.RenderSize);
            editor.IsTaskPreview = initialTaskPreview;
            root.UpdateLayout();
            var previewOptions = FindVisualChildren<CheckBox>(root)
                .Select(checkBox => checkBox.Content as string)
                .Where(content => content is not null)
                .ToArray();
            Assert.DoesNotContain("显示侧栏", previewOptions);
            Assert.DoesNotContain("窄窗口", previewOptions);
            var preview = Assert.Single(
                FindVisualChildren<Border>(root),
                border => AutomationProperties.GetName(border) == "模拟预览");
            var previewSidebar = Assert.Single(
                FindVisualChildren<Border>(root),
                border => AutomationProperties.GetName(border) == "模拟预览侧栏");
            var previewRegion = Assert.Single(
                FindVisualChildren<Grid>(root),
                grid => AutomationProperties.GetName(grid) == "模拟预览区域");
            var previewToolbar = Assert.Single(
                FindVisualChildren<DockPanel>(root),
                panel => AutomationProperties.GetName(panel) == "模拟预览工具栏");
            Assert.True(double.IsNaN(preview.Width));
            Assert.True(double.IsNaN(preview.Height));
            Assert.Equal(HorizontalAlignment.Stretch, preview.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Stretch, preview.VerticalAlignment);
            Assert.InRange(
                Math.Abs(preview.ActualWidth - previewRegion.ActualWidth),
                0,
                1);
            Assert.InRange(
                Math.Abs(preview.ActualHeight - (
                    previewRegion.ActualHeight -
                    previewToolbar.ActualHeight -
                    previewToolbar.Margin.Top -
                    previewToolbar.Margin.Bottom)),
                0,
                1);
            Assert.DoesNotContain(
                FindVisualChildren<ScrollViewer>(root),
                scrollViewer => AutomationProperties.GetName(scrollViewer) == "模拟预览滚动区域");
            Assert.True(preview.ActualWidth > 600);
            Assert.Equal(Visibility.Visible, previewSidebar.Visibility);
            var homePreview = Assert.Single(
                FindVisualChildren<Grid>(root),
                grid => AutomationProperties.GetName(grid) == "Codex 首页模拟内容");
            var taskPreview = Assert.Single(
                FindVisualChildren<Grid>(root),
                grid => AutomationProperties.GetName(grid) == "Codex 任务页模拟内容");
            Assert.Equal(
                editor.IsTaskPreview ? Visibility.Collapsed : Visibility.Visible,
                homePreview.Visibility);
            Assert.Equal(
                editor.IsTaskPreview ? Visibility.Visible : Visibility.Collapsed,
                taskPreview.Visibility);
            if (editor.IsTaskPreview)
            {
                var previewCanvas = Assert.Single(
                    FindVisualChildren<Grid>(preview),
                    grid => AutomationProperties.GetName(grid) == "模拟预览画布");
                var taskContentHost = Assert.Single(
                    FindVisualChildren<Grid>(taskPreview),
                    grid => AutomationProperties.GetName(grid) == "Codex 任务模拟主内容区");
                var taskWorkspace = Assert.Single(
                    FindVisualChildren<StackPanel>(taskPreview),
                    panel => AutomationProperties.GetName(panel) == "Codex 任务模拟工作区");
                Assert.Equal(HorizontalAlignment.Center, taskWorkspace.HorizontalAlignment);
                Assert.InRange(
                    Math.Abs(taskWorkspace.ActualWidth - Math.Min(taskContentHost.ActualWidth, 720)),
                    0,
                    1);
                var taskInput = Assert.Single(
                    FindVisualChildren<Border>(taskPreview),
                    border => AutomationProperties.GetName(border) == "Codex 任务页模拟输入框");
                Assert.InRange(
                    Math.Abs(taskInput.ActualWidth - Math.Min(taskContentHost.ActualWidth, 760)),
                    0,
                    1);
                var taskEnvironment = Assert.Single(
                    FindVisualChildren<Border>(taskPreview),
                    border => AutomationProperties.GetName(border) == "Codex 任务模拟环境面板");
                var isEnvironmentVisible = previewCanvas.ActualWidth >= 960;
                Assert.Equal(
                    isEnvironmentVisible ? Visibility.Visible : Visibility.Collapsed,
                    taskEnvironment.Visibility);
                Assert.InRange(
                    Math.Abs(taskPreview.ColumnDefinitions[1].ActualWidth -
                        (isEnvironmentVisible ? 260 : 0)),
                    0,
                    1);
            }
            Assert.Contains("本地预览", editorText);
            Assert.Contains(
                editorText,
                text => text?.EndsWith("演示工作区", StringComparison.Ordinal) == true);
            Assert.DoesNotContain("Mike Clark", editorText);

            Assert.Contains(
                editorText,
                text => text?.StartsWith("任务内容区遮罩深度：", StringComparison.Ordinal) == true);
            Assert.DoesNotContain(
                "仅调整任务内容区的深色遮罩，不影响侧栏和顶部栏；隐藏背景模式下不可调。",
                editorText);
            Assert.DoesNotContain(
                "点击色块打开色板；支持 Hex、RGB 与 CSS rgba()。",
                editorText);
            var taskOverlaySlider = Assert.Single(
                FindVisualChildren<Slider>(root),
                slider => AutomationProperties.GetName(slider) == "任务内容区遮罩深度");
            Assert.True(taskOverlaySlider.IsEnabled);
            Assert.True(SliderJumpBehavior.GetIsEnabled(taskOverlaySlider));
            Assert.Equal(editor.TaskOverlay, taskOverlaySlider.Value);
            var taskOverlayPreview = Assert.Single(
                FindVisualChildren<Border>(root),
                border => AutomationProperties.GetName(border) == "任务内容区遮罩预览");
            Assert.Equal(editor.TaskContentOverlay, taskOverlayPreview.Opacity);
            taskOverlaySlider.BringIntoView();
            root.UpdateLayout();
        }

        if (viewModel.IsSettingsVisible)
        {
            var settingsTabs = Assert.Single(
                FindVisualChildren<TabControl>(root));
            var tabItems = settingsTabs.Items
                .Cast<TabItem>()
                .ToArray();
            Assert.Equal(3, tabItems.Length);
            Assert.Equal(
                new[] { "通用", "诊断", "关于" },
                tabItems.Select(item => item.Header as string));
            Assert.All(
                tabItems,
                item =>
                {
                    Assert.Equal(104, item.MinWidth);
                    Assert.Equal(36, item.MinHeight);
                    Assert.True(item.Focusable);
                    Assert.Equal(Cursors.Hand, item.Cursor);
                });
            var selected = Assert.Single(tabItems, item => item.IsSelected);
            Assert.Equal(
                viewModel.SelectedSettingsSection,
                Assert.IsType<SettingsSection>(selected.Tag));
            var selectedTabChrome = Assert.IsType<Border>(
                selected.Template.FindName("TabChrome", selected));
            var selectedTabBackground = Assert.IsType<SolidColorBrush>(
                Application.Current.Resources["InputBrush"]);
            Assert.Equal(
                selectedTabBackground.Color,
                Assert.IsType<SolidColorBrush>(selectedTabChrome.Background).Color);
            Assert.Equal(
                Assert.IsType<SolidColorBrush>(Application.Current.Resources["ForegroundBrush"]).Color,
                Assert.IsType<SolidColorBrush>(selected.Foreground).Color);

            if (viewModel.SelectedSettingsSection == SettingsSection.Diagnostics)
            {
                var diagnosticButtons = FindVisualChildren<Button>(root)
                    .Select(button => button.Content as string)
                    .Where(content => content is not null)
                    .ToArray();
                Assert.Contains("刷新诊断", diagnosticButtons);
                Assert.Contains("打开日志目录", diagnosticButtons);
                Assert.Contains("复制 Issue 摘要", diagnosticButtons);
                Assert.Contains("导出诊断包…", diagnosticButtons);
                Assert.Contains(
                    FindVisualChildren<TextBlock>(root),
                    textBlock => textBlock.Text == "最近事件");
            }
            else if (viewModel.SelectedSettingsSection == SettingsSection.About)
            {
                var gitHubButton = Assert.Single(
                    FindVisualChildren<Button>(root),
                    button => AutomationProperties.GetName(button) == "打开 GitHub 仓库");
                Assert.Same(viewModel.OpenGitHubRepositoryCommand, gitHubButton.Command);
                Assert.Contains(
                    FindVisualChildren<TextBlock>(gitHubButton),
                    textBlock => textBlock.Text == "GitHub");
                Assert.Contains(
                    FindVisualChildren<ContentControl>(gitHubButton),
                    contentControl => contentControl.Content is GeometryGroup);
            }
        }

        var selectionStatus = Assert.Single(
            FindVisualChildren<Border>(root),
            border => AutomationProperties.GetName(border) == "所选主题状态");
        Assert.Equal(0.94, selectionStatus.Opacity, precision: 2);
        Assert.Equal(
            viewModel.SelectedTheme is not null && viewModel.IsLibraryVisible
                ? Visibility.Visible
                : Visibility.Collapsed,
            selectionStatus.Visibility);
        if (selectionStatus.Visibility == Visibility.Visible)
        {
            var statusBottom = selectionStatus
                .TranslatePoint(new Point(0, selectionStatus.ActualHeight), root)
                .Y;
            Assert.InRange(statusBottom, root.ActualHeight - 24, root.ActualHeight);
        }

        var sidebarButtons = FindVisualChildren<Button>(root)
            .Where(button =>
                AutomationProperties.GetName(button) is "工作台" or "收藏" or "回收站" or "设置")
            .ToDictionary(AutomationProperties.GetName);
        var selectedNavigationName = viewModel.CurrentPage switch
        {
            LibraryPage.Favorites => "收藏",
            LibraryPage.Trash => "回收站",
            LibraryPage.Settings => "设置",
            _ => "工作台",
        };
        var sidebarAccent = Assert.IsType<SolidColorBrush>(
            Application.Current.Resources["SidebarAccentBrush"]);
        Assert.Equal(sidebarAccent.Color, Assert.IsType<SolidColorBrush>(
            sidebarButtons[selectedNavigationName].Background).Color);
        Assert.All(
            sidebarButtons.Values,
            button =>
            {
                Assert.Equal(new Thickness(1), button.BorderThickness);
                button.ApplyTemplate();
                var navigationChrome = Assert.IsType<Border>(
                    button.Template.FindName("NavigationChrome", button));
                Assert.Equal(new Thickness(1), navigationChrome.BorderThickness);
                Assert.Equal(
                    Colors.Transparent,
                    Assert.IsType<SolidColorBrush>(navigationChrome.BorderBrush).Color);
            });

        if (viewModel.IsTrashVisible)
        {
            Assert.Contains("清空回收站", navigationNames);
            Assert.Contains("还原已删除主题", navigationNames);
            Assert.Contains("永久删除主题", navigationNames);

            var trashCardView = FindVisualChildren<ListBox>(root)
                .Single(list => AutomationProperties.GetName(list) == "回收站卡片展示");
            var trashListView = FindVisualChildren<ListBox>(root)
                .Single(list => AutomationProperties.GetName(list) == "回收站列表展示");
            Assert.Equal(viewModel.Themes.Count, trashCardView.Items.Count);
            Assert.Equal(viewModel.Themes.Count, trashListView.Items.Count);
            Assert.Equal(
                viewModel.IsCardView ? Visibility.Visible : Visibility.Collapsed,
                trashCardView.Visibility);
            Assert.Equal(
                viewModel.IsListView ? Visibility.Visible : Visibility.Collapsed,
                trashListView.Visibility);

            var trashLayoutButtons = FindVisualChildren<ToggleButton>(root)
                .Where(button =>
                    AutomationProperties.GetName(button) is
                        "回收站卡片展示模式" or "回收站列表展示模式")
                .ToArray();
            Assert.Equal(2, trashLayoutButtons.Length);
            Assert.Equal("卡片展示", trashLayoutButtons[0].ToolTip);
            Assert.Equal("列表展示", trashLayoutButtons[1].ToolTip);
            Assert.Equal(viewModel.IsCardView, trashLayoutButtons[0].IsChecked == true);
            Assert.Equal(viewModel.IsListView, trashLayoutButtons[1].IsChecked == true);
            Assert.All(
                trashLayoutButtons,
                button => Assert.Equal(350, ToolTipService.GetInitialShowDelay(button)));
            Assert.Equal(
                viewModel.Themes.Count == 0,
                !viewModel.EmptyTrashCommand.CanExecute(null));
        }

        var contentButtonNames = FindVisualChildren<Button>(root)
            .Select(AutomationProperties.GetName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        Assert.DoesNotContain("收藏或取消收藏", contentButtonNames);
        if (contentButtonNames.Contains("新建主题"))
        {
            Assert.Contains("导入主题", contentButtonNames);
            Assert.Contains("还原默认外观", contentButtonNames);
            Assert.Contains("刷新主题资料库", contentButtonNames);

            var libraryHeaderButtons = FindVisualChildren<Button>(root)
                .Where(button =>
                    AutomationProperties.GetName(button) is
                        "还原默认外观" or "导入主题" or "新建主题")
                .OrderBy(button => button.TranslatePoint(new Point(), root).X)
                .ToArray();
            Assert.Equal(
                ["还原默认外观", "导入主题", "新建主题"],
                libraryHeaderButtons.Select(AutomationProperties.GetName));

            var restoreDefaultButton = libraryHeaderButtons[0];
            Assert.Same(viewModel.RestoreCommand, restoreDefaultButton.Command);
            Assert.Same(
                Application.Current.Resources["GhostButtonStyle"],
                restoreDefaultButton.Style);

            var refreshButton = Assert.Single(
                FindVisualChildren<Button>(root),
                button => AutomationProperties.GetName(button) == "刷新主题资料库");
            Assert.Equal("刷新主题资料库", refreshButton.ToolTip);
            Assert.Equal(350, ToolTipService.GetInitialShowDelay(refreshButton));
            Assert.Equal(38, refreshButton.Width);
        }

        var searchBox = FindVisualChildren<TextBox>(root)
            .SingleOrDefault(textBox => AutomationProperties.GetName(textBox) == "搜索主题");
        if (searchBox is not null)
        {
            Assert.Equal("搜索名称或标签", searchBox.Tag);
            Assert.Equal(42, searchBox.MinHeight);

            var filterBoxes = FindVisualChildren<ComboBox>(root)
                .Where(comboBox =>
                    AutomationProperties.GetName(comboBox) is
                        "主题范围" or "标签筛选" or "排序方式")
                .ToArray();
            Assert.Equal(3, filterBoxes.Length);

            var scopeFilter = Assert.Single(
                filterBoxes,
                comboBox => AutomationProperties.GetName(comboBox) == "主题范围");
            scopeFilter.ApplyTemplate();
            var chrome = Assert.IsType<Border>(
                scopeFilter.Template.FindName("Chrome", scopeFilter));
            Assert.Equal(8, chrome.CornerRadius.TopLeft);
            var toggle = Assert.IsType<ToggleButton>(
                FindVisualChild<ToggleButton>(scopeFilter));
            Assert.NotNull(toggle.Template);
            toggle.ApplyTemplate();
            var toggleChrome = Assert.IsType<Border>(
                FindVisualChild<Border>(toggle));
            var toggleBackground = Assert.IsType<SolidColorBrush>(toggleChrome.Background);
            Assert.Equal(Colors.Transparent, toggleBackground.Color);
        }

        var layoutButtons = FindVisualChildren<ToggleButton>(root)
            .Where(button =>
                AutomationProperties.GetName(button) is "卡片展示" or "列表展示")
            .ToArray();
        if (layoutButtons.Length > 0)
        {
            Assert.Equal(2, layoutButtons.Length);
            Assert.Equal("卡片展示", layoutButtons[0].ToolTip);
            Assert.Equal("列表展示", layoutButtons[1].ToolTip);
            Assert.Equal(viewModel.IsCardView, layoutButtons[0].IsChecked == true);
            Assert.Equal(viewModel.IsListView, layoutButtons[1].IsChecked == true);
            Assert.All(
                layoutButtons,
                button => Assert.Equal(350, ToolTipService.GetInitialShowDelay(button)));
            Assert.All(
                layoutButtons,
                button =>
                {
                    Assert.NotNull(button.FocusVisualStyle);
                    button.ApplyTemplate();
                    var segmentChrome = Assert.IsType<Border>(
                        button.Template.FindName("SegmentChrome", button));
                    var segmentBorder = Assert.IsType<SolidColorBrush>(
                        segmentChrome.BorderBrush);
                    Assert.Equal(Colors.Transparent, segmentBorder.Color);
                });
        }

        var themeGrid = FindVisualChild<VirtualizingUniformGrid>(root);
        if (viewModel.IsLibraryVisible &&
            viewModel.Themes.Count >= 20 &&
            themeGrid is not null &&
            themeGrid.ActualHeight > 0)
        {
            var cardPreviews = FindVisualChildren<Border>(themeGrid)
                .Where(border => AutomationProperties.GetName(border) == "卡片预览")
                .ToArray();
            Assert.NotEmpty(cardPreviews);
            Assert.All(
                cardPreviews,
                preview => Assert.Equal(new CornerRadius(10, 10, 0, 0), preview.CornerRadius));

            var favoriteButtons = FindVisualChildren<ToggleButton>(themeGrid)
                .Where(button =>
                    AutomationProperties.GetName(button).StartsWith(
                        "收藏或取消收藏：",
                        StringComparison.Ordinal))
                .ToArray();
            Assert.NotEmpty(favoriteButtons);
            Assert.All(
                favoriteButtons,
                button =>
                {
                    var card = Assert.IsType<ThemeCardViewModel>(button.DataContext);
                    Assert.Same(viewModel.ToggleFavoriteCommand, button.Command);
                    Assert.Same(card, button.CommandParameter);
                    Assert.Equal(card.IsFavorite, button.IsChecked == true);
                    Assert.Equal(card.IsFavorite ? Visibility.Visible : Visibility.Collapsed, button.Visibility);
                    Assert.Equal(36, button.Width);
                    Assert.Equal(36, button.Height);

                    button.ApplyTemplate();
                    var chrome = Assert.IsType<Border>(
                        button.Template.FindName("FavoriteChrome", button));
                    Assert.Equal(new CornerRadius(8), chrome.CornerRadius);
                    var icon = Assert.IsType<System.Windows.Shapes.Path>(
                        button.Template.FindName("FavoriteIcon", button));
                    Assert.Equal(
                        card.IsFavorite ? Color.FromRgb(250, 204, 21) : Colors.Transparent,
                        Assert.IsType<SolidColorBrush>(icon.Fill).Color);
                });

            var cardSubtitles = FindVisualChildren<TextBlock>(themeGrid)
                .Where(textBlock =>
                    AutomationProperties.GetName(textBlock) == "卡片副标题")
                .ToArray();
            Assert.NotEmpty(cardSubtitles);
            Assert.All(
                cardSubtitles,
                subtitle => Assert.Equal(
                    Assert.IsType<ThemeCardViewModel>(subtitle.DataContext).CardSubtitleText,
                    subtitle.Text));

            var cardContainers = FindVisualChildren<ListBoxItem>(themeGrid).ToArray();
            Assert.NotEmpty(cardContainers);
            Assert.All(
                cardContainers,
                container =>
                {
                    container.ApplyTemplate();
                    var card = Assert.IsType<Border>(
                        container.Template.FindName("Card", container));
                    var cardOutline = Assert.IsType<Border>(
                        container.Template.FindName("CardOutline", container));
                    Assert.Equal(new Thickness(1), card.Margin);
                    Assert.Equal(new CornerRadius(12), card.CornerRadius);
                    Assert.Equal(
                        new Thickness(container.IsSelected ? 2 : 1),
                        card.BorderThickness);
                    Assert.Equal(card.Margin, cardOutline.Margin);
                    Assert.Equal(card.CornerRadius, cardOutline.CornerRadius);
                    Assert.Equal(card.BorderThickness, cardOutline.BorderThickness);
                    Assert.Equal(1, Panel.GetZIndex(cardOutline));
                    Assert.False(cardOutline.IsHitTestVisible);
                    Assert.True(card.SnapsToDevicePixels);
                    Assert.True(card.UseLayoutRounding);
                    Assert.True(cardOutline.SnapsToDevicePixels);
                    Assert.True(cardOutline.UseLayoutRounding);
                });

            var statusGroups = FindVisualChildren<StackPanel>(themeGrid)
                .Where(panel => AutomationProperties.GetName(panel) == "主题状态标签组")
                .ToArray();
            Assert.NotEmpty(statusGroups);
            Assert.All(
                statusGroups,
                group => Assert.True(
                    group.TranslatePoint(new Point(), themeGrid).Y >= 152,
                    "主题状态标签应位于卡片信息区的标题行，而不是封面预览层。"));

            var realizedCount = VisualTreeHelper.GetChildrenCount(themeGrid);
            Assert.InRange(realizedCount, 1, 20);
            Assert.True(themeGrid.ExtentHeight > themeGrid.ViewportHeight);

            themeGrid.PageDown();
            root.UpdateLayout();
            Assert.True(themeGrid.VerticalOffset > 0);
            Assert.InRange(VisualTreeHelper.GetChildrenCount(themeGrid), 1, 20);
            themeGrid.SetVerticalOffset(0);
            root.UpdateLayout();
        }

        var listView = FindVisualChildren<ListBox>(root)
            .SingleOrDefault(list => AutomationProperties.GetName(list) == "主题列表展示");
        if (viewModel.IsLibraryVisible &&
            listView is not null &&
            listView.ActualHeight > 0)
        {
            var itemsHost = FindVisualChild<VirtualizingStackPanel>(listView);
            Assert.NotNull(itemsHost);
            Assert.InRange(VisualTreeHelper.GetChildrenCount(itemsHost), 1, 20);
        }

        var pixelWidth = (int)Math.Round(width * dpi / 96);
        var pixelHeight = (int)Math.Round(height * dpi / 96);
        var bitmap = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            dpi,
            dpi,
            PixelFormats.Pbgra32);
        bitmap.Render(root);
        Assert.Equal(pixelWidth, bitmap.PixelWidth);
        Assert.Equal(pixelHeight, bitmap.PixelHeight);

        if (!string.IsNullOrWhiteSpace(screenshotPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath)!);
            using var stream = File.Create(screenshotPath);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
        }

    }

    [Fact]
    public void SearchBoxOutsideClick_ClearsFocusButInsideClickDoesNot()
    {
        WpfTestHost.Invoke(() =>
        {
            var searchBox = new TextBox();
            var outside = new Border();
            var panel = new StackPanel();
            panel.Children.Add(searchBox);
            panel.Children.Add(outside);
            var window = new Window { Content = panel };
            try
            {
                window.Show();
                window.UpdateLayout();
                searchBox.ApplyTemplate();
                Assert.True(searchBox.Focus());
                Assert.True(searchBox.IsKeyboardFocusWithin);

                var inside = VisualTreeHelper.GetChild(searchBox, 0);
                Assert.False(MainWindow.ClearSearchFocusOnOutsideClick(searchBox, inside));
                Assert.True(searchBox.IsKeyboardFocusWithin);

                Assert.True(MainWindow.ClearSearchFocusOnOutsideClick(searchBox, outside));
                Assert.False(searchBox.IsKeyboardFocusWithin);
                Assert.False(MainWindow.ClearSearchFocusOnOutsideClick(searchBox, outside));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ThemeListBackgroundClick_ClearsSelectionButItemAndScrollbarClicksDoNot()
    {
        WpfTestHost.Invoke(() =>
        {
            var first = new ListBoxItem();
            var second = new ListBoxItem();
            var listBox = new ListBox();
            listBox.Items.Add(first);
            listBox.Items.Add(second);
            var window = new Window
            {
                Content = listBox,
                Left = -10000,
                Top = -10000,
                Width = 320,
                Height = 240,
                ShowInTaskbar = false,
            };
            try
            {
                window.Show();
                window.Activate();
                window.UpdateLayout();
                listBox.SelectedItem = first;

                Assert.True(MainWindow.ClearThemeSelectionOnBackgroundClick(listBox, listBox));
                Assert.Null(listBox.SelectedItem);
                Assert.True(listBox.Focusable);

                listBox.SelectedItem = second;
                Assert.False(MainWindow.ClearThemeSelectionOnBackgroundClick(listBox, second));
                Assert.Same(second, listBox.SelectedItem);

                var scrollBar = new ScrollBar();
                Assert.False(MainWindow.ClearThemeSelectionOnBackgroundClick(listBox, scrollBar));
                Assert.Same(second, listBox.SelectedItem);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static bool IsLayoutVisible(
        FrameworkElement element,
        DependencyObject root)
    {
        for (DependencyObject? current = element;
             current is not null && !ReferenceEquals(current, root);
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { Visibility: not Visibility.Visible })
            {
                return false;
            }
        }

        return true;
    }
}
