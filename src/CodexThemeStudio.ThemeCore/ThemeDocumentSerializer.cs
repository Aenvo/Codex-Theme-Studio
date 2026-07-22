using System.Text.Json;
using System.Text.Json.Serialization;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.ThemeCore;

public sealed class ThemeDocumentSerializer
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 32,
    };

    private readonly JsonSerializerOptions serializerOptions = CreateSerializerOptions();

    public ThemeDocumentReadResult Read(ReadOnlyMemory<byte> utf8Json)
    {
        try
        {
            using var document = JsonDocument.Parse(utf8Json, DocumentOptions);
            if (document.RootElement.ValueKind is not JsonValueKind.Object)
            {
                return Invalid("theme.json.root", "主题 JSON 根节点必须是对象。");
            }

            if (!document.RootElement.TryGetProperty("schemaVersion", out var schemaElement) ||
                !schemaElement.TryGetInt32(out var schemaVersion))
            {
                return Invalid("theme.schema_version.missing", "主题缺少有效的 schemaVersion。");
            }

            if (schemaVersion > ThemePackageContractValidator.CurrentSchemaVersion)
            {
                return new ThemeDocumentReadResult(
                    ThemeDocumentReadStatus.UnsupportedNewerSchema,
                    schemaVersion,
                    null,
                    [
                        new ValidationIssue(
                            "theme.schema_version.newer",
                            $"主题使用较新的 Schema v{schemaVersion}，当前版本只读报告且不会覆盖。",
                            ValidationSeverity.Error,
                            "schemaVersion"),
                    ]);
            }

            if (schemaVersion != ThemePackageContractValidator.CurrentSchemaVersion)
            {
                return Invalid(
                    "theme.schema_version.unsupported",
                    $"不支持 Schema v{schemaVersion}。",
                    schemaVersion);
            }

            var theme = JsonSerializer.Deserialize<ThemePackage>(
                utf8Json.Span,
                serializerOptions);
            if (theme is null)
            {
                return Invalid("theme.json.empty", "主题 JSON 无法解析。", schemaVersion);
            }

            var issues = ThemePackageContractValidator.Validate(theme);
            if (issues.Count > 0)
            {
                return new ThemeDocumentReadResult(
                    ThemeDocumentReadStatus.Invalid,
                    schemaVersion,
                    null,
                    issues);
            }

            return new ThemeDocumentReadResult(
                ThemeDocumentReadStatus.Success,
                schemaVersion,
                theme,
                Array.Empty<ValidationIssue>());
        }
        catch (JsonException)
        {
            return Invalid(
                "theme.json.invalid",
                "主题 JSON 格式无效或包含未知字段。");
        }
    }

    public OperationResult<byte[]> Serialize(ThemePackage theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var issues = ThemePackageContractValidator.Validate(theme);
        if (issues.Count > 0)
        {
            return OperationResult<byte[]>.Failure(
                OperationErrorCode.ValidationFailed,
                issues[0].UserMessage,
                issues[0].Code);
        }

        return OperationResult<byte[]>.Success(
            JsonSerializer.SerializeToUtf8Bytes(theme, serializerOptions));
    }

    private static ThemeDocumentReadResult Invalid(
        string code,
        string userMessage,
        int? schemaVersion = null) =>
        new(
            ThemeDocumentReadStatus.Invalid,
            schemaVersion,
            null,
            [
                new ValidationIssue(
                    code,
                    userMessage,
                    ValidationSeverity.Error),
            ]);

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = true,
        };
        options.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
