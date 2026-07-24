using System.Text.Json;
using CodexThemeStudio.CodexAdapter;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.CodexAdapter.Tests;

public sealed class OkkSkinExternalPersistenceServiceTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"CodexThemeStudio-okk-persistence-{Guid.NewGuid():N}");

    [Fact]
    public async Task Disable_PreservesUnknownStateAndCacheWhileStoppingPersistence()
    {
        var environment = CreateEnvironment();
        var service = new OkkSkinExternalPersistenceService(
            root,
            environment.Startup,
            environment.Processes);

        var result = await service.DisableAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ExternalPersistenceDisableOutcome.Success,
            result.Value!.Outcome);
        Assert.False(result.Value.Status.RequiresAction);
        Assert.Equal(1, environment.Startup.RemoveCalls);
        Assert.Equal([741], environment.Processes.StoppedProcessIds);

        using var state = JsonDocument.Parse(
            await File.ReadAllBytesAsync(Path.Combine(root, "state.json")));
        Assert.False(state.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal(
            JsonValueKind.Null,
            state.RootElement.GetProperty("appliedPid").ValueKind);
        Assert.Equal(
            "preserved",
            state.RootElement.GetProperty("unknownField").GetString());
        Assert.True(File.Exists(Path.Combine(root, "current", "theme.json")));
        Assert.True(File.Exists(Path.Combine(root, "current", "bg.jpg")));
    }

    [Fact]
    public async Task Disable_IsIdempotentAfterSuccessfulDisable()
    {
        var environment = CreateEnvironment();
        var service = new OkkSkinExternalPersistenceService(
            root,
            environment.Startup,
            environment.Processes);
        var first = await service.DisableAsync(CancellationToken.None);

        var second = await service.DisableAsync(CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(
            ExternalPersistenceDisableOutcome.Success,
            second.Value!.Outcome);
        Assert.Equal(1, environment.Startup.RemoveCalls);
        Assert.Single(environment.Processes.StoppedProcessIds);
    }

    [Fact]
    public async Task Disable_RejectsUnexpectedRunTargetBeforeChangingState()
    {
        var environment = CreateEnvironment();
        environment.Startup.Command =
            "\"C:\\Windows\\System32\\wscript.exe\" \"C:\\Unexpected\\OkkSkin.vbs\"";
        var original = await File.ReadAllBytesAsync(
            Path.Combine(root, "state.json"));
        var service = new OkkSkinExternalPersistenceService(
            root,
            environment.Startup,
            environment.Processes);

        var result = await service.DisableAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.Conflict, result.Error!.Code);
        Assert.Equal(0, environment.Startup.RemoveCalls);
        Assert.Empty(environment.Processes.StoppedProcessIds);
        Assert.Equal(
            original,
            await File.ReadAllBytesAsync(Path.Combine(root, "state.json")));
    }

    [Fact]
    public async Task Disable_ReturnsPartialWhenVerifiedAgentCannotStop()
    {
        var environment = CreateEnvironment();
        environment.Processes.StopResult = OperationResult.Failure(
            OperationErrorCode.ExternalToolFailure,
            "测试 Agent 无法停止。",
            "test.agent.stop_failed");
        var service = new OkkSkinExternalPersistenceService(
            root,
            environment.Startup,
            environment.Processes);

        var result = await service.DisableAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ExternalPersistenceDisableOutcome.Partial,
            result.Value!.Outcome);
        Assert.Contains(
            result.Value.Status.Residuals,
            message => message.Contains("无法停止", StringComparison.Ordinal));
        Assert.Null(environment.Startup.Command);
        using var state = JsonDocument.Parse(
            await File.ReadAllBytesAsync(Path.Combine(root, "state.json")));
        Assert.False(state.RootElement.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Disable_ReturnsPartialWhenStartupEntryCannotBeRemoved()
    {
        var environment = CreateEnvironment();
        environment.Startup.RemoveResult = OperationResult.Failure(
            OperationErrorCode.AccessDenied,
            "测试启动项无法移除。",
            "test.startup.access_denied");
        var service = new OkkSkinExternalPersistenceService(
            root,
            environment.Startup,
            environment.Processes);

        var result = await service.DisableAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ExternalPersistenceDisableOutcome.Partial,
            result.Value!.Outcome);
        Assert.True(result.Value.Status.IsStartupRegistered);
        Assert.Contains(
            result.Value.Status.Residuals,
            message => message.Contains("启动项", StringComparison.Ordinal));
        Assert.Equal([741], environment.Processes.StoppedProcessIds);
    }

    [Fact]
    public async Task Status_RejectsInvalidLauncherWithoutInspectingProcesses()
    {
        var environment = CreateEnvironment();
        await File.WriteAllTextAsync(
            Path.Combine(root, "OkkSkin.vbs"),
            "CreateObject(\"WScript.Shell\").Run \"unexpected\"");
        var service = new OkkSkinExternalPersistenceService(
            root,
            environment.Startup,
            environment.Processes);

        var status = await service.GetStatusAsync(CancellationToken.None);

        Assert.True(status.IsSuccess);
        Assert.False(status.Value!.CanDisableSafely);
        Assert.Contains(
            status.Value.Residuals,
            message => message.Contains("启动器", StringComparison.Ordinal));
        Assert.Equal(0, environment.Processes.FindCalls);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private TestEnvironment CreateEnvironment()
    {
        Directory.CreateDirectory(Path.Combine(root, "current"));
        var nodePath = Path.Combine(root, "tools", "node.exe");
        var agentPath = Path.Combine(root, "provider", "agent.mjs");
        Directory.CreateDirectory(Path.GetDirectoryName(nodePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(agentPath)!);
        File.WriteAllBytes(nodePath, [0x01]);
        File.WriteAllText(agentPath, "// test");
        File.WriteAllText(
            Path.Combine(root, "OkkSkin.vbs"),
            $"CreateObject(\"WScript.Shell\").Run \"\"\"{nodePath}\"\" \"\"{agentPath}\"\"\", 0, False\r\n");
        File.WriteAllText(
            Path.Combine(root, "state.json"),
            """
            {
              "skinId": "doro-q",
              "enabled": true,
              "appliedPid": 321,
              "startedAt": "2026-07-20T05:41:17.508Z",
              "unknownField": "preserved"
            }
            """);
        File.WriteAllText(
            Path.Combine(root, "current", "theme.json"),
            """{"schemaVersion":1,"id":"doro-q"}""");
        File.WriteAllBytes(
            Path.Combine(root, "current", "bg.jpg"),
            [0x01, 0x02, 0x03]);
        var command =
            $"\"{Path.Combine(Environment.SystemDirectory, "wscript.exe")}\" " +
            $"\"{Path.Combine(root, "OkkSkin.vbs")}\"";
        return new TestEnvironment(
            new FakeStartupStore(command),
            new FakeProcessController([741]));
    }

    private sealed record TestEnvironment(
        FakeStartupStore Startup,
        FakeProcessController Processes);
}

internal sealed class FakeStartupStore(string? command) : IOkkSkinStartupStore
{
    public string? Command { get; set; } = command;

    public int RemoveCalls { get; private set; }

    public OperationResult? RemoveResult { get; set; }

    public Task<OperationResult<string?>> ReadAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(
            OperationResult<string?>.SuccessOptional(Command));

    public Task<OperationResult> RemoveAsync(
        string expectedCommand,
        CancellationToken cancellationToken)
    {
        RemoveCalls++;
        if (RemoveResult is not null)
        {
            return Task.FromResult(RemoveResult);
        }

        if (!string.Equals(Command, expectedCommand, StringComparison.Ordinal))
        {
            return Task.FromResult(
                OperationResult.Failure(
                    OperationErrorCode.Conflict,
                    "测试启动项已变化。"));
        }

        Command = null;
        return Task.FromResult(OperationResult.Success());
    }
}

internal sealed class FakeProcessController(
    IReadOnlyList<int> processIds) : IOkkSkinProcessController
{
    private IReadOnlyList<int> processIds = processIds;

    public int FindCalls { get; private set; }

    public List<int> StoppedProcessIds { get; } = [];

    public OperationResult? StopResult { get; set; }

    public Task<OperationResult<IReadOnlyList<int>>> FindAsync(
        string nodePath,
        string agentPath,
        CancellationToken cancellationToken)
    {
        FindCalls++;
        return Task.FromResult(
            OperationResult<IReadOnlyList<int>>.Success(processIds));
    }

    public Task<OperationResult> StopAsync(
        IReadOnlyList<int> targetProcessIds,
        CancellationToken cancellationToken)
    {
        if (StopResult is not null)
        {
            return Task.FromResult(StopResult);
        }

        StoppedProcessIds.AddRange(targetProcessIds);
        processIds = processIds
            .Except(targetProcessIds)
            .ToArray();
        return Task.FromResult(OperationResult.Success());
    }
}
