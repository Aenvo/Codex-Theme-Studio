namespace CodexThemeStudio.Contracts.Results;

public enum OperationErrorCode
{
    None = 0,
    ValidationFailed,
    NotFound,
    Conflict,
    Cancelled,
    Timeout,
    AccessDenied,
    InvalidPath,
    StorageUnavailable,
    CodexNotFound,
    CodexIdentityMismatch,
    UnsupportedVersion,
    InspectorUnavailable,
    PortInUse,
    IdentityChanged,
    ProtocolRejected,
    ProcessExited,
    InvalidResponse,
    ExternalToolFailure,
    NotImplemented,
    InternalError,
}
