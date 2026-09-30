using CodexThemeStudio.CodexAdapter;

namespace CodexThemeStudio.CodexAdapter.Tests;

public sealed class PackagedRendererProbeTests
{
    [Fact]
    public void PackageDebugController_ExposesRecoveryOnly()
    {
        var methods = typeof(PackageDebugSettingsController)
            .GetMethods(System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToArray();

        Assert.Equal(["Disable"], methods);
    }

    [Fact]
    public void ActivationArguments_UseLoopbackPortAndQuoteManagedProfile()
    {
        var result = PackagedRendererProbeRunner.BuildActivationArguments(
            @"D:\Managed Profiles\Codex Theme Studio",
            49152);

        Assert.Equal(
            "--remote-debugging-address=127.0.0.1 --remote-debugging-port=49152 \"--user-data-dir=D:\\Managed Profiles\\Codex Theme Studio\"",
            result);
    }

    [Fact]
    public void ActivationArguments_UseOfficialProfileWhenManagedProfileIsAbsent()
    {
        var result = PackagedRendererProbeRunner.BuildActivationArguments(
            managedUserDataPath: null,
            port: 49152);

        Assert.Equal(
            "--remote-debugging-address=127.0.0.1 --remote-debugging-port=49152",
            result);
        Assert.DoesNotContain("--user-data-dir", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(65536)]
    public void ActivationArguments_RejectUnsafePort(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PackagedRendererProbeRunner.BuildActivationArguments(
                @"D:\Managed Profiles\Codex Theme Studio",
                port));
    }

    [Fact]
    public void ProbeResponse_ParsesInjectorTopLevelEnvelope()
    {
        const string response = """
            {
              "service": "CodexThemeStudio.Injector",
              "version": "0.3.0",
              "protocolVersion": 1,
              "status": "ok",
              "portProbe": {
                "browserId": "d23f7f83-879d-4d18-9d71-5446465f0cf5",
                "targetCount": 2,
                "eligibleTargetCount": 1,
                "routeTypes": ["app:root", "avatar-overlay"],
                "canaryApplied": true,
                "canaryCleaned": true
              }
            }
            """;

        var result = PackagedRendererProbeRunner.ParseProbe(response);

        Assert.NotNull(result);
        Assert.Equal("d23f7f83-879d-4d18-9d71-5446465f0cf5", result.BrowserId);
        Assert.Equal(2, result.TargetCount);
        Assert.Equal(1, result.EligibleTargetCount);
        Assert.Equal(["app:root", "avatar-overlay"], result.RouteTypes);
        Assert.True(result.CanaryApplied);
        Assert.True(result.CanaryCleaned);
    }

    [Fact]
    public void ProbeResponse_RejectsObsoleteNestedPayloadShape()
    {
        const string response = """
            {
              "service": "CodexThemeStudio.Injector",
              "version": "0.3.0",
              "protocolVersion": 1,
              "status": "ok",
              "payload": {
                "portProbe": {
                  "browserId": "d23f7f83-879d-4d18-9d71-5446465f0cf5",
                  "targetCount": 1,
                  "eligibleTargetCount": 1,
                  "routeTypes": ["app:root"],
                  "canaryApplied": true,
                  "canaryCleaned": true
                }
              }
            }
            """;

        Assert.Null(PackagedRendererProbeRunner.ParseProbe(response));
    }

    [Fact]
    public void ProbeFailure_PreservesErrorCodeWhenDiagnosticCodeIsAbsent()
    {
        const string response = """
            {
              "service": "CodexThemeStudio.Injector",
              "version": "0.3.0",
              "protocolVersion": 1,
              "status": "error",
              "error": {
                "code": "port_renderer_unqualified",
                "retryable": true,
                "userMessage": "Injector 操作失败。"
              }
            }
            """;

        Assert.Equal(
            "port_renderer_unqualified",
            PackagedRendererProbeRunner.ReadDiagnosticCode(response));
    }

    [Fact]
    public void ProbeFailure_FormatsSanitizedStructuralEvidence()
    {
        const string response = """
            {
              "status": "error",
              "error": {
                "code": "port_renderer_unqualified",
                "diagnosticCode": "port_renderer_unqualified",
                "details": {
                  "targetCount": 3,
                  "candidateCount": 1,
                  "routeTypes": ["app:index.html", "avatar-overlay"],
                  "evaluations": [{
                    "qualified": false,
                    "reason": "shell-features-missing",
                    "pageMode": "home",
                    "features": {
                      "shell": true,
                      "sidebar": false,
                      "content": true,
                      "composer": true
                    }
                  }]
                }
              }
            }
            """;

        var failure = PackagedRendererProbeRunner.ReadProbeFailure(response);

        Assert.Equal("port_renderer_unqualified", failure.DiagnosticCode);
        Assert.Equal(
            "Renderer 回环端口探针失败。 页面 3，候选 1，路由 app:index.html,avatar-overlay，结构 shell-features-missing[shell=1,sidebar=0,content=1,composer=1]。",
            failure.UserMessage);
    }
}
