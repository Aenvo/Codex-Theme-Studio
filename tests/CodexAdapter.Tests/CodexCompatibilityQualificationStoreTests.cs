using CodexThemeStudio.CodexAdapter;
using CodexThemeStudio.Contracts.Models;

namespace CodexThemeStudio.CodexAdapter.Tests;

public sealed class CodexCompatibilityQualificationStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "cts-qualification-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Qualify_PersistsAndCanBeReadByANewStoreInstance()
    {
        var path = Path.Combine(root, "compatibility-qualifications.json");
        var hash = new string('a', 64);
        var writer = new CodexCompatibilityQualificationStore(path);

        var qualified = await writer.QualifyAsync(
            hash,
            DateTimeOffset.Parse("2026-07-24T06:00:00Z"),
            CancellationToken.None);
        var reader = new CodexCompatibilityQualificationStore(path);
        var result = await reader.IsQualifiedAsync(hash, CancellationToken.None);

        Assert.True(qualified.IsSuccess, qualified.Error?.DiagnosticCode);
        Assert.True(result.IsSuccess, result.Error?.DiagnosticCode);
        Assert.True(result.Value, "The persisted fingerprint was not found.");
    }

    [Fact]
    public async Task V1_PreservesPersistenceQualificationButDoesNotBecomeStartupCache()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "compatibility-qualifications.json");
        var hash = new string('b', 64);
        await File.WriteAllTextAsync(
            path,
            $$"""
              {"schemaVersion":1,"qualifiedExecutableSha256":["{{hash}}"],"updatedAtUtc":"2026-07-24T06:00:00Z"}
              """);
        var store = new CodexCompatibilityQualificationStore(path);

        var qualified = await store.IsQualifiedAsync(hash, CancellationToken.None);
        var cached = await store.FindCompatibleAsync(
            Installation(hash),
            CancellationToken.None);

        Assert.True(qualified.IsSuccess);
        Assert.True(qualified.Value);
        Assert.True(cached.IsSuccess);
        Assert.Null(cached.Value);
    }

    [Fact]
    public async Task V1_PascalCaseWrittenByPreviousVersion_IsMigratable()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "compatibility-qualifications.json");
        var hash = new string('9', 64);
        await File.WriteAllTextAsync(
            path,
            $$"""
              {"SchemaVersion":1,"QualifiedExecutableSha256":["{{hash}}"],"UpdatedAtUtc":"2026-07-24T06:00:00Z"}
              """);
        var store = new CodexCompatibilityQualificationStore(path);

        var qualified = await store.IsQualifiedAsync(hash, CancellationToken.None);
        var write = await store.CacheCapabilityAsync(
            CompatibleRecord(hash),
            CancellationToken.None);

        Assert.True(qualified.IsSuccess && qualified.Value);
        Assert.True(write.IsSuccess, write.Error?.DiagnosticCode);
        Assert.Contains(
            "\"schemaVersion\":2",
            await File.ReadAllTextAsync(path),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CapabilityCache_RequiresExactBuildAndProbeContract()
    {
        var hash = new string('c', 64);
        var record = CompatibleRecord(hash);
        var store =
            CodexCompatibilityQualificationStore.CreateWithRecordsInMemory(record);

        var matching = await store.FindCompatibleAsync(
            Installation(hash),
            CancellationToken.None);
        var updatedVersion = await store.FindCompatibleAsync(
            Installation(hash) with { Version = "2.0.0" },
            CancellationToken.None);
        var updatedHash = await store.FindCompatibleAsync(
            Installation(new string('d', 64)),
            CancellationToken.None);
        var sourceNotAcknowledged = await store.FindCompatibleAsync(
            Installation(hash) with { SourceAcknowledged = false },
            CancellationToken.None);
        var staleContractStore =
            CodexCompatibilityQualificationStore.CreateWithRecordsInMemory(
                record with
                {
                    ProbeContractVersion =
                        CodexCompatibilityQualificationStore.CurrentProbeContractVersion - 1,
                });
        var staleContract = await staleContractStore.FindCompatibleAsync(
            Installation(hash),
            CancellationToken.None);

        Assert.NotNull(matching.Value);
        Assert.Null(updatedVersion.Value);
        Assert.Null(updatedHash.Value);
        Assert.Null(sourceNotAcknowledged.Value);
        Assert.Null(staleContract.Value);
        Assert.False(
            (await store.IsQualifiedAsync(hash, CancellationToken.None)).Value);
    }

    [Fact]
    public async Task CapabilityCache_PersistsSchemaV2WithoutGrantingPersistence()
    {
        var path = Path.Combine(root, "compatibility-qualifications.json");
        var hash = new string('f', 64);
        var store = new CodexCompatibilityQualificationStore(path);

        var write = await store.CacheCapabilityAsync(
            CompatibleRecord(hash),
            CancellationToken.None);
        var document = await File.ReadAllTextAsync(path);
        var reloaded = new CodexCompatibilityQualificationStore(path);

        Assert.True(write.IsSuccess, write.Error?.DiagnosticCode);
        Assert.Contains("\"schemaVersion\":2", document, StringComparison.Ordinal);
        Assert.Contains("\"probeContractVersion\":2", document, StringComparison.Ordinal);
        Assert.NotNull(
            (await reloaded.FindCompatibleAsync(
                Installation(hash),
                CancellationToken.None)).Value);
        Assert.False(
            (await reloaded.IsQualifiedAsync(hash, CancellationToken.None)).Value);
    }

    [Fact]
    public async Task UnknownSchema_FailsClosedAndIsNotOverwritten()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "compatibility-qualifications.json");
        await File.WriteAllTextAsync(path, """{"schemaVersion":99}""");
        var original = await File.ReadAllTextAsync(path);
        var store = new CodexCompatibilityQualificationStore(path);

        var result = await store.FindCompatibleAsync(
            Installation(new string('e', 64)),
            CancellationToken.None);
        var write = await store.CacheCapabilityAsync(
            CompatibleRecord(new string('e', 64)),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.False(write.IsSuccess);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    private static CodexInstallationInfo Installation(string hash) =>
        new(
            "OpenAI.Codex_2p2nqsd0c76g0",
            "OpenAI.Codex_1.0.0_x64__2p2nqsd0c76g0",
            "1.0.0",
            @"C:\Codex\ChatGPT.exe",
            ExecutableSha256: hash);

    private static CodexCompatibilityQualificationStore.QualificationRecord
        CompatibleRecord(string hash) =>
        new(
            hash,
            "1.0.0",
            "OpenAI.Codex_1.0.0_x64__2p2nqsd0c76g0",
            @"C:\Codex\ChatGPT.exe",
            CodexInstallationSource.StoreAutomatic,
            CodexIdentityAssessment.TrustedStore,
            CodexCompatibilityLevel.Verified,
            CodexCompatibilityQualificationStore.CurrentProbeContractVersion,
            CodexCompatibilityQualificationStore.CurrentRequiredCapabilitiesVersion,
            DateTimeOffset.Parse("2026-07-24T06:00:00Z"),
            true,
            true,
            false);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
