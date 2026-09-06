using CodexThemeStudio.Contracts.Models;

namespace CodexThemeStudio.Desktop.MacOS.Services;

public sealed class FakeMacThemeStudioClient : IMacThemeStudioClient
{
    private static readonly TimeSpan DefaultDelay =
        TimeSpan.FromMilliseconds(280);

    private readonly IReadOnlyList<MacThemeLibraryItem> themes;
    private readonly TimeSpan operationDelay;
    private MacRuntimeSnapshot runtime;
    private MacThemeOperationOutcome? nextApplyOutcome;
    private MacThemeOperationOutcome? nextRestoreOutcome;

    public FakeMacThemeStudioClient(
        IReadOnlyList<MacThemeLibraryItem>? themes = null,
        MacRuntimeSnapshot? runtime = null,
        TimeSpan? operationDelay = null)
    {
        this.themes = themes ?? CreateFixtures();
        this.runtime = runtime ?? new MacRuntimeSnapshot(
            MacRuntimeState.ReadyUnqualified,
            true,
            "Codex 已就绪；首次应用需要完成安全资格验证。");
        this.operationDelay = operationDelay ?? DefaultDelay;
    }

    public static FakeMacThemeStudioClient CreateDefault() => new();

    public void SetRuntime(MacRuntimeSnapshot value)
    {
        runtime = value ?? throw new ArgumentNullException(nameof(value));
    }

    public void SetNextApplyOutcome(MacThemeOperationOutcome value)
    {
        nextApplyOutcome =
            value ?? throw new ArgumentNullException(nameof(value));
    }

    public void SetNextRestoreOutcome(MacThemeOperationOutcome value)
    {
        nextRestoreOutcome =
            value ?? throw new ArgumentNullException(nameof(value));
    }

    public async Task<MacThemeLibrarySnapshot> LoadLibraryAsync(
        CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(80), cancellationToken);
        return new MacThemeLibrarySnapshot(
            themes,
            true,
            "应用范围：调色板与受支持的声明式样式；背景只在 Studio 内预览");
    }

    public async Task<MacRuntimeSnapshot> RefreshRuntimeAsync(
        CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(60), cancellationToken);
        return runtime;
    }

    public ValueTask<MacThemeOperationHandle> BeginApplyTemporaryAsync(
        Guid themeId,
        CancellationToken admissionCancellationToken)
    {
        admissionCancellationToken.ThrowIfCancellationRequested();
        if (themeId == Guid.Empty || themes.All(theme => theme.ThemeId != themeId))
        {
            throw new ArgumentException(
                "A known fixture theme is required.",
                nameof(themeId));
        }

        var outcome = nextApplyOutcome ?? new MacThemeOperationOutcome(
            true,
            MacOperationStage.TemporaryApplied,
            MacProofState.Verified,
            null,
            null);
        nextApplyOutcome = null;
        return ValueTask.FromResult(CreateHandle(outcome));
    }

    public ValueTask<MacThemeOperationHandle> BeginRestoreAsync(
        CancellationToken admissionCancellationToken)
    {
        admissionCancellationToken.ThrowIfCancellationRequested();
        var outcome = nextRestoreOutcome ?? new MacThemeOperationOutcome(
            true,
            MacOperationStage.Idle,
            MacProofState.Verified,
            null,
            null);
        nextRestoreOutcome = null;
        return ValueTask.FromResult(CreateHandle(outcome));
    }

    private MacThemeOperationHandle CreateHandle(
        MacThemeOperationOutcome outcome)
    {
        var operationId = Guid.NewGuid();
        var completion = CompleteAsync(outcome);
        return new MacThemeOperationHandle(
            operationId,
            true,
            completion);
    }

    private async Task<MacThemeOperationOutcome> CompleteAsync(
        MacThemeOperationOutcome outcome)
    {
        await Task.Delay(operationDelay, CancellationToken.None);
        return outcome;
    }

    private static IReadOnlyList<MacThemeLibraryItem> CreateFixtures() =>
    [
        CreateTheme(
            "深海蓝",
            "沉静的深蓝工作环境",
            "#0B1220",
            "#111C2E",
            "#3B82F6",
            "#E7EEF9",
            "#8EA2BF",
            "#29405F",
            true,
            true,
            "twilight-lake.png",
            ["本地", "深色", "冷色"]),
        CreateTheme(
            "石墨",
            "克制的中性黑灰",
            "#111111",
            "#191919",
            "#7C8AA0",
            "#F5F5F5",
            "#A3A3A3",
            "#303030",
            false,
            true,
            "graphite-amber.png",
            ["本地", "深色", "中性"]),
        CreateTheme(
            "苔原绿",
            "低饱和自然色",
            "#101713",
            "#17231C",
            "#4ADE80",
            "#ECFDF3",
            "#9AB5A3",
            "#294535",
            true,
            true,
            "glacier-cyan.png",
            ["本地", "深色", "自然"]),
        CreateTheme(
            "暮光紫",
            "柔和紫色强调",
            "#15111D",
            "#21182D",
            "#A78BFA",
            "#F5F0FF",
            "#A99DB9",
            "#403451",
            false,
            true,
            "twilight-lake.png",
            ["本地", "深色", "柔和"]),
        CreateTheme(
            "琥珀夜",
            "暖色聚焦主题",
            "#17130C",
            "#241C10",
            "#F59E0B",
            "#FFF7E8",
            "#B9A98B",
            "#4B3B20",
            false,
            true,
            "graphite-amber.png",
            ["本地", "深色", "暖色"]),
        CreateTheme(
            "冰川",
            "清晰的冷灰对比",
            "#0E1418",
            "#172127",
            "#22D3EE",
            "#EAFBFF",
            "#93AEB7",
            "#2B424B",
            true,
            true,
            "glacier-cyan.png",
            ["本地", "深色", "冷色"]),
    ];

    private static MacThemeLibraryItem CreateTheme(
        string name,
        string subtitle,
        string background,
        string panel,
        string accent,
        string text,
        string muted,
        string border,
        bool favorite,
        bool hasBackground,
        string previewFile,
        IReadOnlyList<string> tags) =>
        new(
            Guid.NewGuid(),
            name,
            subtitle,
            tags,
            new ThemePalette(
                background,
                panel,
                accent,
                text,
                muted,
                border),
            favorite,
            DateTimeOffset.UtcNow.AddDays(
                name.Length % 4 + 1),
            hasBackground,
            new Uri(
                "avares://CodexThemeStudio.Desktop.MacOS/" +
                $"Assets/Fixtures/{previewFile}"));
}
