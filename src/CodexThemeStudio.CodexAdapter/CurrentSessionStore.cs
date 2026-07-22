using System.Text.Json;
using System.Text.Json.Serialization;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.CodexAdapter;

public interface ICurrentSessionStore
{
    Task<OperationResult<RuntimeSessionState>> ReadAsync(
        CancellationToken cancellationToken);

    Task<OperationResult> WriteAsync(
        RuntimeSessionState state,
        CancellationToken cancellationToken);
}

public sealed record RuntimeSessionState(
    int SchemaVersion,
    ThemeRuntimeState State,
    Guid? ThemeId,
    int? CodexProcessId,
    DateTimeOffset? CodexStartedAtUtc,
    int? Generation,
    Guid OperationId,
    DateTimeOffset UpdatedAtUtc)
{
    public const int CurrentSchemaVersion = 1;

    public static RuntimeSessionState Default(
        Guid operationId,
        DateTimeOffset updatedAtUtc) =>
        new(
            CurrentSchemaVersion,
            ThemeRuntimeState.Default,
            null,
            null,
            null,
            null,
            operationId,
            updatedAtUtc);
}

public sealed class AtomicCurrentSessionStore : ICurrentSessionStore
{
    private const int MaximumStateBytes = 16 * 1024;
    private readonly string statePath;

    public AtomicCurrentSessionStore(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        if (!Path.IsPathFullyQualified(dataRoot))
        {
            throw new ArgumentException(
                "DataRoot must be an absolute path.",
                nameof(dataRoot));
        }

        var root = Path.GetFullPath(dataRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        statePath = Path.Combine(root, "runtime", "current-session.json");
    }

    public async Task<OperationResult<RuntimeSessionState>> ReadAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(statePath))
            {
                return OperationResult<RuntimeSessionState>.Success(
                    RuntimeSessionState.Default(
                        Guid.Empty,
                        DateTimeOffset.UnixEpoch));
            }

            var info = new FileInfo(statePath);
            if (info.Length is <= 0 or > MaximumStateBytes ||
                (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return InvalidState("runtime.session.invalid_file");
            }

            var bytes = await File.ReadAllBytesAsync(statePath, cancellationToken);
            var state = JsonSerializer.Deserialize<RuntimeSessionState>(
                bytes,
                JsonOptions);
            if (state is null ||
                state.SchemaVersion != RuntimeSessionState.CurrentSchemaVersion)
            {
                return InvalidState("runtime.session.schema_unsupported");
            }

            return OperationResult<RuntimeSessionState>.Success(state);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<RuntimeSessionState>.Failure(
                OperationErrorCode.Cancelled,
                "会话状态读取已取消。",
                "runtime.session.read_cancelled");
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResult<RuntimeSessionState>.Failure(
                OperationErrorCode.AccessDenied,
                "没有权限读取当前会话状态。",
                "runtime.session.read_access_denied");
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return InvalidState("runtime.session.read_failed");
        }
    }

    public async Task<OperationResult> WriteAsync(
        RuntimeSessionState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        var directory = Path.GetDirectoryName(statePath)!;
        var temporaryPath = Path.Combine(
            directory,
            $".current-session.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                return OperationResult.Failure(
                    OperationErrorCode.InvalidPath,
                    "运行时目录不能是符号链接或 Junction。",
                    "runtime.session.directory_reparse");
            }

            var bytes = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(statePath))
            {
                File.Replace(temporaryPath, statePath, null);
            }
            else
            {
                File.Move(temporaryPath, statePath);
            }

            return OperationResult.Success();
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Failure(
                OperationErrorCode.Cancelled,
                "会话状态写入已取消。",
                "runtime.session.write_cancelled");
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResult.Failure(
                OperationErrorCode.AccessDenied,
                "没有权限写入当前会话状态。",
                "runtime.session.write_access_denied");
        }
        catch (IOException)
        {
            return OperationResult.Failure(
                OperationErrorCode.StorageUnavailable,
                "无法原子写入当前会话状态。",
                "runtime.session.write_failed");
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static OperationResult<RuntimeSessionState> InvalidState(
        string diagnosticCode) =>
        OperationResult<RuntimeSessionState>.Failure(
            OperationErrorCode.InvalidResponse,
            "当前会话状态文件无效。",
            diagnosticCode);

    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
