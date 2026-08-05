using System.Text.Json;
using System.Text.Json.Serialization;
using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.CodexAdapter;

public sealed record PersistenceAgentConfiguration(
    int SchemaVersion,
    bool Enabled,
    bool Suspended,
    PersistenceStorageMode StorageMode,
    string SnapshotRoot,
    string AgentExecutablePath,
    string NodeExecutablePath,
    string InjectorScriptPath,
    string StateFilePath,
    string LogFilePath,
    int PollIntervalSeconds = 10,
    int VerifyIntervalSeconds = 60)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record PersistenceAgentState(
    int SchemaVersion,
    int? AppliedProcessId,
    DateTimeOffset? AppliedProcessStartedAtUtc,
    Guid? ThemeId,
    string? SnapshotFingerprint,
    DateTimeOffset? LastVerifiedAtUtc,
    string? LastDiagnosticCode,
    int? SuppressedProcessId = null,
    DateTimeOffset? SuppressedProcessStartedAtUtc = null,
    Guid? SuppressedThemeId = null)
{
    public const int CurrentSchemaVersion = 1;

    public static PersistenceAgentState Empty { get; } =
        new(CurrentSchemaVersion, null, null, null, null, null, null);
}

public static class PersistenceAgentConfigurationPolicy
{
    public static OperationResult Validate(
        PersistenceAgentConfiguration configuration,
        string configurationPath)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!Path.IsPathFullyQualified(configurationPath) ||
            !Path.IsPathFullyQualified(configuration.AgentExecutablePath) ||
            !Path.IsPathFullyQualified(configuration.NodeExecutablePath) ||
            !Path.IsPathFullyQualified(configuration.InjectorScriptPath) ||
            !Path.IsPathFullyQualified(configuration.SnapshotRoot) ||
            !Path.IsPathFullyQualified(configuration.StateFilePath) ||
            !Path.IsPathFullyQualified(configuration.LogFilePath) ||
            configuration.PollIntervalSeconds is < 3 or > 300 ||
            configuration.VerifyIntervalSeconds is < 15 or > 3600)
        {
            return Invalid("persistence.config.fields_invalid");
        }

        var agentDirectory = Path.GetDirectoryName(
            Path.GetFullPath(configuration.AgentExecutablePath))!;
        var expectedNode = Path.Combine(agentDirectory, "runtime", "node", "node.exe");
        var expectedInjector = Path.Combine(
            agentDirectory,
            "runtime",
            "injector",
            "index.mjs");
        var configDirectory = Path.GetDirectoryName(
            Path.GetFullPath(configurationPath))!;
        var stableRoot = Path.GetDirectoryName(configDirectory)!;
        if (!SamePath(configuration.NodeExecutablePath, expectedNode) ||
            !SamePath(configuration.InjectorScriptPath, expectedInjector) ||
            !SamePath(
                configuration.StateFilePath,
                Path.Combine(configDirectory, "state.json")) ||
            !IsDescendant(configuration.LogFilePath, stableRoot) ||
            !File.Exists(configuration.AgentExecutablePath) ||
            !File.Exists(configuration.NodeExecutablePath) ||
            !File.Exists(configuration.InjectorScriptPath))
        {
            return Invalid("persistence.config.runtime_identity_invalid");
        }

        if (configuration.StorageMode == PersistenceStorageMode.StrictDataRoot &&
            !Directory.Exists(configuration.SnapshotRoot))
        {
            return OperationResult.Failure(
                OperationErrorCode.StorageUnavailable,
                "严格存储模式的快照目录当前不可用。",
                "persistence.config.strict_snapshot_unavailable");
        }

        return OperationResult.Success();
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsDescendant(string path, string root)
    {
        var prefix = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(
            prefix,
            StringComparison.OrdinalIgnoreCase);
    }

    private static OperationResult Invalid(string diagnosticCode) =>
        OperationResult.Failure(
            OperationErrorCode.ValidationFailed,
            "Agent 配置未通过运行路径白名单校验。",
            diagnosticCode);
}

public interface IPersistenceAgentConfigurationStore
{
    Task<OperationResult<PersistenceAgentConfiguration>> ReadAsync(
        CancellationToken cancellationToken);

    Task<OperationResult> WriteAsync(
        PersistenceAgentConfiguration configuration,
        CancellationToken cancellationToken);
}

public interface IPersistenceAgentStateStore
{
    Task<OperationResult<PersistenceAgentState>> ReadAsync(
        CancellationToken cancellationToken);

    Task<OperationResult> WriteAsync(
        PersistenceAgentState state,
        CancellationToken cancellationToken);
}

public sealed class PersistenceAgentConfigurationStore(string path) :
    IPersistenceAgentConfigurationStore
{
    private readonly string fullPath = RequireAbsolute(path);

    public Task<OperationResult<PersistenceAgentConfiguration>> ReadAsync(
        CancellationToken cancellationToken) =>
        AtomicJsonFile.ReadAsync<PersistenceAgentConfiguration>(
            fullPath,
            PersistenceAgentConfiguration.CurrentSchemaVersion,
            static value => value.SchemaVersion,
            "Agent 配置",
            cancellationToken);

    public Task<OperationResult> WriteAsync(
        PersistenceAgentConfiguration configuration,
        CancellationToken cancellationToken) =>
        AtomicJsonFile.WriteAsync(fullPath, configuration, cancellationToken);

    private static string RequireAbsolute(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Path.IsPathFullyQualified(value)
            ? Path.GetFullPath(value)
            : throw new ArgumentException("Configuration path must be absolute.", nameof(value));
    }
}

public sealed class PersistenceAgentStateStore(string path) :
    IPersistenceAgentStateStore
{
    private readonly string fullPath = Path.GetFullPath(path);

    public async Task<OperationResult<PersistenceAgentState>> ReadAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(fullPath))
        {
            return OperationResult<PersistenceAgentState>.Success(
                PersistenceAgentState.Empty);
        }

        return await AtomicJsonFile.ReadAsync<PersistenceAgentState>(
            fullPath,
            PersistenceAgentState.CurrentSchemaVersion,
            static value => value.SchemaVersion,
            "Agent 状态",
            cancellationToken);
    }

    public Task<OperationResult> WriteAsync(
        PersistenceAgentState state,
        CancellationToken cancellationToken) =>
        AtomicJsonFile.WriteAsync(fullPath, state, cancellationToken);
}

public sealed class PersistenceAgentEngine
{
    private readonly IPersistenceSnapshotStore snapshotStore;
    private readonly ICodexDiscoveryService discoveryService;
    private readonly ICodexInspectorService inspectorService;
    private readonly IInjectorRendererClient rendererClient;
    private readonly IPersistenceAgentStateStore stateStore;
    private readonly CodexVersionPolicy versionPolicy;
    private readonly CodexCompatibilityQualificationStore qualificationStore;
    private readonly TimeProvider timeProvider;

    public PersistenceAgentEngine(
        IPersistenceSnapshotStore snapshotStore,
        ICodexDiscoveryService discoveryService,
        ICodexInspectorService inspectorService,
        IInjectorRendererClient rendererClient,
        IPersistenceAgentStateStore stateStore,
        CodexVersionPolicy? versionPolicy = null,
        CodexCompatibilityQualificationStore? qualificationStore = null,
        TimeProvider? timeProvider = null)
    {
        this.snapshotStore = snapshotStore;
        this.discoveryService = discoveryService;
        this.inspectorService = inspectorService;
        this.rendererClient = rendererClient;
        this.stateStore = stateStore;
        this.versionPolicy = versionPolicy ?? new CodexVersionPolicy();
        this.qualificationStore = qualificationStore ?? new CodexCompatibilityQualificationStore();
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<OperationResult<ThemeRuntimeStatus>> RunCycleAsync(
        PersistenceAgentConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!configuration.Enabled || configuration.Suspended)
        {
            return Success(
                ThemeRuntimeState.Default,
                "持久化 Agent 当前已停用或暂停。");
        }

        var snapshot = await snapshotStore.ReadCurrentAsync(
            configuration.SnapshotRoot,
            cancellationToken);
        if (!snapshot.IsSuccess)
        {
            return OperationResult<ThemeRuntimeStatus>.Failure(snapshot.Error!);
        }
        var verifiedSnapshot = snapshot.Value!;

        var discovery = await discoveryService.DiscoverAsync(cancellationToken);
        if (!discovery.IsSuccess)
        {
            return discovery.Error!.Code == OperationErrorCode.CodexNotFound
                ? Success(
                    ThemeRuntimeState.NotInstalled,
                    "未检测到官方 Codex；Agent 将低频重试。",
                    snapshot.Value!.Theme.Id)
                : OperationResult<ThemeRuntimeStatus>.Failure(discovery.Error!);
        }
        var installed = discovery.Value!.Installation;

        var qualification = await qualificationStore.IsQualifiedAsync(
            installed.ExecutableSha256,
            cancellationToken);
        var persistenceEligible = installed.SourceAcknowledged &&
            qualification.IsSuccess &&
            qualification.Value;
        if (!persistenceEligible)
        {
            return Success(
                ThemeRuntimeState.Unsupported,
                "当前 Codex 构建尚未完成临时应用兼容验证，Agent 暂停注入。",
                verifiedSnapshot.Theme.Id,
                codexVersion: installed.Version);
        }

        var processes = discovery.Value.Processes;
        if (processes.Count == 0)
        {
            return Success(
                ThemeRuntimeState.NotRunning,
                "Codex 未运行；Agent 保持空闲。",
                verifiedSnapshot.Theme.Id,
                codexVersion: installed.Version);
        }

        if (processes.Count != 1)
        {
            return OperationResult<ThemeRuntimeStatus>.Failure(
                OperationErrorCode.Conflict,
                "检测到多个 Codex 主进程，Agent 已停止本轮注入。",
                "persistence.agent.multiple_processes");
        }

        var process = processes[0];
        var state = await stateStore.ReadAsync(cancellationToken);
        if (!state.IsSuccess)
        {
            return OperationResult<ThemeRuntimeStatus>.Failure(state.Error!);
        }

        var temporaryOverrideMatches =
            state.Value!.SuppressedProcessId == process.ProcessId &&
            state.Value.SuppressedProcessStartedAtUtc == process.StartedAtUtc &&
            state.Value.SnapshotFingerprint == verifiedSnapshot.Descriptor.Fingerprint &&
            state.Value.SuppressedThemeId is not null &&
            state.Value.SuppressedThemeId != verifiedSnapshot.Theme.Id;
        if (temporaryOverrideMatches)
        {
            return Success(
                ThemeRuntimeState.Temporary,
                "当前 Codex 实例正在使用临时主题；Agent 本轮让行。",
                state.Value.SuppressedThemeId,
                process.ProcessId,
                ThemeRuntimeEvidence.ProcessOnly,
                installed.Version,
                verifiedSnapshot.Theme.Id);
        }

        var probe = await inspectorService.ProbeAsync(process, cancellationToken);
        if (!probe.IsSuccess ||
            versionPolicy.Evaluate(installed.Version, probe.Value!) ==
                CodexCompatibilityLevel.Incompatible)
        {
            await TryCloseInspectorAsync(process);
            return Success(
                ThemeRuntimeState.Unsupported,
                "Codex 能力探测失败，Agent 暂停本轮注入。",
                verifiedSnapshot.Theme.Id,
                process.ProcessId,
                ThemeRuntimeEvidence.ProcessOnly,
                installed.Version);
        }

        var now = timeProvider.GetUtcNow();
        var identityMatches =
            state.Value!.AppliedProcessId == process.ProcessId &&
            state.Value.AppliedProcessStartedAtUtc == process.StartedAtUtc &&
            state.Value.SnapshotFingerprint == verifiedSnapshot.Descriptor.Fingerprint &&
            state.Value.ThemeId == verifiedSnapshot.Theme.Id;
        if (identityMatches &&
            state.Value.LastVerifiedAtUtc is { } verifiedAt &&
            now - verifiedAt <
            TimeSpan.FromSeconds(configuration.VerifyIntervalSeconds))
        {
            return Success(
                ThemeRuntimeState.Persistent,
                "当前 Codex 实例已经应用持久主题；本轮未重复注入。",
                verifiedSnapshot.Theme.Id,
                process.ProcessId,
                ThemeRuntimeEvidence.ProcessOnly,
                installed.Version);
        }

        if (identityMatches)
        {
            var rendererStatus = await rendererClient.GetStatusAsync(
                process,
                cancellationToken);
            if (rendererStatus.IsSuccess &&
                rendererStatus.Value!.Active &&
                rendererStatus.Value.ThemeId == verifiedSnapshot.Theme.Id &&
                rendererStatus.Value.AppliedWindows > 0)
            {
                await stateStore.WriteAsync(
                    state.Value with { LastVerifiedAtUtc = now },
                    cancellationToken);
                await TryCloseInspectorAsync(process);
                return Success(
                    ThemeRuntimeState.Persistent,
                    "持久主题运行时标记已复核，无需重新注入。",
                    verifiedSnapshot.Theme.Id,
                    process.ProcessId,
                    ThemeRuntimeEvidence.RuntimeMarkers,
                    installed.Version);
            }
        }
        else
        {
            var handoffStatus = await rendererClient.GetStatusAsync(
                process,
                cancellationToken);
            if (!handoffStatus.IsSuccess)
            {
                await TryCloseInspectorAsync(process);
                return OperationResult<ThemeRuntimeStatus>.Failure(
                    handoffStatus.Error!);
            }

            if (handoffStatus.Value!.Active &&
                handoffStatus.Value.ThemeId == verifiedSnapshot.Theme.Id &&
                handoffStatus.Value.AppliedWindows > 0)
            {
                var handoffWrite = await stateStore.WriteAsync(
                    new PersistenceAgentState(
                        PersistenceAgentState.CurrentSchemaVersion,
                        process.ProcessId,
                        process.StartedAtUtc,
                        verifiedSnapshot.Theme.Id,
                        verifiedSnapshot.Descriptor.Fingerprint,
                        now,
                        null),
                    cancellationToken);
                await TryCloseInspectorAsync(process);
                if (!handoffWrite.IsSuccess)
                {
                    return OperationResult<ThemeRuntimeStatus>.Failure(
                        handoffWrite.Error!);
                }

                return Success(
                    ThemeRuntimeState.Persistent,
                    "Agent 已接管 GUI 应用的当前主题，无需重复注入。",
                    verifiedSnapshot.Theme.Id,
                    process.ProcessId,
                    ThemeRuntimeEvidence.RuntimeMarkers,
                    installed.Version);
            }

            if (handoffStatus.Value.Active &&
                handoffStatus.Value.ThemeId is { } temporaryThemeId &&
                temporaryThemeId != verifiedSnapshot.Theme.Id &&
                handoffStatus.Value.AppliedWindows > 0)
            {
                var suppressionWrite = await stateStore.WriteAsync(
                    state.Value with
                    {
                        ThemeId = verifiedSnapshot.Theme.Id,
                        SnapshotFingerprint = verifiedSnapshot.Descriptor.Fingerprint,
                        SuppressedProcessId = process.ProcessId,
                        SuppressedProcessStartedAtUtc = process.StartedAtUtc,
                        SuppressedThemeId = temporaryThemeId,
                    },
                    cancellationToken);
                await TryCloseInspectorAsync(process);
                if (!suppressionWrite.IsSuccess)
                {
                    return OperationResult<ThemeRuntimeStatus>.Failure(
                        suppressionWrite.Error!);
                }

                return Success(
                    ThemeRuntimeState.Temporary,
                    "检测到当前 Codex 实例的临时主题；Agent 已让行。",
                    temporaryThemeId,
                    process.ProcessId,
                    ThemeRuntimeEvidence.RuntimeMarkers,
                    installed.Version,
                    verifiedSnapshot.Theme.Id);
            }
        }

        var payload = RendererPayloadFactory.Create(
            verifiedSnapshot.Theme,
            verifiedSnapshot.Image);
        if (!payload.IsSuccess)
        {
            return OperationResult<ThemeRuntimeStatus>.Failure(payload.Error!);
        }

        try
        {
            var apply = await rendererClient.ApplyAsync(
                process,
                payload.Value!,
                cancellationToken);
            if (!apply.IsSuccess)
            {
                return OperationResult<ThemeRuntimeStatus>.Failure(apply.Error!);
            }

            if (!apply.Value!.Active ||
                apply.Value.ThemeId != verifiedSnapshot.Theme.Id ||
                apply.Value.AppliedWindows == 0)
            {
                return OperationResult<ThemeRuntimeStatus>.Failure(
                    OperationErrorCode.InvalidResponse,
                    "Agent 未能确认持久主题运行时标记。",
                    "persistence.agent.apply_unverified");
            }

            var write = await stateStore.WriteAsync(
                new PersistenceAgentState(
                    PersistenceAgentState.CurrentSchemaVersion,
                    process.ProcessId,
                    process.StartedAtUtc,
                    verifiedSnapshot.Theme.Id,
                    verifiedSnapshot.Descriptor.Fingerprint,
                    now,
                    null),
                cancellationToken);
            if (!write.IsSuccess)
            {
                return OperationResult<ThemeRuntimeStatus>.Failure(write.Error!);
            }

            return Success(
                ThemeRuntimeState.Persistent,
                "Agent 已向新的可信 Codex 实例应用持久主题。",
                verifiedSnapshot.Theme.Id,
                process.ProcessId,
                ThemeRuntimeEvidence.RuntimeMarkers,
                installed.Version);
        }
        finally
        {
            await TryCloseInspectorAsync(process);
        }
    }

    private async Task TryCloseInspectorAsync(CodexProcessInfo process)
    {
        using var source = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await inspectorService.CloseInspectorAsync(process, source.Token);
    }

    private OperationResult<ThemeRuntimeStatus> Success(
        ThemeRuntimeState state,
        string message,
        Guid? themeId = null,
        int? processId = null,
        ThemeRuntimeEvidence evidence = ThemeRuntimeEvidence.None,
        string? codexVersion = null,
        Guid? selectedThemeId = null) =>
        OperationResult<ThemeRuntimeStatus>.Success(
            new ThemeRuntimeStatus(
                state,
                themeId,
                true,
                processId,
                timeProvider.GetUtcNow(),
                message,
                selectedThemeId ?? themeId,
                null,
                evidence,
                false,
                0,
                0,
                0,
                codexVersion));
}

public sealed class PersistenceAgentRunner
{
    public const string MutexName =
        @"Local\CodexThemeStudio.PersistenceAgent.v1";
    public const string StopEventName =
        @"Local\CodexThemeStudio.PersistenceAgent.Stop.v1";

    private readonly string configurationPath;
    private readonly string appVersion;
    private readonly string mutexName;
    private readonly string stopEventName;

    public PersistenceAgentRunner(
        string configurationPath,
        string appVersion = "unknown")
        : this(configurationPath, appVersion, MutexName, StopEventName)
    {
    }

    internal PersistenceAgentRunner(
        string configurationPath,
        string appVersion,
        string mutexName,
        string stopEventName)
    {
        this.configurationPath = Path.GetFullPath(configurationPath);
        this.appVersion = appVersion;
        this.mutexName = RequireWaitHandleName(mutexName, nameof(mutexName));
        this.stopEventName = RequireWaitHandleName(
            stopEventName,
            nameof(stopEventName));
    }

    internal string InstanceMutexName => mutexName;

    internal string ShutdownEventName => stopEventName;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return 4;
        }

        using var mutex = new Mutex(
            initiallyOwned: true,
            mutexName,
            out var createdNew);
        if (!createdNew)
        {
            return 3;
        }

        using var stopEvent = new EventWaitHandle(
            false,
            EventResetMode.ManualReset,
            stopEventName);
        var configurationStore =
            new PersistenceAgentConfigurationStore(configurationPath);
        var delay = TimeSpan.FromSeconds(5);
        var sessionId = Guid.NewGuid();
        var diagnostics = new LocalDiagnosticService(
            LocalDiagnosticService.GetDefaultLogDirectory());
        string? lastSignature = null;
        var lastHeartbeatAtUtc = DateTimeOffset.MinValue;
        var previousFailed = false;

        _ = await diagnostics.WriteAsync(
            DiagnosticEventFactory.Create(
                DiagnosticSource.Agent,
                DiagnosticLevel.Information,
                "agent.started",
                DiagnosticOutcome.Started,
                sessionId,
                appVersion,
                operation: "persistence.agent"),
            cancellationToken);

        while (!cancellationToken.IsCancellationRequested &&
               !stopEvent.WaitOne(TimeSpan.Zero))
        {
            var configuration = await configurationStore.ReadAsync(cancellationToken);
            if (!configuration.IsSuccess)
            {
                var configurationSignature =
                    $"configuration:{configuration.Error!.DiagnosticCode ?? configuration.Error.Code.ToString()}";
                var configurationNow = DateTimeOffset.UtcNow;
                if (!string.Equals(
                        configurationSignature,
                        lastSignature,
                        StringComparison.Ordinal) ||
                    configurationNow - lastHeartbeatAtUtc >= TimeSpan.FromMinutes(15))
                {
                    _ = await diagnostics.WriteAsync(
                        DiagnosticEventFactory.Create(
                            DiagnosticSource.Agent,
                            DiagnosticLevel.Error,
                            "agent.configuration.failed",
                            DiagnosticOutcome.Failed,
                            sessionId,
                            appVersion,
                            operation: "persistence.agent",
                            error: configuration.Error),
                        cancellationToken);
                    lastSignature = configurationSignature;
                    lastHeartbeatAtUtc = configurationNow;
                }

                previousFailed = true;
                delay = NextBackoff(delay);
                await DelayOrStopAsync(stopEvent, delay, cancellationToken);
                continue;
            }

            if (!configuration.Value!.Enabled)
            {
                _ = await diagnostics.WriteAsync(
                    DiagnosticEventFactory.Create(
                        DiagnosticSource.Agent,
                        DiagnosticLevel.Information,
                        "agent.stopped",
                        DiagnosticOutcome.Succeeded,
                        sessionId,
                        appVersion,
                        operation: "persistence.agent"),
                    CancellationToken.None);
                return 0;
            }

            var policy = PersistenceAgentConfigurationPolicy.Validate(
                configuration.Value,
                configurationPath);
            if (!policy.IsSuccess)
            {
                _ = await diagnostics.WriteAsync(
                    DiagnosticEventFactory.Create(
                        DiagnosticSource.Agent,
                        DiagnosticLevel.Error,
                        "agent.configuration.failed",
                        DiagnosticOutcome.Failed,
                        sessionId,
                        appVersion,
                        operation: "persistence.agent",
                        error: policy.Error),
                    cancellationToken);
                return 5;
            }

            var client = new InjectorCommandClient(
                configuration.Value.NodeExecutablePath,
                configuration.Value.InjectorScriptPath,
                targetSelection: new CodexTargetSelectionService());
            var engine = new PersistenceAgentEngine(
                new PersistenceSnapshotStore(),
                client,
                client,
                client,
                new PersistenceAgentStateStore(configuration.Value.StateFilePath));
            var cycle = await engine.RunCycleAsync(
                configuration.Value,
                cancellationToken);
            var signature = cycle.IsSuccess
                ? $"state:{cycle.Value!.State}"
                : $"error:{cycle.Error!.DiagnosticCode ?? cycle.Error.Code.ToString()}";
            var now = DateTimeOffset.UtcNow;
            var shouldWrite = !string.Equals(
                    signature,
                    lastSignature,
                    StringComparison.Ordinal) ||
                now - lastHeartbeatAtUtc >= TimeSpan.FromMinutes(15);
            if (shouldWrite)
            {
                var recovered = cycle.IsSuccess && previousFailed;
                var diagnosticEvent = cycle.IsSuccess
                    ? DiagnosticEventFactory.Create(
                        DiagnosticSource.Agent,
                        recovered
                            ? DiagnosticLevel.Information
                            : GetLevel(cycle.Value!.State),
                        recovered
                            ? "agent.recovered"
                            : $"agent.state.{cycle.Value!.State.ToString().ToLowerInvariant()}",
                        recovered
                            ? DiagnosticOutcome.Recovered
                            : DiagnosticOutcome.State,
                        sessionId,
                        appVersion,
                        operation: "persistence.agent",
                        codexVersion: cycle.Value!.CodexVersion)
                    : DiagnosticEventFactory.Create(
                        DiagnosticSource.Agent,
                        DiagnosticLevel.Error,
                        "agent.cycle.failed",
                        DiagnosticOutcome.Failed,
                        sessionId,
                        appVersion,
                        operation: "persistence.agent",
                        error: cycle.Error);
                var write = await diagnostics.WriteAsync(
                    diagnosticEvent,
                    cancellationToken);
                if (!write.IsSuccess)
                {
                    await RecordLogWriteFailureAsync(
                        configuration.Value.StateFilePath,
                        write.Error!.DiagnosticCode,
                        cancellationToken);
                }
                else
                {
                    await ClearLogWriteFailureAsync(
                        configuration.Value.StateFilePath,
                        cancellationToken);
                }

                lastSignature = signature;
                lastHeartbeatAtUtc = now;
            }

            previousFailed = !cycle.IsSuccess;
            delay = cycle.IsSuccess
                ? TimeSpan.FromSeconds(configuration.Value.PollIntervalSeconds)
                : NextBackoff(delay);
            await DelayOrStopAsync(stopEvent, delay, cancellationToken);
        }

        _ = await diagnostics.WriteAsync(
            DiagnosticEventFactory.Create(
                DiagnosticSource.Agent,
                DiagnosticLevel.Information,
                "agent.stopped",
                DiagnosticOutcome.Succeeded,
                sessionId,
                appVersion,
                operation: "persistence.agent"),
            CancellationToken.None);
        return 0;
    }

    public static bool SignalStop()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var stopEvent = EventWaitHandle.OpenExisting(StopEventName);
            return stopEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return true;
        }
    }

    private static TimeSpan NextBackoff(TimeSpan current) =>
        TimeSpan.FromSeconds(Math.Min(60, Math.Max(5, current.TotalSeconds * 2)));

    private static string RequireWaitHandleName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }

    private static DiagnosticLevel GetLevel(ThemeRuntimeState state) =>
        state switch
        {
            ThemeRuntimeState.Unsupported or
            ThemeRuntimeState.Partial or
            ThemeRuntimeState.Mismatch => DiagnosticLevel.Warning,
            ThemeRuntimeState.Unavailable or
            ThemeRuntimeState.Failed or
            ThemeRuntimeState.InspectorResidual => DiagnosticLevel.Error,
            _ => DiagnosticLevel.Information,
        };

    private static async Task RecordLogWriteFailureAsync(
        string stateFilePath,
        string? diagnosticCode,
        CancellationToken cancellationToken)
    {
        var stateStore = new PersistenceAgentStateStore(stateFilePath);
        var state = await stateStore.ReadAsync(cancellationToken);
        if (state.IsSuccess)
        {
            _ = await stateStore.WriteAsync(
                state.Value! with
                {
                    LastDiagnosticCode =
                        diagnosticCode ?? "diagnostics.write_failed",
                },
                cancellationToken);
        }
    }

    private static async Task ClearLogWriteFailureAsync(
        string stateFilePath,
        CancellationToken cancellationToken)
    {
        var stateStore = new PersistenceAgentStateStore(stateFilePath);
        var state = await stateStore.ReadAsync(cancellationToken);
        if (state.IsSuccess &&
            state.Value!.LastDiagnosticCode?.StartsWith(
                "diagnostics.",
                StringComparison.Ordinal) == true)
        {
            _ = await stateStore.WriteAsync(
                state.Value with { LastDiagnosticCode = null },
                cancellationToken);
        }
    }

    private static async Task DelayOrStopAsync(
        EventWaitHandle stopEvent,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + delay;
        while (DateTimeOffset.UtcNow < deadline &&
               !stopEvent.WaitOne(TimeSpan.Zero))
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(250),
                cancellationToken);
        }
    }
}

internal static class AtomicJsonFile
{
    private const int MaximumBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Options = new(
        JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = true,
    };

    public static async Task<OperationResult<T>> ReadAsync<T>(
        string path,
        int schemaVersion,
        Func<T, int> getSchemaVersion,
        string label,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists ||
                info.Length is <= 0 or > MaximumBytes ||
                (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return OperationResult<T>.Failure(
                    OperationErrorCode.NotFound,
                    $"{label}不存在或无效。",
                    "persistence.json.missing");
            }

            var value = JsonSerializer.Deserialize<T>(
                await File.ReadAllBytesAsync(path, cancellationToken),
                Options);
            return value is not null && getSchemaVersion(value) == schemaVersion
                ? OperationResult<T>.Success(value)
                : OperationResult<T>.Failure(
                    OperationErrorCode.UnsupportedVersion,
                    $"{label} Schema 不受支持。",
                    "persistence.json.schema_unsupported");
        }
        catch (OperationCanceledException)
        {
            return OperationResult<T>.Failure(
                OperationErrorCode.Cancelled,
                $"{label}读取已取消。",
                "persistence.json.cancelled");
        }
        catch (Exception exception)
            when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return OperationResult<T>.Failure(
                OperationErrorCode.InvalidResponse,
                $"{label}已损坏或不可读。",
                "persistence.json.invalid");
        }
    }

    public static async Task<OperationResult> WriteAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)!;
        var temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                File.Replace(temporary, path, null);
            }
            else
            {
                File.Move(temporary, path);
            }

            return OperationResult.Success();
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Failure(
                OperationErrorCode.Cancelled,
                "原子写入已取消。",
                "persistence.json.write_cancelled");
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResult.Failure(
                OperationErrorCode.AccessDenied,
                "没有权限写入 Agent 运行文件。",
                "persistence.json.write_access_denied");
        }
        catch (IOException)
        {
            return OperationResult.Failure(
                OperationErrorCode.StorageUnavailable,
                "无法原子写入 Agent 运行文件。",
                "persistence.json.write_failed");
        }
    }
}
