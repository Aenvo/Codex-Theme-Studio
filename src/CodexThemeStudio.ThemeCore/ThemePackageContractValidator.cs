namespace CodexThemeStudio.ThemeCore;

using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

public static class ThemePackageContractValidator
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumNameLength = 120;
    public const double MaximumBlur = 64;

    public static IReadOnlyList<ValidationIssue> Validate(ThemePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var issues = new List<ValidationIssue>();

        if (package.SchemaVersion != CurrentSchemaVersion)
        {
            issues.Add(new ValidationIssue(
                "theme.schema_version.invalid",
                $"仅支持 Schema v{CurrentSchemaVersion}。",
                ValidationSeverity.Error,
                nameof(package.SchemaVersion)));
        }

        if (package.Id == Guid.Empty)
        {
            issues.Add(new ValidationIssue(
                "theme.id.empty",
                "主题 ID 不能为空。",
                ValidationSeverity.Error,
                nameof(package.Id)));
        }

        if (string.IsNullOrWhiteSpace(package.Name))
        {
            issues.Add(new ValidationIssue(
                "theme.name.empty",
                "主题名称不能为空。",
                ValidationSeverity.Error,
                nameof(package.Name)));
        }
        else
        {
            if (package.Name.Length > MaximumNameLength)
            {
                issues.Add(new ValidationIssue(
                    "theme.name.too_long",
                    $"主题名称不能超过 {MaximumNameLength} 个 UTF-16 字符。",
                    ValidationSeverity.Error,
                    nameof(package.Name)));
            }

            if (package.Name.Any(char.IsControl))
            {
                issues.Add(new ValidationIssue(
                    "theme.name.control_character",
                    "主题名称不能包含控制字符。",
                    ValidationSeverity.Error,
                    nameof(package.Name)));
            }
        }

        if (!Enum.IsDefined(package.Variant))
        {
            issues.Add(new ValidationIssue(
                "theme.variant.invalid",
                "主题明暗模式不受支持。",
                ValidationSeverity.Error,
                nameof(package.Variant)));
        }

        if (package.Palette is null)
        {
            issues.Add(new ValidationIssue(
                "theme.palette.missing",
                "主题缺少 palette。",
                ValidationSeverity.Error,
                nameof(package.Palette)));
        }
        else
        {
            ValidatePalette(package.Palette, issues);
        }

        if (package.Art is null)
        {
            issues.Add(new ValidationIssue(
                "theme.art.missing",
                "主题缺少 art。",
                ValidationSeverity.Error,
                nameof(package.Art)));
        }
        else
        {
            ValidateArt(package.Art, issues);
        }

        return issues;
    }

    private static void ValidatePalette(
        ThemePalette palette,
        ICollection<ValidationIssue> issues)
    {
        ValidateColor(palette.Background, "palette.background", issues);
        ValidateColor(palette.Panel, "palette.panel", issues);
        ValidateColor(palette.Accent, "palette.accent", issues);
        ValidateColor(palette.Text, "palette.text", issues);
        ValidateColor(palette.Muted, "palette.muted", issues);
        ValidateColor(palette.Border, "palette.border", issues);
    }

    private static void ValidateColor(
        string color,
        string fieldPath,
        ICollection<ValidationIssue> issues)
    {
        if (!ThemeColor.TryNormalize(color, out _))
        {
            issues.Add(new ValidationIssue(
                "theme.color.invalid",
                "颜色必须使用 #RRGGBB 或 #RRGGBBAA 格式。",
                ValidationSeverity.Error,
                fieldPath));
        }
    }

    private static void ValidateArt(
        ThemeArt art,
        ICollection<ValidationIssue> issues)
    {
        var pathResult = RelativePathPolicy.Normalize(art.File);
        if (!pathResult.IsSuccess)
        {
            issues.Add(new ValidationIssue(
                "theme.art.file.invalid",
                pathResult.Error!.UserMessage,
                ValidationSeverity.Error,
                "art.file"));
        }
        else
        {
            var extension = Path.GetExtension(pathResult.Value);
            var supportedExtensions = new[] { ".png", ".jpg", ".jpeg", ".webp" };
            if (!supportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(new ValidationIssue(
                    "theme.art.file.extension",
                    "背景资源只允许 PNG、JPEG 或 WebP。",
                    ValidationSeverity.Error,
                    "art.file"));
            }
        }

        ValidateRange(art.FocusX, 0, 1, "art.focusX", issues);
        ValidateRange(art.FocusY, 0, 1, "art.focusY", issues);
        ValidateRange(art.HomeOpacity, 0, 1, "art.homeOpacity", issues);
        ValidateRange(art.HomeOverlay, 0, 1, "art.homeOverlay", issues);
        ValidateRange(art.TaskOpacity, 0, 1, "art.taskOpacity", issues);
        ValidateRange(art.TaskOverlay, 0, 1, "art.taskOverlay", issues);
        ValidateRange(art.Blur, 0, MaximumBlur, "art.blur", issues);

        ValidateEnum(art.SafeArea, "theme.art.safe_area.invalid", "art.safeArea", issues);
        ValidateEnum(art.Size, "theme.art.size.invalid", "art.size", issues);
        ValidateEnum(art.TaskMode, "theme.art.task_mode.invalid", "art.taskMode", issues);
    }

    private static void ValidateRange(
        double value,
        double minimum,
        double maximum,
        string fieldPath,
        ICollection<ValidationIssue> issues)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            issues.Add(new ValidationIssue(
                "theme.number.out_of_range",
                $"{fieldPath} 必须位于 {minimum} 到 {maximum} 之间。",
                ValidationSeverity.Error,
                fieldPath));
        }
    }

    private static void ValidateEnum<TEnum>(
        TEnum value,
        string code,
        string fieldPath,
        ICollection<ValidationIssue> issues)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            issues.Add(new ValidationIssue(
                code,
                $"{fieldPath} 的值不受支持。",
                ValidationSeverity.Error,
                fieldPath));
        }
    }
}
