using System.Security.Cryptography;
using System.Text;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;
using CodexThemeStudio.ThemeCore;

namespace CodexThemeStudio.CodexRuntime;

public sealed class CodexRuntimeCoordinator
{
    public const int SchemaVersion = 1;
    public const int ProtocolVersion = 1;
    public const int DiscoverDeadlineMilliseconds = 8_000;
    public const int OperationDeadlineMilliseconds = 20_000;
    public const int QualificationDeadlineMilliseconds = 35_000;
    public const int ExpectedAppliedManagedResourceCount = 10;
    public const string DefaultMacBundlePath = "/Applications/ChatGPT.app";

    private readonly ICodexPlatformBridge bridge;
    private readonly ICodexQualificationStore qualificationStore;
    private readonly SemaphoreSlim operationLock = new(1, 1);

    public CodexRuntimeCoordinator(
        ICodexPlatformBridge bridge,
        ICodexQualificationStore qualificationStore)
    {
        this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        this.qualificationStore =
            qualificationStore ?? throw new ArgumentNullException(nameof(qualificationStore));
    }

    public async Task<OperationResult<CodexPlatformResponse>> DiscoverAsync(
        CancellationToken cancellationToken)
    {
        var response = await bridge.ExecuteAsync(
            CreateRequest(
                CodexPlatformCommand.Discover,
                DiscoverDeadlineMilliseconds,
                theme: null),
            cancellationToken);
        if (!response.IsSuccess)
        {
            return response;
        }
        if (!IsTrustedDiscovery(response.Value!))
        {
            return OperationResult<CodexPlatformResponse>.Failure(
                OperationErrorCode.CodexIdentityMismatch,
                "Codex 安装或进程身份未通过可信发现。",
                "macos.discovery.untrusted");
        }
        return response;
    }

    public async Task<OperationResult<CodexPlatformResponse>> ApplyTemporaryAsync(
        ThemePackage theme,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var issues = ThemePackageContractValidator.Validate(theme);
        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return OperationResult<CodexPlatformResponse>.Failure(
                OperationErrorCode.ValidationFailed,
                "主题声明未通过安全校验。",
                "macos.theme.invalid");
        }

        bool lockAcquired;
        try
        {
            lockAcquired = await operationLock.WaitAsync(0, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Cancelled();
        }

        if (!lockAcquired)
        {
            return OperationResult<CodexPlatformResponse>.Failure(
                OperationErrorCode.Conflict,
                "已有主题操作正在进行。",
                "macos.operation.busy");
        }

        try
        {
            var discovery = await DiscoverAsync(cancellationToken);
            if (!discovery.IsSuccess)
            {
                return discovery;
            }

            var discovered = discovery.Value!;
            if (!IsTrustedDiscovery(discovered))
            {
                return OperationResult<CodexPlatformResponse>.Failure(
                    OperationErrorCode.CodexIdentityMismatch,
                    "Codex 安装或进程身份未通过可信发现。",
                    "macos.discovery.untrusted");
            }

            var fingerprint = ComputeInstallationFingerprint(discovered.Installation!);
            var qualified = await qualificationStore.IsQualifiedAsync(
                fingerprint,
                ProtocolVersion,
                cancellationToken);
            var command = qualified
                ? CodexPlatformCommand.ApplyTemporary
                : CodexPlatformCommand.QualifyAndApply;
            var deadline = qualified
                ? OperationDeadlineMilliseconds
                : QualificationDeadlineMilliseconds;
            var applied = await bridge.ExecuteAsync(
                CreateRequest(command, deadline, ProjectTheme(theme)),
                cancellationToken);
            if (!applied.IsSuccess)
            {
                return applied;
            }

            var appliedValue = applied.Value!;
            if (!IsSuccessfulApply(appliedValue))
            {
                return OperationResult<CodexPlatformResponse>.Failure(
                    OperationErrorCode.InvalidResponse,
                    "macOS Runtime 未能证明主题已应用且 Inspector 已关闭。",
                    appliedValue.ErrorCode ?? "macos.apply.proof_incomplete");
            }

            if (!qualified)
            {
                await qualificationStore.WriteQualifiedAsync(
                    fingerprint,
                    ProtocolVersion,
                    appliedValue.ToolVersion,
                    cancellationToken);
            }

            return applied;
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<OperationResult<CodexPlatformResponse>> RestoreAsync(
        CancellationToken cancellationToken)
    {
        bool lockAcquired;
        try
        {
            lockAcquired = await operationLock.WaitAsync(0, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Cancelled();
        }

        if (!lockAcquired)
        {
            return OperationResult<CodexPlatformResponse>.Failure(
                OperationErrorCode.Conflict,
                "已有主题操作正在进行。",
                "macos.operation.busy");
        }

        try
        {
            var response = await bridge.ExecuteAsync(
                CreateRequest(
                    CodexPlatformCommand.Cleanup,
                    OperationDeadlineMilliseconds,
                    theme: null),
                cancellationToken);
            if (!response.IsSuccess)
            {
                return response;
            }

            var value = response.Value!;
            if (!value.IsSuccess ||
                value.CleanupDisposition != CodexCleanupDisposition.Verified ||
                value.InspectorDisposition != CodexInspectorDisposition.Closed ||
                value.ResidualCount != 0 ||
                value.PortListenerCount != 0)
            {
                return OperationResult<CodexPlatformResponse>.Failure(
                    OperationErrorCode.InvalidResponse,
                    "macOS Runtime 未能证明清理完成且 Inspector 已关闭。",
                    value.ErrorCode ?? "macos.cleanup.proof_incomplete");
            }

            return response;
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<OperationResult<CodexPlatformResponse>> InspectRuntimeAsync(
        CancellationToken cancellationToken)
    {
        bool lockAcquired;
        try
        {
            lockAcquired = await operationLock.WaitAsync(0, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Cancelled();
        }

        if (!lockAcquired)
        {
            return OperationResult<CodexPlatformResponse>.Failure(
                OperationErrorCode.Conflict,
                "已有主题操作正在进行。",
                "macos.operation.busy");
        }

        try
        {
            var response = await bridge.ExecuteAsync(
                CreateRequest(
                    CodexPlatformCommand.InspectRuntime,
                    OperationDeadlineMilliseconds,
                    theme: null),
                cancellationToken);
            if (!response.IsSuccess)
            {
                return response;
            }

            var value = response.Value!;
            if (!IsSuccessfulRuntimeInspection(value))
            {
                return OperationResult<CodexPlatformResponse>.Failure(
                    OperationErrorCode.InvalidResponse,
                    "macOS Runtime 未能证明 Inspector 诊断完成且已关闭。",
                    value.ErrorCode ?? "macos.inspect.proof_incomplete");
            }

            return response;
        }
        finally
        {
            operationLock.Release();
        }
    }

    public static CodexThemeProjection ProjectTheme(ThemePackage theme) =>
        new(
            theme.SchemaVersion,
            theme.Id,
            theme.Variant,
            new CodexThemePaletteProjection(
                theme.Palette.Background,
                theme.Palette.Panel,
                theme.Palette.Accent,
                theme.Palette.Text,
                theme.Palette.Muted,
                theme.Palette.Border));

    public static string ComputeInstallationFingerprint(
        CodexInstallationIdentityV2 installation)
    {
        ArgumentNullException.ThrowIfNull(installation);
        var canonical = string.Join(
            '\n',
            installation.Platform,
            installation.ProductIdentifier,
            installation.PublisherIdentifier,
            installation.Version,
            installation.BuildVersion,
            installation.ExecutablePath,
            installation.ExecutableSha256,
            installation.SignatureValid,
            installation.HardenedRuntime,
            installation.GatekeeperAccepted);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static CodexPlatformRequest CreateRequest(
        CodexPlatformCommand command,
        int deadlineMilliseconds,
        CodexThemeProjection? theme) =>
        new(
            SchemaVersion,
            ProtocolVersion,
            Guid.NewGuid(),
            command,
            deadlineMilliseconds,
            DefaultMacBundlePath,
            theme);

    private static bool IsTrustedDiscovery(CodexPlatformResponse response) =>
        response.IsSuccess &&
        response.Installation is
        {
            Platform: CodexHostPlatform.MacOS,
            ProductIdentifier: "com.openai.codex",
            PublisherIdentifier: "2DC432GLL2",
            SignatureValid: true,
            HardenedRuntime: true,
            GatekeeperAccepted: true,
        } &&
        response.Process is not null &&
        response.PortListenerCount == 0 &&
        response.InspectorDisposition == CodexInspectorDisposition.NotOpened;

    private static bool IsSuccessfulApply(CodexPlatformResponse response) =>
        response.IsSuccess &&
        response.AppliedWindowCount == 1 &&
        response.EligibleWindowCount == 1 &&
        response.ResidualCount == ExpectedAppliedManagedResourceCount &&
        response.PortListenerCount == 0 &&
        response.InspectorDisposition == CodexInspectorDisposition.Closed &&
        response.CleanupDisposition != CodexCleanupDisposition.Unverified;

    private static bool IsSuccessfulRuntimeInspection(
        CodexPlatformResponse response) =>
        response.IsSuccess &&
        response.Installation is
        {
            Platform: CodexHostPlatform.MacOS,
            ProductIdentifier: "com.openai.codex",
            PublisherIdentifier: "2DC432GLL2",
            SignatureValid: true,
            HardenedRuntime: true,
            GatekeeperAccepted: true,
        } &&
        response.Process is not null &&
        response.EligibleWindowCount == 1 &&
        response.AppliedWindowCount == 0 &&
        response.ResidualCount == 0 &&
        response.PortListenerCount == 0 &&
        response.InspectorDisposition == CodexInspectorDisposition.Closed &&
        response.CleanupDisposition == CodexCleanupDisposition.NotNeeded;

    private static OperationResult<CodexPlatformResponse> Cancelled() =>
        OperationResult<CodexPlatformResponse>.Failure(
            OperationErrorCode.Cancelled,
            "操作已在启动 macOS Helper 前取消。",
            "operation.cancelled");
}
