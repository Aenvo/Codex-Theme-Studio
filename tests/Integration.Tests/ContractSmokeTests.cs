namespace CodexThemeStudio.Integration.Tests;

using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Results;

public class ContractSmokeTests
{
    [Fact]
    public void RequiredServiceContracts_ArePublicInterfaces()
    {
        Type[] requiredContracts =
        [
            typeof(IThemeRepository),
            typeof(IThemeAssetStore),
            typeof(IStorageLocationService),
            typeof(IStorageConsistencyService),
            typeof(IImagePipeline),
            typeof(IThemeImageImportService),
            typeof(ICodexDiscoveryService),
            typeof(ICodexInspectorService),
            typeof(ICodexThemeRuntime),
            typeof(IPersistenceService),
            typeof(IApplicationStatusService),
        ];

        Assert.All(
            requiredContracts,
            contract =>
            {
                Assert.True(contract.IsPublic);
                Assert.True(contract.IsInterface);
            });
    }

    [Fact]
    public void OperationFailure_ContainsStableCodeAndUserMessage()
    {
        var result = OperationResult.Failure(
            OperationErrorCode.CodexNotFound,
            "尚未连接 Codex。",
            "codex.discovery.not_found");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(OperationErrorCode.CodexNotFound, result.Error.Code);
        Assert.Equal("尚未连接 Codex。", result.Error.UserMessage);
        Assert.Equal("codex.discovery.not_found", result.Error.DiagnosticCode);
    }
}
