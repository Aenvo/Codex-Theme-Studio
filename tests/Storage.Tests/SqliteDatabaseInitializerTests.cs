using CodexThemeStudio.Contracts.Results;
using CodexThemeStudio.Storage;
using Microsoft.Data.Sqlite;

namespace CodexThemeStudio.Storage.Tests;

public class SqliteDatabaseInitializerTests
{
    [Fact]
    public async Task Initialize_IsRepeatableAndRecordsOneMigration()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var initializer = new SqliteDatabaseInitializer(environment.DataRoot);

        var secondRun = await initializer.InitializeAsync(CancellationToken.None);

        Assert.True(secondRun.IsSuccess);
        await using var connection = new SqliteConnection(
            $"Data Source={environment.DatabasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_migrations;";
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Initialize_RejectsNewerDatabaseVersion()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        await using (var connection = new SqliteConnection(
                         $"Data Source={environment.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO schema_migrations(version, applied_utc)
                VALUES (99, '2026-07-20T00:00:00.0000000+00:00');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var initializer = new SqliteDatabaseInitializer(environment.DataRoot);
        var result = await initializer.InitializeAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.UnsupportedVersion, result.Error!.Code);
    }
}

