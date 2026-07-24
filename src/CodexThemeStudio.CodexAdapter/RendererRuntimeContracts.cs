using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.CodexAdapter;

public interface IInjectorRendererClient
{
    Task<OperationResult<RendererRuntimeResult>> ApplyAsync(
        CodexProcessInfo process,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken);

    Task<OperationResult<RendererRuntimeResult>> GetStatusAsync(
        CodexProcessInfo process,
        CancellationToken cancellationToken);

    Task<OperationResult<RendererRuntimeResult>> CleanupAsync(
        CodexProcessInfo process,
        CancellationToken cancellationToken);

    Task<OperationResult<CodexInspectionResult>> InspectAsync(
        CodexProcessInfo process,
        CodexInspectionMode mode,
        CancellationToken cancellationToken);
}

public enum CodexInspectionMode
{
    Full = 0,
    RendererOnly,
}

public sealed record CodexInspectionResult(
    CodexProbeResult? Probe,
    RendererRuntimeResult Renderer);

public sealed record RendererRuntimeResult(
    int RuntimeVersion,
    bool Active,
    int? Generation,
    Guid? ThemeId,
    int EligibleWindows,
    int AppliedWindows,
    int AuxiliaryWindows,
    int HookCount,
    IReadOnlyList<string>? PageModes = null,
    int Failures = 0,
    int? ProcessId = null,
    TimeSpan? InspectorOpenDuration = null,
    bool InspectorWasAlreadyOpen = false,
    bool KnownExternalThemeActive = false);
