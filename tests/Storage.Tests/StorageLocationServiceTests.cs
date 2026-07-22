using CodexThemeStudio.Contracts.Results;
using CodexThemeStudio.Storage;

namespace CodexThemeStudio.Storage.Tests;

public class StorageLocationServiceTests
{
    [Fact]
    public async Task Initialize_CreatesLayoutAndAtomicBootstrap()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();

        foreach (var relativeDirectory in StorageLayout.RequiredDirectories)
        {
            Assert.True(
                Directory.Exists(
                    Path.Combine(environment.DataRoot, relativeDirectory)));
        }

        Assert.True(File.Exists(environment.BootstrapPath));

        var service = new StorageLocationService(environment.BootstrapPath);
        var status = await service.GetStatusAsync(CancellationToken.None);

        Assert.True(status.IsSuccess);
        Assert.True(status.Value!.IsAvailable);
        Assert.True(status.Value.IsWritable);
        Assert.Equal(
            Path.GetFullPath(environment.DataRoot),
            status.Value.DataRoot);
    }

    [Fact]
    public async Task GetStatus_RejectsCorruptedBootstrapWithoutReplacingIt()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        const string corrupted = """{"schemaVersion":1,"dataRoot":42}""";
        await File.WriteAllTextAsync(environment.BootstrapPath, corrupted);
        var service = new StorageLocationService(environment.BootstrapPath);

        var status = await service.GetStatusAsync(CancellationToken.None);

        Assert.False(status.IsSuccess);
        Assert.Equal(OperationErrorCode.ValidationFailed, status.Error!.Code);
        Assert.Equal(
            corrupted,
            await File.ReadAllTextAsync(environment.BootstrapPath));
    }

    [Fact]
    public async Task GetStatus_ExternalDataRootOffline_DoesNotCreateReplacement()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var detached = Path.Combine(environment.ContainerRoot, "detached-data");
        Directory.Move(environment.DataRoot, detached);
        var service = new StorageLocationService(environment.BootstrapPath);

        var status = await service.GetStatusAsync(CancellationToken.None);

        Assert.True(status.IsSuccess);
        Assert.False(status.Value!.IsAvailable);
        Assert.Contains("不会创建新的空库", status.Value.UserMessage, StringComparison.Ordinal);
        Assert.False(Directory.Exists(environment.DataRoot));
        Assert.True(Directory.Exists(detached));
    }

    [Fact]
    public async Task Migrate_CopiesVerifiesSwitchesAndRetainsSource()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var service = new StorageLocationService(environment.BootstrapPath);
        var theme = StorageTestEnvironment.CreateTheme("迁移验证");
        var repository = environment.CreateRepository();
        Assert.True((await repository.SaveAsync(theme, null, CancellationToken.None)).IsSuccess);
        await File.WriteAllBytesAsync(
            Path.Combine(environment.DataRoot, StorageLayout.CacheDirectory, "sample.bin"),
            [1, 2, 3, 4]);
        var destination = Path.Combine(environment.ContainerRoot, "destination");

        var result = await service.MigrateAsync(
            destination,
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.DiagnosticCode);
        Assert.Equal(Path.GetFullPath(destination), result.Value!.DataRoot);
        Assert.True(Directory.Exists(environment.DataRoot));
        Assert.True(File.Exists(Path.Combine(destination, StorageLayout.GetThemeDocument(theme.Id))));
        Assert.True(service.LastMigrationReport!.DatabaseIntegrityVerified);
        Assert.True(service.LastMigrationReport.SourceRetained);
        Assert.Equal(
            destination,
            (await service.GetStatusAsync(CancellationToken.None)).Value!.DataRoot);
    }

    [Fact]
    public async Task Migrate_DiskInsufficientOrReadOnly_DoesNotSwitchBootstrap()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var originalBootstrap = await File.ReadAllTextAsync(environment.BootstrapPath);
        var destination = Path.Combine(environment.ContainerRoot, "destination");

        var diskFull = new StorageLocationService(
            environment.BootstrapPath,
            _ => true,
            _ => 0);
        var diskResult = await diskFull.MigrateAsync(destination, CancellationToken.None);
        Assert.False(diskResult.IsSuccess);
        Assert.Equal("storage.migration.disk_full", diskResult.Error!.DiagnosticCode);
        Assert.Equal(originalBootstrap, await File.ReadAllTextAsync(environment.BootstrapPath));

        var readOnly = new StorageLocationService(
            environment.BootstrapPath,
            _ => false,
            _ => long.MaxValue);
        var readOnlyResult = await readOnly.MigrateAsync(
            Path.Combine(environment.ContainerRoot, "read-only"),
            CancellationToken.None);
        Assert.False(readOnlyResult.IsSuccess);
        Assert.Equal(OperationErrorCode.AccessDenied, readOnlyResult.Error!.Code);
        Assert.Equal(originalBootstrap, await File.ReadAllTextAsync(environment.BootstrapPath));
    }

    [Fact]
    public async Task Migrate_Cancelled_KeepsOldDataRoot()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var service = new StorageLocationService(environment.BootstrapPath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await service.MigrateAsync(
            Path.Combine(environment.ContainerRoot, "cancelled"),
            cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.Cancelled, result.Error!.Code);
        Assert.Equal(
            environment.DataRoot,
            (await service.GetStatusAsync(CancellationToken.None)).Value!.DataRoot);
    }
}
