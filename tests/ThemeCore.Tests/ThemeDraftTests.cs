using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.ThemeCore;

namespace CodexThemeStudio.ThemeCore.Tests;

public sealed class ThemeDraftTests
{
    [Fact]
    public void Draft_DoesNotMutateOriginalAndCanReset()
    {
        var original = CreateTheme();
        var draft = new ThemeDraft(original)
        {
            Name = "草稿名称",
            Art = original.Art with { FocusX = 0.9 },
        };

        Assert.Equal("正式主题", original.Name);
        Assert.Equal(0.5, original.Art.FocusX);
        Assert.Equal("草稿名称", draft.Build().Name);

        draft.Reset();
        Assert.Equal(original, draft.Build());
    }

    private static ThemePackage CreateTheme() =>
        new(
            1,
            Guid.NewGuid(),
            "正式主题",
            ThemeVariant.Auto,
            new ThemePalette(
                "#111827",
                "#1F2937",
                "#6D5EF7",
                "#F9FAFB",
                "#9CA3AF",
                "#374151"),
            new ThemeArt(
                "background.webp",
                0.5,
                0.5,
                ThemeSafeArea.Auto,
                ThemeArtSize.Cover,
                0.72,
                0.28,
                ThemeTaskMode.Ambient,
                0.22,
                0.62,
                0));
}
