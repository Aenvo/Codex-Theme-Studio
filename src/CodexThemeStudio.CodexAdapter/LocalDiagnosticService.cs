using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;
using DiagnosticSource = CodexThemeStudio.Contracts.Models.DiagnosticSource;

namespace CodexThemeStudio.CodexAdapter;

public sealed partial class LocalDiagnosticService :
    IDiagnosticEventSink,
    IDiagnosticQueryService,
    IDiagnosticBundleService
{
    private const int MaximumArchives = 2;
    private const int MaximumDiagnosticLines = 20_000;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string logDirectory;
    private readonly TimeProvider timeProvider;
    private readonly long maximumLogBytes;
    private string? lastWriteFailureCode;

    public LocalDiagnosticService(
        string logDirectory,
        TimeProvider? timeProvider = null,
        long maximumLogBytes = 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        if (!Path.IsPathFullyQualified(logDirectory))
        {
            throw new ArgumentException(
                "The diagnostic log directory must be absolute.",
                nameof(logDirectory));
        }

        if (maximumLogBytes < 512)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLogBytes));
        }

        this.logDirectory = Path.GetFullPath(logDirectory);
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.maximumLogBytes = maximumLogBytes;
    }

    public static string GetDefaultLogDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexThemeStudio",
            "Logs");

    public async Task<OperationResult> WriteAsync(
        DiagnosticEvent diagnosticEvent,
        CancellationToken cancellationToken)
    {
        var validated = ValidateEvent(diagnosticEvent);
        if (!validated.IsSuccess)
        {
            return validated;
        }

        var path = GetLogPath(diagnosticEvent.Source);
        var gate = FileLocks.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Failure(
                OperationErrorCode.Cancelled,
                "诊断事件写入已取消。",
                "diagnostics.write_cancelled");
        }

        try
        {
            Directory.CreateDirectory(logDirectory);
            RotateIfRequired(path);
            var json = JsonSerializer.Serialize(diagnosticEvent, JsonOptions);
            await File.AppendAllTextAsync(
                path,
                $"{json}{Environment.NewLine}",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken).ConfigureAwait(false);
            lastWriteFailureCode = null;
            return OperationResult.Success();
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Failure(
                OperationErrorCode.Cancelled,
                "诊断事件写入已取消。",
                "diagnostics.write_cancelled");
        }
        catch (UnauthorizedAccessException)
        {
            lastWriteFailureCode = "diagnostics.write_access_denied";
            return OperationResult.Failure(
                OperationErrorCode.AccessDenied,
                "诊断日志目录不可写。",
                lastWriteFailureCode);
        }
        catch (IOException)
        {
            lastWriteFailureCode = "diagnostics.write_failed";
            return OperationResult.Failure(
                OperationErrorCode.StorageUnavailable,
                "诊断日志写入失败。",
                lastWriteFailureCode);
        }
        finally
        {
            gate.Release();
        }
    }

    public OperationResult WriteCritical(DiagnosticEvent diagnosticEvent)
    {
        var validated = ValidateEvent(diagnosticEvent);
        if (!validated.IsSuccess)
        {
            return validated;
        }

        var path = GetLogPath(diagnosticEvent.Source);
        var gate = FileLocks.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        if (!gate.Wait(TimeSpan.FromMilliseconds(250)))
        {
            lastWriteFailureCode = "diagnostics.write_busy";
            return OperationResult.Failure(
                OperationErrorCode.Timeout,
                "诊断日志正忙，无法记录致命事件。",
                lastWriteFailureCode);
        }

        try
        {
            Directory.CreateDirectory(logDirectory);
            RotateIfRequired(path);
            var bytes = Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(diagnosticEvent, JsonOptions) +
                Environment.NewLine);
            using var stream = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                16 * 1024,
                FileOptions.WriteThrough);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
            lastWriteFailureCode = null;
            return OperationResult.Success();
        }
        catch (UnauthorizedAccessException)
        {
            lastWriteFailureCode = "diagnostics.write_access_denied";
            return OperationResult.Failure(
                OperationErrorCode.AccessDenied,
                "诊断日志目录不可写。",
                lastWriteFailureCode);
        }
        catch (IOException)
        {
            lastWriteFailureCode = "diagnostics.write_failed";
            return OperationResult.Failure(
                OperationErrorCode.StorageUnavailable,
                "诊断日志写入失败。",
                lastWriteFailureCode);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OperationResult<DiagnosticSnapshot>> ReadAsync(
        int maximumEventGroups,
        CancellationToken cancellationToken)
    {
        if (maximumEventGroups is <= 0 or > 200)
        {
            return OperationResult<DiagnosticSnapshot>.Failure(
                OperationErrorCode.ValidationFailed,
                "诊断事件显示数量无效。",
                "diagnostics.read.maximum_invalid");
        }

        var read = await ReadEventsAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult<DiagnosticSnapshot>.Success(
            CreateSnapshot(read, maximumEventGroups));
    }

    public async Task<OperationResult<DiagnosticIssueDraft>> CreateIssueDraftAsync(
        DiagnosticIssueContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var read = await ReadEventsAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = CreateSnapshot(read, 20);
        var reportId = Guid.NewGuid();
        return OperationResult<DiagnosticIssueDraft>.Success(
            new DiagnosticIssueDraft(
                reportId,
                CreateIssueMarkdown(reportId, context, snapshot),
                snapshot));
    }

    public async Task<OperationResult<DiagnosticBundleResult>> ExportAsync(
        string destinationPath,
        DiagnosticIssueContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(destinationPath) ||
            !Path.IsPathFullyQualified(destinationPath) ||
            !string.Equals(
                Path.GetExtension(destinationPath),
                ".zip",
                StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<DiagnosticBundleResult>.Failure(
                OperationErrorCode.InvalidPath,
                "诊断包目标必须是绝对 ZIP 路径。",
                "diagnostics.bundle.path_invalid");
        }

        var destination = Path.GetFullPath(destinationPath);
        var parent = Path.GetDirectoryName(destination)!;
        if (!Directory.Exists(parent))
        {
            return OperationResult<DiagnosticBundleResult>.Failure(
                OperationErrorCode.NotFound,
                "诊断包目标目录不存在。",
                "diagnostics.bundle.directory_missing");
        }

        var read = await ReadEventsAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = CreateSnapshot(read, 20);
        var reportId = Guid.NewGuid();
        var markdown = CreateIssueMarkdown(reportId, context, snapshot);
        var diagnosticLines = string.Join(
            Environment.NewLine,
            read.Events.Select(item => JsonSerializer.Serialize(item, JsonOptions)));
        if (diagnosticLines.Length > 0)
        {
            diagnosticLines += Environment.NewLine;
        }

        var environment = JsonSerializer.Serialize(
            new
            {
                schemaVersion = 1,
                reportId,
                generatedAtUtc = timeProvider.GetUtcNow(),
                appVersion = SanitizeToken(context.AppVersion, 64) ?? "unknown",
                codexVersion = SanitizeToken(context.CodexVersion, 128),
                codexExecutableSha256 = SanitizeSha256(context.CodexExecutableSha256),
                runtimeStatus = SanitizeToken(context.RuntimeStatus, 64) ?? "unknown",
                operatingSystem = RuntimeInformation.OSDescription,
                framework = RuntimeInformation.FrameworkDescription,
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            },
            JsonOptions);

        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["issue-summary.md"] = Encoding.UTF8.GetBytes(markdown),
            ["diagnostics.jsonl"] = Encoding.UTF8.GetBytes(diagnosticLines),
            ["environment.json"] = Encoding.UTF8.GetBytes(environment + Environment.NewLine),
        };
        var checksums = string.Join(
            Environment.NewLine,
            entries.Select(item =>
                $"{Convert.ToHexString(SHA256.HashData(item.Value)).ToLowerInvariant()}  {item.Key}")) +
            Environment.NewLine;
        entries["SHA256SUMS.txt"] = Encoding.ASCII.GetBytes(checksums);

        var temporary = Path.Combine(
            parent,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                using var archive = new ZipArchive(
                    stream,
                    ZipArchiveMode.Create,
                    leaveOpen: true);
                foreach (var item in entries)
                {
                    var entry = archive.CreateEntry(
                        item.Key,
                        CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await entryStream.WriteAsync(
                        item.Value,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            File.Move(temporary, destination, overwrite: true);
            var bytes = await File.ReadAllBytesAsync(
                destination,
                cancellationToken).ConfigureAwait(false);
            return OperationResult<DiagnosticBundleResult>.Success(
                new DiagnosticBundleResult(
                    reportId,
                    destination,
                    entries.Count,
                    bytes.LongLength,
                    Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
        }
        catch (OperationCanceledException)
        {
            TryDeleteTemporary(temporary);
            return OperationResult<DiagnosticBundleResult>.Failure(
                OperationErrorCode.Cancelled,
                "诊断包导出已取消。",
                "diagnostics.bundle.cancelled");
        }
        catch (UnauthorizedAccessException)
        {
            TryDeleteTemporary(temporary);
            return OperationResult<DiagnosticBundleResult>.Failure(
                OperationErrorCode.AccessDenied,
                "没有权限写入诊断包。",
                "diagnostics.bundle.access_denied");
        }
        catch (IOException)
        {
            TryDeleteTemporary(temporary);
            return OperationResult<DiagnosticBundleResult>.Failure(
                OperationErrorCode.StorageUnavailable,
                "诊断包写入失败。",
                "diagnostics.bundle.write_failed");
        }
    }

    private async Task<ReadResult> ReadEventsAsync(
        CancellationToken cancellationToken)
    {
        var events = new List<DiagnosticEvent>();
        var invalidLines = 0;
        var accessFailed = false;
        foreach (var source in Enum.GetValues<DiagnosticSource>())
        {
            foreach (var path in EnumerateLogPathsOldestFirst(source))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    var info = new FileInfo(path);
                    if (info.Length > maximumLogBytes + 64 * 1024)
                    {
                        invalidLines++;
                        continue;
                    }

                    var lines = await File.ReadAllLinesAsync(
                        path,
                        cancellationToken).ConfigureAwait(false);
                    foreach (var line in lines.TakeLast(MaximumDiagnosticLines))
                    {
                        if (TryParseEvent(line, source, out var item))
                        {
                            events.Add(item!);
                        }
                        else
                        {
                            invalidLines++;
                        }
                    }
                }
                catch (Exception exception)
                    when (exception is IOException or UnauthorizedAccessException)
                {
                    accessFailed = true;
                }
            }
        }

        events.Sort(static (left, right) =>
            left.TimestampUtc.CompareTo(right.TimestampUtc));
        return new ReadResult(
            events,
            invalidLines,
            accessFailed,
            ReadAgentWriteFailure());
    }

    private DiagnosticSnapshot CreateSnapshot(
        ReadResult read,
        int maximumEventGroups)
    {
        var groups = new List<DiagnosticEventGroup>();
        foreach (var item in read.Events)
        {
            if (groups.Count > 0 &&
                IsSameEvent(groups[^1].Event, item))
            {
                var previous = groups[^1];
                groups[^1] = previous with
                {
                    RepeatCount = previous.RepeatCount + 1,
                    LastTimestampUtc = item.TimestampUtc,
                };
            }
            else
            {
                groups.Add(new DiagnosticEventGroup(
                    item,
                    1,
                    item.TimestampUtc,
                    item.TimestampUtc));
            }
        }

        var recent = groups
            .TakeLast(maximumEventGroups)
            .Reverse()
            .ToArray();
        var latest = read.Events.LastOrDefault();
        var latestError = read.Events.LastOrDefault(item =>
            item.Level == DiagnosticLevel.Error);
        var latestRecovery = read.Events.LastOrDefault(item =>
            item.Outcome == DiagnosticOutcome.Recovered);
        var directoryAvailable = Directory.Exists(logDirectory) && !read.AccessFailed;

        DiagnosticHealth health;
        string status;
        var writeFailureCode =
            lastWriteFailureCode ?? read.PersistentWriteFailureCode;
        if (read.AccessFailed || writeFailureCode is not null)
        {
            health = DiagnosticHealth.Unavailable;
            status = "诊断日志部分不可用";
        }
        else if (latest is null)
        {
            health = DiagnosticHealth.Empty;
            status = "暂无诊断记录";
        }
        else if (latest.Level is DiagnosticLevel.Warning or DiagnosticLevel.Error)
        {
            health = DiagnosticHealth.NeedsAttention;
            status = "最近一次操作需要关注";
        }
        else
        {
            health = DiagnosticHealth.Normal;
            status = "诊断记录正常";
        }

        return new DiagnosticSnapshot(
            health,
            status,
            latest?.TimestampUtc,
            latestError,
            latestRecovery,
            recent,
            logDirectory,
            directoryAvailable,
            read.InvalidLineCount,
            writeFailureCode);
    }

    private string CreateIssueMarkdown(
        Guid reportId,
        DiagnosticIssueContext context,
        DiagnosticSnapshot snapshot)
    {
        var latestError = snapshot.LatestError is null
            ? "无"
            : $"{snapshot.LatestError.TimestampUtc:O} / " +
              $"{snapshot.LatestError.ErrorCode?.ToString() ?? "Unknown"} / " +
              $"{snapshot.LatestError.DiagnosticCode ?? "none"}";
        var rangeStart = snapshot.RecentEvents.LastOrDefault()?.FirstTimestampUtc;
        var rangeEnd = snapshot.RecentEvents.FirstOrDefault()?.LastTimestampUtc;
        return $"""
            ## Codex Theme Studio 诊断摘要

            - 报告 ID：`{reportId:D}`
            - 生成时间（UTC）：`{timeProvider.GetUtcNow():O}`
            - 应用版本：`{SanitizeToken(context.AppVersion, 64) ?? "unknown"}`
            - Codex 版本：`{SanitizeToken(context.CodexVersion, 128) ?? "unknown"}`
            - Codex EXE SHA-256：`{SanitizeSha256(context.CodexExecutableSha256) ?? "unknown"}`
            - Windows：`{RuntimeInformation.OSDescription}`
            - .NET：`{RuntimeInformation.FrameworkDescription}`
            - 架构：`{RuntimeInformation.ProcessArchitecture}`
            - 当前运行状态：`{SanitizeToken(context.RuntimeStatus, 64) ?? "unknown"}`
            - 诊断状态：`{snapshot.StatusText}`
            - 最近错误：`{latestError}`
            - 记录时间范围：`{rangeStart?.ToString("O") ?? "none"} — {rangeEnd?.ToString("O") ?? "none"}`

            > 此摘要由应用在本地生成，不包含对话、账号、Token、主题内容、绝对路径或原始异常消息。应用未自动上传任何数据。
            """;
    }

    private static OperationResult ValidateEvent(DiagnosticEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.SchemaVersion != DiagnosticEvent.CurrentSchemaVersion ||
            item.TimestampUtc == default ||
            item.SessionId == Guid.Empty ||
            !IsSafeToken(item.EventName, 96) ||
            (item.Operation is not null && !IsSafeToken(item.Operation, 96)) ||
            !IsSafeToken(item.AppVersion, 64) ||
            (item.DiagnosticCode is not null &&
                !IsSafeToken(item.DiagnosticCode, 128)) ||
            (item.CodexVersion is not null &&
                !IsSafeToken(item.CodexVersion, 128)) ||
            (item.CodexExecutableSha256 is not null &&
                SanitizeSha256(item.CodexExecutableSha256) is null) ||
            !IsSafeException(item.Exception))
        {
            return OperationResult.Failure(
                OperationErrorCode.ValidationFailed,
                "诊断事件包含不安全或无效字段。",
                "diagnostics.event.invalid");
        }

        return OperationResult.Success();
    }

    private static bool IsSafeException(DiagnosticExceptionInfo? exception)
    {
        if (exception is null)
        {
            return true;
        }

        return IsSafeTypeOrMethod(exception.Type, 256) &&
            exception.MethodFrames.Count <= 10 &&
            exception.MethodFrames.All(frame => IsSafeTypeOrMethod(frame, 384)) &&
            Regex.IsMatch(exception.StackFingerprint, "^[A-Fa-f0-9]{64}$");
    }

    private static bool IsSameEvent(DiagnosticEvent left, DiagnosticEvent right) =>
        left.Source == right.Source &&
        left.Level == right.Level &&
        left.EventName == right.EventName &&
        left.Operation == right.Operation &&
        left.Outcome == right.Outcome &&
        left.ErrorCode == right.ErrorCode &&
        left.DiagnosticCode == right.DiagnosticCode;

    private bool TryParseEvent(
        string line,
        DiagnosticSource fallbackSource,
        out DiagnosticEvent? diagnosticEvent)
    {
        diagnosticEvent = null;
        if (string.IsNullOrWhiteSpace(line) || line.Length > 32 * 1024)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("schemaVersion", out _))
            {
                var parsed = JsonSerializer.Deserialize<DiagnosticEvent>(
                    line,
                    JsonOptions);
                if (parsed is null || !ValidateEvent(parsed).IsSuccess)
                {
                    return false;
                }

                diagnosticEvent = parsed;
                return true;
            }

            if (!root.TryGetProperty("timestampUtc", out var timestampElement) ||
                !timestampElement.TryGetDateTimeOffset(out var timestamp) ||
                !root.TryGetProperty("eventName", out var eventElement) ||
                !root.TryGetProperty("code", out var codeElement))
            {
                return false;
            }

            var eventName = SanitizeToken(eventElement.GetString(), 96);
            var code = SanitizeToken(codeElement.GetString(), 128);
            if (eventName is null || code is null)
            {
                return false;
            }

            var isError = string.Equals(
                eventName,
                "error",
                StringComparison.OrdinalIgnoreCase);
            diagnosticEvent = new DiagnosticEvent(
                DiagnosticEvent.CurrentSchemaVersion,
                timestamp,
                fallbackSource,
                isError ? DiagnosticLevel.Error : DiagnosticLevel.Information,
                $"legacy.{eventName.ToLowerInvariant()}",
                "persistence.agent",
                isError ? DiagnosticOutcome.Failed : DiagnosticOutcome.State,
                CreateLegacySessionId(timestamp),
                null,
                isError ? OperationErrorCode.ExternalToolFailure : null,
                code,
                "legacy",
                null,
                null,
                null);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private string GetLogPath(DiagnosticSource source) =>
        Path.Combine(
            logDirectory,
            source == DiagnosticSource.Desktop
                ? "desktop.jsonl"
                : "agent.jsonl");

    private string? ReadAgentWriteFailure()
    {
        var stableRoot = Directory.GetParent(logDirectory)?.FullName;
        if (string.IsNullOrWhiteSpace(stableRoot))
        {
            return null;
        }

        var statePath = Path.Combine(stableRoot, "Agent", "state.json");
        try
        {
            var info = new FileInfo(statePath);
            if (!info.Exists ||
                info.Length is <= 0 or > 64 * 1024 ||
                (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllBytes(statePath));
            if (!document.RootElement.TryGetProperty(
                    "lastDiagnosticCode",
                    out var codeElement))
            {
                return null;
            }

            var code = SanitizeToken(codeElement.GetString(), 128);
            return code?.StartsWith(
                "diagnostics.",
                StringComparison.Ordinal) == true
                ? code
                : null;
        }
        catch (Exception exception)
            when (exception is IOException or
                UnauthorizedAccessException or
                JsonException)
        {
            return null;
        }
    }

    private IEnumerable<string> EnumerateLogPathsOldestFirst(
        DiagnosticSource source)
    {
        var current = GetLogPath(source);
        for (var index = MaximumArchives; index >= 1; index--)
        {
            yield return $"{current}.{index}";
        }

        yield return current;
    }

    private void RotateIfRequired(string path)
    {
        if (!File.Exists(path) ||
            new FileInfo(path).Length < maximumLogBytes)
        {
            return;
        }

        for (var index = MaximumArchives; index >= 1; index--)
        {
            var destination = $"{path}.{index}";
            var source = index == 1 ? path : $"{path}.{index - 1}";
            if (File.Exists(source))
            {
                File.Move(source, destination, overwrite: true);
            }
        }
    }

    private static void TryDeleteTemporary(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static Guid CreateLegacySessionId(DateTimeOffset timestamp)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"legacy|{timestamp:O}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static string? SanitizeToken(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        IsSafeToken(value, maximumLength)
            ? value
            : null;

    private static string? SanitizeSha256(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        Regex.IsMatch(value, "^[A-Fa-f0-9]{64}$")
            ? value.ToLowerInvariant()
            : null;

    private static bool IsSafeToken(string value, int maximumLength) =>
        value.Length is > 0 &&
        value.Length <= maximumLength &&
        SafeTokenRegex().IsMatch(value);

    private static bool IsSafeTypeOrMethod(string value, int maximumLength) =>
        value.Length is > 0 &&
        value.Length <= maximumLength &&
        SafeTypeOrMethodRegex().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeTokenRegex();

    [GeneratedRegex("^[A-Za-z0-9._+`<>\\[\\],:() -]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeTypeOrMethodRegex();

    private sealed record ReadResult(
        IReadOnlyList<DiagnosticEvent> Events,
        int InvalidLineCount,
        bool AccessFailed,
        string? PersistentWriteFailureCode);
}

public static class DiagnosticEventFactory
{
    public static DiagnosticEvent Create(
        DiagnosticSource source,
        DiagnosticLevel level,
        string eventName,
        DiagnosticOutcome outcome,
        Guid sessionId,
        string appVersion,
        string? operation = null,
        Guid? correlationId = null,
        OperationError? error = null,
        string? codexVersion = null,
        string? codexExecutableSha256 = null,
        Exception? exception = null,
        TimeProvider? timeProvider = null) =>
        new(
            DiagnosticEvent.CurrentSchemaVersion,
            (timeProvider ?? TimeProvider.System).GetUtcNow(),
            source,
            level,
            eventName,
            operation,
            outcome,
            sessionId,
            correlationId,
            error?.Code,
            error?.DiagnosticCode,
            appVersion,
            codexVersion,
            codexExecutableSha256,
            exception is null ? null : SanitizeException(exception));

    public static string GetApplicationVersion(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?.Split('+', 2)[0] ??
        assembly.GetName().Version?.ToString(3) ??
        "unknown";

    private static DiagnosticExceptionInfo SanitizeException(Exception exception)
    {
        var frames = new StackTrace(exception, fNeedFileInfo: false)
            .GetFrames()?
            .Select(frame => frame.GetMethod())
            .Where(method => method is not null)
            .Take(10)
            .Select(method =>
                $"{method!.DeclaringType?.FullName ?? "unknown"}.{method.Name}")
            .ToArray() ?? [];
        var material = string.Join(
            "|",
            new[] { exception.GetType().FullName ?? exception.GetType().Name }
                .Concat(frames));
        return new DiagnosticExceptionInfo(
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.HResult,
            frames,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))
                .ToLowerInvariant());
    }
}
