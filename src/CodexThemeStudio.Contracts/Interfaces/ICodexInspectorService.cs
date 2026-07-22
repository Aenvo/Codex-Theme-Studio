using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Contracts.Interfaces;

public interface ICodexInspectorService
{
    Task<OperationResult<CodexProbeResult>> ProbeAsync(
        CodexProcessInfo process,
        CancellationToken cancellationToken);

    Task<OperationResult> CloseInspectorAsync(
        CodexProcessInfo process,
        CancellationToken cancellationToken);
}
