using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.CodexRuntime;

public interface ICodexPlatformBridge
{
    Task<OperationResult<CodexPlatformResponse>> ExecuteAsync(
        CodexPlatformRequest request,
        CancellationToken cancellationToken);
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
