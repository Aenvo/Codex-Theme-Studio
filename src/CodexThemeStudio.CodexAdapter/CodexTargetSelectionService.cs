using System.Security.Cryptography;
using System.Text.Json;
using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.CodexAdapter;

public sealed class CodexTargetSelectionService : ICodexTargetSelectionService
{
    private readonly string configurationPath;
    private readonly SemaphoreSlim gate = new(1, 1);

    public CodexTargetSelectionService(string? configurationPath = null)
    {
        this.configurationPath = configurationPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexThemeStudio",
            "Runtime",
            "codex-target.json");
    }

    public async Task<OperationResult<CodexTargetStatus>> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        var selection = await ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!selection.IsSuccess)
        {
            return OperationResult<CodexTargetStatus>.Failure(selection.Error!);
        }

        if (selection.Value is null)
        {
            return OperationResult<CodexTargetStatus>.Success(AutomaticStatus());
        }

        var current = await RefreshFingerprintAsync(selection.Value, cancellationToken)
            .ConfigureAwait(false);
        return current.IsSuccess
            ? OperationResult<CodexTargetStatus>.Success(ToStatus(current.Value!))
            : OperationResult<CodexTargetStatus>.Failure(current.Error!);
    }

    public async Task<OperationResult<CodexTargetStatus>> SelectAsync(
        string executablePath,
        bool acknowledgeUnverifiedSource,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executablePath) ||
            !Path.IsPathFullyQualified(executablePath) ||
            !string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(executablePath))
        {
            return OperationResult<CodexTargetStatus>.Failure(
                OperationErrorCode.ValidationFailed,
                "请选择一个存在的 Codex 可执行文件。",
                "codex.target.invalid_executable");
        }

        var fullPath = Path.GetFullPath(executablePath);
        var fingerprint = await ComputeSha256Async(fullPath, cancellationToken)
            .ConfigureAwait(false);
        var selection = new CodexTargetSelection(
            CodexTargetSelection.CurrentSchemaVersion,
            fullPath,
            fingerprint,
            acknowledgeUnverifiedSource ? fingerprint : null);
        var write = await WriteAsync(selection, cancellationToken).ConfigureAwait(false);
        return write.IsSuccess
            ? OperationResult<CodexTargetStatus>.Success(ToStatus(selection))
            : OperationResult<CodexTargetStatus>.Failure(write.Error!);
    }

    public async Task<OperationResult<CodexTargetStatus>> ResetAsync(
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(configurationPath))
            {
                var backup = configurationPath + ".automatic-" +
                    DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
                File.Move(configurationPath, backup);
            }

            return OperationResult<CodexTargetStatus>.Success(AutomaticStatus());
        }
        catch (IOException)
        {
            return IoFailure("codex.target.reset_failed");
        }
        catch (UnauthorizedAccessException)
        {
            return IoFailure("codex.target.reset_denied");
        }
        finally
        {
            gate.Release();
        }
    }

    internal async Task<OperationResult<CodexTargetSelection?>> ResolveAsync(
        CancellationToken cancellationToken)
    {
        var read = await ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess || read.Value is null)
        {
            return read;
        }

        if (!File.Exists(read.Value.ExecutablePath))
        {
            return OperationResult<CodexTargetSelection?>.SuccessOptional(null);
        }

        var refreshed = await RefreshFingerprintAsync(read.Value, cancellationToken)
            .ConfigureAwait(false);
        if (!refreshed.IsSuccess)
        {
            return OperationResult<CodexTargetSelection?>.Failure(refreshed.Error!);
        }

        return OperationResult<CodexTargetSelection?>.Success(refreshed.Value);
    }

    private async Task<OperationResult<CodexTargetSelection>> RefreshFingerprintAsync(
        CodexTargetSelection selection,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(selection.ExecutablePath))
        {
            return OperationResult<CodexTargetSelection>.Failure(
                OperationErrorCode.CodexNotFound,
                "已选择的 Codex 可执行文件不存在；将尝试自动检测 Store 版本。",
                "codex.target.missing");
        }

        var fingerprint = await ComputeSha256Async(selection.ExecutablePath, cancellationToken)
            .ConfigureAwait(false);
        if (string.Equals(fingerprint, selection.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<CodexTargetSelection>.Success(selection);
        }

        var changed = selection with
        {
            ExecutableSha256 = fingerprint,
            AcknowledgedSha256 = null,
        };
        var write = await WriteAsync(changed, cancellationToken).ConfigureAwait(false);
        return write.IsSuccess
            ? OperationResult<CodexTargetSelection>.Success(changed)
            : OperationResult<CodexTargetSelection>.Failure(write.Error!);
    }

    private async Task<OperationResult<CodexTargetSelection?>> ReadAsync(
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(configurationPath))
            {
                return OperationResult<CodexTargetSelection?>.SuccessOptional(null);
            }

            await using var stream = new FileStream(
                configurationPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var selection = await JsonSerializer.DeserializeAsync<CodexTargetSelection>(
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (selection is null ||
                selection.SchemaVersion != CodexTargetSelection.CurrentSchemaVersion ||
                !Path.IsPathFullyQualified(selection.ExecutablePath))
            {
                return OperationResult<CodexTargetSelection?>.Failure(
                    OperationErrorCode.ValidationFailed,
                    "Codex 目标配置无效，请恢复自动检测后重新选择。",
                    "codex.target.config_invalid");
            }

            return OperationResult<CodexTargetSelection?>.Success(selection);
        }
        catch (JsonException)
        {
            return OperationResult<CodexTargetSelection?>.Failure(
                OperationErrorCode.ValidationFailed,
                "Codex 目标配置无法读取，请恢复自动检测后重新选择。",
                "codex.target.config_invalid");
        }
        catch (IOException)
        {
            return OperationResult<CodexTargetSelection?>.Failure(
                OperationErrorCode.StorageUnavailable,
                "无法读取 Codex 目标配置。",
                "codex.target.read_failed");
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<OperationResult> WriteAsync(
        CodexTargetSelection selection,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(configurationPath)!;
            Directory.CreateDirectory(directory);
            var temporary = configurationPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await using (var stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        selection,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                File.Move(temporary, configurationPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }

            return OperationResult.Success();
        }
        catch (IOException)
        {
            return OperationResult.Failure(
                OperationErrorCode.StorageUnavailable,
                "无法保存 Codex 目标配置。",
                "codex.target.write_failed");
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResult.Failure(
                OperationErrorCode.AccessDenied,
                "没有权限保存 Codex 目标配置。",
                "codex.target.write_denied");
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static CodexTargetStatus AutomaticStatus() =>
        new(false, null, null, true, "正在自动检测 Microsoft Store Codex。");

    private static CodexTargetStatus ToStatus(CodexTargetSelection selection) =>
        new(
            true,
            selection.ExecutablePath,
            selection.ExecutableSha256,
            string.Equals(
                selection.ExecutableSha256,
                selection.AcknowledgedSha256,
                StringComparison.OrdinalIgnoreCase),
            "正在使用用户选择的 Codex 可执行文件。");

    private static OperationResult<CodexTargetStatus> IoFailure(string diagnosticCode) =>
        OperationResult<CodexTargetStatus>.Failure(
            OperationErrorCode.StorageUnavailable,
            "无法更新 Codex 目标配置。",
            diagnosticCode);
}

internal sealed record CodexTargetSelection(
    int SchemaVersion,
    string ExecutablePath,
    string ExecutableSha256,
    string? AcknowledgedSha256)
{
    public const int CurrentSchemaVersion = 1;
}
