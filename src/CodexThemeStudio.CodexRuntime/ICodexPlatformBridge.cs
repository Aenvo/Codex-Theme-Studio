using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.CodexRuntime;

public interface ICodexPlatformBridge
{
    Task<CodexPlatformBridgeResult> ExecuteAsync(
        CodexPlatformRequest request,
        CancellationToken cancellationToken);
}

public sealed record CodexPlatformFailure(string Code, string Stage);

public sealed record CodexPlatformBridgeResult(
    OperationResult<CodexPlatformResponse> Operation,
    CodexPlatformFailure? PrimaryError,
    CodexPlatformFailure? RecoveryError)
{
    public bool IsSuccess => Operation.IsSuccess;

    public CodexPlatformResponse? Value => Operation.Value;

    public OperationError? Error => Operation.Error;

    public static implicit operator OperationResult<CodexPlatformResponse>(
        CodexPlatformBridgeResult value) =>
        value.Operation;

    public static CodexPlatformBridgeResult Success(
        CodexPlatformResponse response) =>
        new(
            OperationResult<CodexPlatformResponse>.Success(response),
            null,
            null);

    public static CodexPlatformBridgeResult Failure(
        OperationErrorCode code,
        string userMessage,
        string diagnosticCode,
        string stage,
        CodexPlatformFailure? recoveryError = null) =>
        new(
            OperationResult<CodexPlatformResponse>.Failure(
                code,
                userMessage,
                diagnosticCode),
            new CodexPlatformFailure(diagnosticCode, stage),
            recoveryError);
}

public interface ICodexQualificationStore
{
    Task<bool> IsQualifiedAsync(
        string installationFingerprint,
        int protocolVersion,
        CancellationToken cancellationToken);

    Task WriteQualifiedAsync(
        string installationFingerprint,
        int protocolVersion,
        string toolVersion,
        CancellationToken cancellationToken);
}
