namespace CodexThemeStudio.ThemeCore.Tests;

using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.ThemeCore;

public class ThemePackageContractValidatorTests
{
    [Fact]
    public void Serializer_RoundTripsChineseAndEmojiName()
    {
        var theme = CreateValidTheme("夜航主题 🌙✨");
        var serializer = new ThemeDocumentSerializer();

        var serialized = serializer.Serialize(theme);
        var read = serializer.Read(serialized.Value!);

        Assert.True(serialized.IsSuccess);
        Assert.Equal(ThemeDocumentReadStatus.Success, read.Status);
        Assert.Equal(theme, read.Theme);
    }

    [Fact]
    public void Serializer_RoundTripsCropArtSize()
    {
        var theme = CreateValidTheme("裁切主题") with
        {
            Art = CreateValidTheme("裁切主题").Art with
            {
                Size = ThemeArtSize.Crop,
                FocusX = 0.2,
                FocusY = 0.8,
            },
        };
        var serializer = new ThemeDocumentSerializer();

        var serialized = serializer.Serialize(theme);
        var read = serializer.Read(serialized.Value!);

        Assert.True(serialized.IsSuccess);
        Assert.Equal(ThemeDocumentReadStatus.Success, read.Status);
        Assert.Equal(ThemeArtSize.Crop, read.Theme!.Art.Size);
        Assert.Equal(0.2, read.Theme.Art.FocusX);
        Assert.Equal(0.8, read.Theme.Art.FocusY);
    }

    [Fact]
    public void Serializer_RejectsUnknownFieldInCurrentSchema()
    {
        var serializer = new ThemeDocumentSerializer();
        var json = """
            {
              "schemaVersion": 1,
              "id": "1296cb77-2297-4992-af72-5c3cc40b32be",
              "name": "Unknown field",
              "variant": "auto",
              "palette": {
                "background": "#100D14",
                "panel": "#18131D",
                "accent": "#B98BD2",
                "text": "#EEE8F2",
                "muted": "#A99EAE",
                "border": "#302735"
              },
              "art": {
                "file": "background.webp",
                "focusX": 0.5,
                "focusY": 0.5,
                "safeArea": "auto",
                "size": "cover",
                "homeOpacity": 0.8,
                "homeOverlay": 0.25,
                "taskMode": "ambient",
                "taskOpacity": 0.3,
                "taskOverlay": 0.65,
                "blur": 0
              },
              "unexpected": true
            }
            """;

        var read = serializer.Read(System.Text.Encoding.UTF8.GetBytes(json));

        Assert.Equal(ThemeDocumentReadStatus.Invalid, read.Status);
        Assert.Contains(read.Issues, issue => issue.Code == "theme.json.invalid");
    }

    [Fact]
    public void Serializer_ReportsNewerSchemaWithoutDeserializing()
    {
        var serializer = new ThemeDocumentSerializer();
        var json = """{"schemaVersion":99,"futureField":{"enabled":true}}""";

        var read = serializer.Read(System.Text.Encoding.UTF8.GetBytes(json));

        Assert.Equal(ThemeDocumentReadStatus.UnsupportedNewerSchema, read.Status);
        Assert.Equal(99, read.SchemaVersion);
        Assert.Null(read.Theme);
    }

    [Fact]
    public void Serializer_RejectsIllegalEnumValue()
    {
        var serializer = new ThemeDocumentSerializer();
        var serialized = serializer.Serialize(CreateValidTheme("非法枚举"));
        var json = System.Text.Encoding.UTF8.GetString(serialized.Value!)
            .Replace("\"variant\": \"auto\"", "\"variant\": \"future\"", StringComparison.Ordinal);

        var read = serializer.Read(System.Text.Encoding.UTF8.GetBytes(json));

        Assert.Equal(ThemeDocumentReadStatus.Invalid, read.Status);
        Assert.Contains(read.Issues, issue => issue.Code == "theme.json.invalid");
    }

    [Fact]
    public void Serializer_ReportsMissingNestedObjectsAsValidationIssues()
    {
        var serializer = new ThemeDocumentSerializer();
        var json = """{"schemaVersion":1}""";

        var read = serializer.Read(System.Text.Encoding.UTF8.GetBytes(json));

        Assert.Equal(ThemeDocumentReadStatus.Invalid, read.Status);
        Assert.Contains(read.Issues, issue => issue.Code == "theme.palette.missing");
        Assert.Contains(read.Issues, issue => issue.Code == "theme.art.missing");
    }

    [Fact]
    public void Validator_AcceptsMaximumLengthAndRejectsLongerName()
    {
        var accepted = CreateValidTheme(new string('界', 120));
        var rejected = CreateValidTheme(new string('界', 121));

        Assert.Empty(ThemePackageContractValidator.Validate(accepted));
        Assert.Contains(
            ThemePackageContractValidator.Validate(rejected),
            issue => issue.Code == "theme.name.too_long");
    }

    [Theory]
    [InlineData("../escape.webp")]
    [InlineData("C:\\escape.webp")]
    [InlineData("/escape.webp")]
    [InlineData("assets//background.webp")]
    [InlineData("CON.webp")]
    public void Validator_RejectsUnsafeArtPath(string path)
    {
        var theme = CreateValidTheme("Unsafe") with
        {
            Art = CreateValidTheme("Unsafe").Art with { File = path },
        };

        var issues = ThemePackageContractValidator.Validate(theme);

        Assert.Contains(issues, issue => issue.Code == "theme.art.file.invalid");
    }

    [Fact]
    public void Validator_RejectsInvalidColorAndNumericRange()
    {
        var theme = CreateValidTheme("Invalid") with
        {
            Palette = CreateValidTheme("Invalid").Palette with
            {
                Accent = "red",
            },
            Art = CreateValidTheme("Invalid").Art with
            {
                FocusX = 1.1,
                Blur = 65,
            },
        };

        var issues = ThemePackageContractValidator.Validate(theme);

        Assert.Contains(issues, issue => issue.Code == "theme.color.invalid");
        Assert.Equal(
            2,
            issues.Count(issue => issue.Code == "theme.number.out_of_range"));
    }

    internal static ThemePackage CreateValidTheme(
        string name,
        Guid? id = null) =>
        new(
            ThemePackageContractValidator.CurrentSchemaVersion,
            id ?? Guid.NewGuid(),
            name,
            ThemeVariant.Auto,
            new ThemePalette(
                "#100D14",
                "#18131D",
                "#B98BD2",
                "#EEE8F2",
                "#A99EAE",
                "#302735"),
            new ThemeArt(
                "background.webp",
                0.5,
                0.5,
                ThemeSafeArea.Auto,
                ThemeArtSize.Cover,
                0.8,
                0.25,
                ThemeTaskMode.Ambient,
                0.3,
                0.65,
                0));
}

public class RelativePathPolicyTests
{
    [Theory]
    [InlineData("themes/abc/theme.json", "themes/abc/theme.json")]
    [InlineData("themes\\abc\\theme.json", "themes/abc/theme.json")]
    public void Normalize_ReturnsPortablePath(string input, string expected)
    {
        var result = RelativePathPolicy.Normalize(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }
}
