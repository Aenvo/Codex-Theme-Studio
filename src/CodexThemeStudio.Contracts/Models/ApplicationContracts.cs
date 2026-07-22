namespace CodexThemeStudio.Contracts.Models;

public sealed record StorageLocationStatus(
    string DataRoot,
    bool IsAvailable,
    bool IsWritable,
    long? AvailableBytes,
    string UserMessage);

public sealed record ApplicationStatus(
    StorageLocationStatus Storage,
    ThemeRuntimeStatus Runtime,
    DateTimeOffset ObservedAtUtc);

public enum PersistenceStorageMode
{
    StableLocal = 0,
    StrictDataRoot,
}

public sealed record PersistenceOptions(
    PersistenceStorageMode StorageMode = PersistenceStorageMode.StableLocal,
    string? DataRoot = null);
