using System.Text.Json;
using System.Text.Json.Serialization;
using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.CodexAdapter;

public sealed class CodexThemeRuntimeService : ICodexThemeRuntime
{
    private const int MaximumImageBytes = 16 * 1024 * 1024;
    private readonly ICodexDiscoveryService discoveryService;
    private readonly ICodexInspectorService inspectorService;
    private readonly IInjectorRendererClient rendererClient;
    private readonly IThemeAssetStore assetStore;
    private readonly IThemeRepository themeRepository;
    private readonly ICurrentSessionStore sessionStore;
    private readonly CodexVersionPolicy versionPolicy;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan operationTimeout;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly object operationSync = new();
    private ActiveOperation? activeOperation;

    public CodexThemeRuntimeService(
        ICodexDiscoveryService discoveryService,
        ICodexInspectorService inspectorService,
        IInjectorRendererClient rendererClient,
        IThemeAssetStore assetStore,
        IThemeRepository themeRepository,
        ICurrentSessionStore sessionStore,
        CodexVersionPolicy? versionPolicy = null,
        TimeProvider? timeProvider = null,
        TimeSpan? operationTimeout = null)
    {
        this.discoveryService =
            discoveryService ?? throw new ArgumentNullException(nameof(discoveryService));
        this.inspectorService =
            inspectorService ?? throw new ArgumentNullException(nameof(inspectorService));
        this.rendererClient =
            rendererClient ?? throw new ArgumentNullException(nameof(rendererClient));
        this.assetStore =
            assetStore ?? throw new ArgumentNullException(nameof(assetStore));
        this.themeRepository =
            themeRepository ?? throw new ArgumentNullException(nameof(themeRepository));
        this.sessionStore =
            sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        this.versionPolicy = versionPolicy ?? new CodexVersionPolicy();
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.operationTimeout = operationTimeout ?? TimeSpan.FromSeconds(60);
    }

    public Task<OperationResult<ThemeRuntimeStatus>> ApplyTemporaryAsync(
        ThemePackage theme,
        CancellationToken cancellationToken) =>
        ApplyCoreAsync(theme, allowRollback: false, cancellationToken);

    public Task<OperationResult<ThemeRuntimeStatus>> SwitchTemporaryAsync(
        ThemePackage theme,
        CancellationToken cancellationToken) =>
        ApplyCoreAsync(theme, allowRollback: true, cancellationToken);

    public async Task<OperationResult<ThemeRuntimeStatus>> RestoreAsync(
        CancellationToken cancellationToken)
    {
        var operationId = Guid.NewGuid();
        if (!writeLock.Wait(0))
        {
            return Busy();
        }

        SetActive(new ActiveOperation(
            operationId,
            ThemeRuntimeState.Restoring,
            null,
            timeProvider.GetUtcNow()));
        CodexProcessInfo? process = null;
        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(operationTimeout);
        try
        {
            var context = await DiscoverAsync(
                timeoutSource.Token,
                requireVerifiedVersion: false);
            if (!context.IsSuccess)
            {
                if (context.Error!.Code == OperationErrorCode.CodexNotFound)
                {
                    var clear = await WriteDefaultAsync(operationId);
                    return clear.IsSuccess
                        ? OperationResult<ThemeRuntimeStatus>.Success(
                            CreateStatus(
                                ThemeRuntimeState.Default,
                                "Codex 未运行；当前会话已是官方外观。",
                                operationId: operationId))
                        : OperationResult<ThemeRuntimeStatus>.Failure(clear.Error!);
                }

                return OperationResult<ThemeRuntimeStatus>.Failure(context.Error!);
            }

            process = context.Value!.Process;
            var cleanup = await rendererClient.CleanupAsync(
                process,
                timeoutSource.Token);
            if (!cleanup.IsSuccess &&
                cleanup.Error!.Code != OperationErrorCode.ProcessExited)
            {
                return OperationResult<ThemeRuntimeStatus>.Failure(cleanup.Error!);
            }

            var write = await WriteDefaultAsync(operationId);
            if (!write.IsSuccess)
            {
                return OperationResult<ThemeRuntimeStatus>.Failure(write.Error!);
            }

            return OperationResult<ThemeRuntimeStatus>.Success(
                CreateStatus(
                    ThemeRuntimeState.Default,
                    cleanup.IsSuccess
                        ? "已完整还原 Codex 官方外观。"
                        : "Codex 已退出；当前会话状态已安全清除。",
                    processId: cleanup.IsSuccess ? process.ProcessId : null,
                    operationId: operationId,
                    codexVersion: context.Value.Installation.Version));
        }
        finally
        {
            if (process is not null)
            {
                await TryCloseInspectorAsync(process);
            }

            ClearActive(operationId);
            writeLock.Release();
        }
    }

    public async Task<OperationResult<ThemeRuntimeStatus>> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        var active = GetActive();
        if (active is not null)
        {
            return OperationResult<ThemeRuntimeStatus>.Success(
                CreateStatus(
                    active.State,
                    active.State == ThemeRuntimeState.Restoring
                        ? "正在还原 Codex 官方外观。"
                        : "正在应用临时主题。",
                    selectedThemeId: active.ThemeId,
                    operationId: active.OperationId));
        }

        var session = await sessionStore.ReadAsync(cancellationToken);
        if (!session.IsSuccess)
        {
            return OperationResult<ThemeRuntimeStatus>.Failure(session.Error!);
        }

        var installation = await discoveryService.FindInstallationAsync(cancellationToken);
        if (!installation.IsSuccess)
        {
            if (installation.Error!.Code == OperationErrorCode.CodexNotFound)
            {
                return OperationResult<ThemeRuntimeStatus>.Success(
                    CreateStatus(
                        ThemeRuntimeState.NotInstalled,
                        "未检测到当前用户安装的官方 Microsoft Store Codex。",
                        selectedThemeId: session.Value!.ThemeId));
            }

            return OperationResult<ThemeRuntimeStatus>.Failure(installation.Error!);
        }

        if (!versionPolicy.IsVerified(installation.Value!.Version))
        {
            return OperationResult<ThemeRuntimeStatus>.Success(
                CreateStatus(
                    ThemeRuntimeState.Unsupported,
                    "当前 Codex 版本尚未验证，已按安全策略停止注入。",
                    selectedThemeId: session.Value!.ThemeId,
                    codexVersion: installation.Value.Version));
        }

        var processes = await discoveryService.FindProcessesAsync(
            installation.Value,
            cancellationToken);
        if (!processes.IsSuccess)
        {
            return OperationResult<ThemeRuntimeStatus>.Failure(processes.Error!);
        }

        if (processes.Value!.Count == 0)
        {
            return OperationResult<ThemeRuntimeStatus>.Success(
                CreateStatus(
                    ThemeRuntimeState.NotRunning,
                    "Codex 未运行；请先启动 Codex。",
                    selectedThemeId: session.Value!.ThemeId,
                    codexVersion: installation.Value.Version));
        }

        if (processes.Value.Count != 1)
        {
            return OperationResult<ThemeRuntimeStatus>.Success(
                CreateStatus(
                    ThemeRuntimeState.Unavailable,
                    "检测到多个 Codex 主进程，无法安全确定目标实例。",
                    selectedThemeId: session.Value!.ThemeId,
                    codexVersion: installation.Value.Version));
        }

        var process = processes.Value[0];
        var renderer = await rendererClient.GetStatusAsync(
            process,
            cancellationToken);
        if (!renderer.IsSuccess)
        {
            if (renderer.Error!.Code == OperationErrorCode.PortInUse)
            {
                return OperationResult<ThemeRuntimeStatus>.Success(
                    CreateStatus(
                        ThemeRuntimeState.InspectorResidual,
                        "Inspector 端口被异常占用，未读取页面内容。",
                        processId: process.ProcessId,
                        selectedThemeId: session.Value!.ThemeId,
                        evidence: ThemeRuntimeEvidence.ProcessOnly,
                        hasInspectorResidual: true,
                        codexVersion: installation.Value.Version));
            }

            await TryCloseInspectorAsync(process);
            return OperationResult<ThemeRuntimeStatus>.Failure(renderer.Error!);
        }

        await TryCloseInspectorAsync(process);
        return OperationResult<ThemeRuntimeStatus>.Success(
            MapRendererStatus(
                renderer.Value!,
                session.Value!,
                process,
                installation.Value.Version));
    }

    private async Task<OperationResult<ThemeRuntimeStatus>> ApplyCoreAsync(
        ThemePackage theme,
        bool allowRollback,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var operationId = Guid.NewGuid();
        if (!writeLock.Wait(0))
        {
            return Busy();
        }

        SetActive(new ActiveOperation(
            operationId,
            ThemeRuntimeState.Applying,
            theme.Id,
            timeProvider.GetUtcNow()));
        CodexProcessInfo? process = null;
        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(operationTimeout);
        try
        {
            var session = await sessionStore.ReadAsync(timeoutSource.Token);
            if (!session.IsSuccess)
            {
                return OperationResult<ThemeRuntimeStatus>.Failure(session.Error!);
            }

            var context = await DiscoverAsync(timeoutSource.Token);
            if (!context.IsSuccess)
            {
                return OperationResult<ThemeRuntimeStatus>.Failure(context.Error!);
            }

            process = context.Value!.Process;
            var payload = await CreatePayloadAsync(theme, timeoutSource.Token);
            if (!payload.IsSuccess)
            {
                return OperationResult<ThemeRuntimeStatus>.Failure(payload.Error!);
            }

            ReadOnlyMemory<byte>? rollbackPayload = null;
            if (allowRollback &&
                session.Value!.ThemeId is { } previousThemeId &&
                previousThemeId != theme.Id)
            {
                rollbackPayload = await TryCreateRollbackPayloadAsync(
                    previousThemeId,
                    timeoutSource.Token);
            }

            var apply = await rendererClient.ApplyAsync(
                process,
                payload.Value!,
                timeoutSource.Token);
            if (!apply.IsSuccess)
            {
                await RecoverAfterFailedSwitchAsync(
                    process,
                    rollbackPayload,
                    session.Value!,
                    operationId);
                return OperationResult<ThemeRuntimeStatus>.Failure(apply.Error!);
            }

            var renderer = apply.Value!;
            if (renderer.RuntimeVersion != 1 ||
                !renderer.Active ||
                renderer.ThemeId != theme.Id)
            {
                await RecoverAfterFailedSwitchAsync(
                    process,
                    rollbackPayload,
                    session.Value!,
                    operationId);
                return OperationResult<ThemeRuntimeStatus>.Failure(
                    OperationErrorCode.InvalidResponse,
                    "渲染器未确认目标主题，已执行安全恢复。",
                    "runtime.apply.verification_failed");
            }

            var state = renderer.AppliedWindows > 0 &&
                renderer.Failures == 0 &&
                renderer.AppliedWindows >= renderer.EligibleWindows
                ? ThemeRuntimeState.Temporary
                : ThemeRuntimeState.Partial;
            var newSession = new RuntimeSessionState(
                RuntimeSessionState.CurrentSchemaVersion,
                state,
                theme.Id,
                process.ProcessId,
                process.StartedAtUtc,
                renderer.Generation,
                operationId,
                timeProvider.GetUtcNow());
            var write = await WriteWithRecoveryTokenAsync(newSession);
            if (!write.IsSuccess)
            {
                await RecoverAfterFailedSwitchAsync(
                    process,
                    rollbackPayload,
                    session.Value!,
                    operationId);
                return OperationResult<ThemeRuntimeStatus>.Failure(write.Error!);
            }

            var message = state == ThemeRuntimeState.Temporary
                ? "临时主题已应用；仅影响当前 Codex 实例。"
                : "主题运行时已建立，但仍有窗口待就绪或应用失败。";
            if (state == ThemeRuntimeState.Temporary)
            {
                var recent = await RecordApplyWithRecoveryTokenAsync(theme.Id, message);
                if (!recent.IsSuccess)
                {
                    message += " 最近使用记录暂未更新。";
                }
            }

            return OperationResult<ThemeRuntimeStatus>.Success(
                CreateStatus(
                    state,
                    message,
                    theme.Id,
                    process.ProcessId,
                    theme.Id,
                    operationId,
                    ThemeRuntimeEvidence.RuntimeMarkers,
                    renderer.InspectorWasAlreadyOpen,
                    renderer.EligibleWindows,
                    renderer.AppliedWindows,
                    Math.Max(0, renderer.EligibleWindows - renderer.AppliedWindows),
                    context.Value.Installation.Version));
        }
        finally
        {
            if (process is not null)
            {
                await TryCloseInspectorAsync(process);
            }

            ClearActive(operationId);
            writeLock.Release();
        }
    }

    private async Task<OperationResult<RuntimeContext>> DiscoverAsync(
        CancellationToken cancellationToken,
        bool requireVerifiedVersion = true)
    {
        var installation = await discoveryService.FindInstallationAsync(cancellationToken);
        if (!installation.IsSuccess)
        {
            return OperationResult<RuntimeContext>.Failure(installation.Error!);
        }

        var installed = installation.Value!;
        if (requireVerifiedVersion &&
            !versionPolicy.IsVerified(installed.Version))
        {
            return OperationResult<RuntimeContext>.Failure(
                OperationErrorCode.UnsupportedVersion,
                "当前 Codex 版本尚未验证，未执行注入。",
                "runtime.codex_version_unverified");
        }

        var processes = await discoveryService.FindProcessesAsync(
            installed,
            cancellationToken);
        if (!processes.IsSuccess)
        {
            return OperationResult<RuntimeContext>.Failure(processes.Error!);
        }

        if (processes.Value!.Count == 0)
        {
            return OperationResult<RuntimeContext>.Failure(
                OperationErrorCode.CodexNotFound,
                "Codex 未运行；请先启动 Codex。",
                "runtime.codex_not_running");
        }

        if (processes.Value.Count != 1)
        {
            return OperationResult<RuntimeContext>.Failure(
                OperationErrorCode.Conflict,
                "检测到多个 Codex 主进程，无法安全确定目标实例。",
                "runtime.multiple_main_processes");
        }

        return OperationResult<RuntimeContext>.Success(
            new RuntimeContext(installed, processes.Value[0]));
    }

    private async Task<OperationResult<byte[]>> CreatePayloadAsync(
        ThemePackage theme,
        CancellationToken cancellationToken)
    {
        var relativePath = $"themes/{theme.Id:D}/{theme.Art.File}";
        var opened = await assetStore.OpenReadAsync(relativePath, cancellationToken);
        if (!opened.IsSuccess)
        {
            return OperationResult<byte[]>.Failure(opened.Error!);
        }

        await using var stream = opened.Value!;
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > MaximumImageBytes)
            {
                return OperationResult<byte[]>.Failure(
                    OperationErrorCode.ValidationFailed,
                    "主题图片超过 16 MB 安全上限。",
                    "runtime.image_too_large");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        var image = buffer.ToArray();
        var contentType = DetectContentType(image);
        if (contentType is null)
        {
            return OperationResult<byte[]>.Failure(
                OperationErrorCode.ValidationFailed,
                "主题图片内容不是受支持的 PNG、JPEG 或 WebP。",
                "runtime.image_signature_invalid");
        }

        return OperationResult<byte[]>.Success(
            JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    theme,
                    image = new
                    {
                        contentType,
                        base64 = Convert.ToBase64String(image),
                    },
                },
                PayloadJsonOptions));
    }

    private async Task<ReadOnlyMemory<byte>?> TryCreateRollbackPayloadAsync(
        Guid themeId,
        CancellationToken cancellationToken)
    {
        var theme = await themeRepository.GetAsync(themeId, cancellationToken);
        if (!theme.IsSuccess)
        {
            return null;
        }

        var payload = await CreatePayloadAsync(theme.Value!, cancellationToken);
        return payload.IsSuccess ? payload.Value : null;
    }

    private async Task RecoverAfterFailedSwitchAsync(
        CodexProcessInfo process,
        ReadOnlyMemory<byte>? rollbackPayload,
        RuntimeSessionState previousSession,
        Guid operationId)
    {
        using var recoverySource = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        if (rollbackPayload is { } payload)
        {
            var rollback = await rendererClient.ApplyAsync(
                process,
                payload,
                recoverySource.Token);
            if (rollback.IsSuccess &&
                rollback.Value!.Active &&
                rollback.Value.ThemeId == previousSession.ThemeId)
            {
                var recovered = previousSession with
                {
                    Generation = rollback.Value.Generation,
                    OperationId = operationId,
                    UpdatedAtUtc = timeProvider.GetUtcNow(),
                };
                await sessionStore.WriteAsync(recovered, recoverySource.Token);
                return;
            }
        }

        await rendererClient.CleanupAsync(process, recoverySource.Token);
        await sessionStore.WriteAsync(
            RuntimeSessionState.Default(operationId, timeProvider.GetUtcNow()),
            recoverySource.Token);
    }

    private ThemeRuntimeStatus MapRendererStatus(
        RendererRuntimeResult renderer,
        RuntimeSessionState session,
        CodexProcessInfo process,
        string codexVersion)
    {
        if (renderer.InspectorWasAlreadyOpen)
        {
            return CreateStatus(
                ThemeRuntimeState.InspectorResidual,
                "检测到 Inspector 异常残留；查询结束后已请求关闭。",
                renderer.ThemeId,
                process.ProcessId,
                session.ThemeId,
                evidence: renderer.Active
                    ? ThemeRuntimeEvidence.RuntimeMarkers
                    : ThemeRuntimeEvidence.ProcessOnly,
                hasInspectorResidual: true,
                eligibleWindows: renderer.EligibleWindows,
                appliedWindows: renderer.AppliedWindows,
                pendingWindows: Math.Max(
                    0,
                    renderer.EligibleWindows - renderer.AppliedWindows),
                codexVersion: codexVersion);
        }

        if (!renderer.Active)
        {
            var staleSelection = session.ThemeId is not null &&
                (session.CodexProcessId != process.ProcessId ||
                 session.CodexStartedAtUtc != process.StartedAtUtc);
            return CreateStatus(
                staleSelection ? ThemeRuntimeState.Mismatch : ThemeRuntimeState.Ready,
                staleSelection
                    ? "当前 Codex 实例未应用所选主题。"
                    : "Codex 正在运行，当前未注入主题。",
                processId: process.ProcessId,
                selectedThemeId: session.ThemeId,
                evidence: ThemeRuntimeEvidence.ProcessOnly,
                codexVersion: codexVersion);
        }

        if (renderer.ThemeId != session.ThemeId)
        {
            return CreateStatus(
                ThemeRuntimeState.Mismatch,
                "运行中的主题与当前会话所选主题不一致。",
                renderer.ThemeId,
                process.ProcessId,
                session.ThemeId,
                evidence: ThemeRuntimeEvidence.RuntimeMarkers,
                eligibleWindows: renderer.EligibleWindows,
                appliedWindows: renderer.AppliedWindows,
                pendingWindows: Math.Max(
                    0,
                    renderer.EligibleWindows - renderer.AppliedWindows),
                codexVersion: codexVersion);
        }

        var partial = renderer.Failures > 0 ||
            renderer.AppliedWindows == 0 ||
            renderer.AppliedWindows < renderer.EligibleWindows;
        return CreateStatus(
            partial ? ThemeRuntimeState.Partial : ThemeRuntimeState.Temporary,
            partial
                ? "主题已建立，但仍有窗口待就绪或应用失败。"
                : "主题运行时标记已确认；尚未执行用户可见效果验证。",
            renderer.ThemeId,
            process.ProcessId,
            session.ThemeId,
            evidence: ThemeRuntimeEvidence.RuntimeMarkers,
            eligibleWindows: renderer.EligibleWindows,
            appliedWindows: renderer.AppliedWindows,
            pendingWindows: Math.Max(
                0,
                renderer.EligibleWindows - renderer.AppliedWindows),
            codexVersion: codexVersion);
    }

    private async Task<OperationResult> WriteDefaultAsync(Guid operationId) =>
        await WriteWithRecoveryTokenAsync(
            RuntimeSessionState.Default(operationId, timeProvider.GetUtcNow()));

    private async Task<OperationResult> WriteWithRecoveryTokenAsync(
        RuntimeSessionState state)
    {
        using var source = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await sessionStore.WriteAsync(state, source.Token);
    }

    private async Task<OperationResult> RecordApplyWithRecoveryTokenAsync(
        Guid themeId,
        string message)
    {
        using var source = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await themeRepository.RecordApplyResultAsync(
            themeId,
            ThemeApplyResult.Succeeded,
            message,
            source.Token);
    }

    private async Task TryCloseInspectorAsync(CodexProcessInfo process)
    {
        using var source = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await inspectorService.CloseInspectorAsync(process, source.Token);
    }

    private static string? DetectContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 &&
            bytes[..8].SequenceEqual(
                new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }))
        {
            return "image/png";
        }

        if (bytes.Length >= 4 &&
            bytes[0] == 0xff &&
            bytes[1] == 0xd8 &&
            bytes[^2] == 0xff &&
            bytes[^1] == 0xd9)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 12 &&
            bytes[..4].SequenceEqual("RIFF"u8) &&
            bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }

    private ThemeRuntimeStatus CreateStatus(
        ThemeRuntimeState state,
        string message,
        Guid? themeId = null,
        int? processId = null,
        Guid? selectedThemeId = null,
        Guid? operationId = null,
        ThemeRuntimeEvidence evidence = ThemeRuntimeEvidence.None,
        bool hasInspectorResidual = false,
        int eligibleWindows = 0,
        int appliedWindows = 0,
        int pendingWindows = 0,
        string? codexVersion = null) =>
        new(
            state,
            themeId,
            false,
            processId,
            timeProvider.GetUtcNow(),
            message,
            selectedThemeId,
            operationId,
            evidence,
            hasInspectorResidual,
            eligibleWindows,
            appliedWindows,
            pendingWindows,
            codexVersion);

    private OperationResult<ThemeRuntimeStatus> Busy()
    {
        var active = GetActive();
        return OperationResult<ThemeRuntimeStatus>.Failure(
            OperationErrorCode.Conflict,
            active is null
                ? "另一个主题写操作正在执行，请稍后重试。"
                : $"另一个主题写操作正在执行（操作 ID：{active.OperationId:D}）。",
            "runtime.operation_busy");
    }

    private void SetActive(ActiveOperation operation)
    {
        lock (operationSync)
        {
            activeOperation = operation;
        }
    }

    private ActiveOperation? GetActive()
    {
        lock (operationSync)
        {
            return activeOperation;
        }
    }

    private void ClearActive(Guid operationId)
    {
        lock (operationSync)
        {
            if (activeOperation?.OperationId == operationId)
            {
                activeOperation = null;
            }
        }
    }

    private sealed record RuntimeContext(
        CodexInstallationInfo Installation,
        CodexProcessInfo Process);

    private sealed record ActiveOperation(
        Guid OperationId,
        ThemeRuntimeState State,
        Guid? ThemeId,
        DateTimeOffset StartedAtUtc);

    private static readonly JsonSerializerOptions PayloadJsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
