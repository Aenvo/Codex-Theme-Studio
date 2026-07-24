using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Contracts.Interfaces;

public interface IDiagnosticEventSink
{
    Task<OperationResult> WriteAsync(
        DiagnosticEvent diagnosticEvent,
        CancellationToken cancellationToken);

    OperationResult WriteCritical(DiagnosticEvent diagnosticEvent);
}

public interface IDiagnosticQueryService
{
    Task<OperationResult<DiagnosticSnapshot>> ReadAsync(
        int maximumEventGroups,
        CancellationToken cancellationToken);
}

public interface IDiagnosticBundleService
{
    Task<OperationResult<DiagnosticIssueDraft>> CreateIssueDraftAsync(
        DiagnosticIssueContext context,
        CancellationToken cancellationToken);

    Task<OperationResult<DiagnosticBundleResult>> ExportAsync(
        string destinationPath,
        DiagnosticIssueContext context,
        CancellationToken cancellationToken);
}
