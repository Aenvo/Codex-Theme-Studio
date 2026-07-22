namespace CodexThemeStudio.Contracts.Results;

public sealed record OperationError
{
    public OperationError(
        OperationErrorCode code,
        string userMessage,
        string? diagnosticCode = null)
    {
        if (code is OperationErrorCode.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(code),
                "A failed operation must have a non-success error code.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);

        Code = code;
        UserMessage = userMessage;
        DiagnosticCode = diagnosticCode;
    }

    public OperationErrorCode Code { get; }

    public string UserMessage { get; }

    public string? DiagnosticCode { get; }
}

