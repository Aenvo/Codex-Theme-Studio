namespace CodexThemeStudio.Contracts.Results;

public sealed record OperationResult
{
    private OperationResult(bool isSuccess, OperationError? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public OperationError? Error { get; }

    public static OperationResult Success() => new(true, null);

    public static OperationResult Failure(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new OperationResult(false, error);
    }

    public static OperationResult Failure(
        OperationErrorCode code,
        string userMessage,
        string? diagnosticCode = null) =>
        Failure(new OperationError(code, userMessage, diagnosticCode));
}

public sealed record OperationResult<T>
{
    private OperationResult(bool isSuccess, T? value, OperationError? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public OperationError? Error { get; }

    public static OperationResult<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new OperationResult<T>(true, value, null);
    }

    public static OperationResult<T> SuccessOptional(T? value) =>
        new(true, value, null);

    public static OperationResult<T> Failure(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new OperationResult<T>(false, default, error);
    }

    public static OperationResult<T> Failure(
        OperationErrorCode code,
        string userMessage,
        string? diagnosticCode = null) =>
        Failure(new OperationError(code, userMessage, diagnosticCode));
}
