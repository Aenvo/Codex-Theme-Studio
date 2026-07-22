using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Contracts.Interfaces;

public interface IStorageLocationService
{
    Task<OperationResult<StorageLocationStatus>> InitializeAsync(
        string defaultDataRoot,
        CancellationToken cancellationToken);

    Task<OperationResult<StorageLocationStatus>> GetStatusAsync(
        CancellationToken cancellationToken);

    Task<OperationResult<StorageLocationStatus>> MigrateAsync(
        string destinationDataRoot,
        CancellationToken cancellationToken);

    StorageMigrationReport? LastMigrationReport { get; }
}
