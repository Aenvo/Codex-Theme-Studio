using CodexThemeStudio.Contracts.Models;

namespace CodexThemeStudio.CodexAdapter.Tests;

public sealed class CodexVersionPolicyTests
{
    [Fact]
    public void DefaultPolicy_AcceptsRecordedVersionAndRejectsUnknownVersion()
    {
        var policy = new CodexVersionPolicy();

        Assert.True(policy.IsVerified("26.715.4045.0"));
        Assert.True(policy.IsVerified("26.715.10079.0"));
        Assert.True(policy.IsVerified("26.721.3404.0"));
        Assert.True(policy.IsVerified("26.727.6591.0"));
        Assert.False(policy.IsVerified("26.715.4045.1"));
        Assert.False(policy.IsVerified("26.715.10079.1"));
        Assert.False(policy.IsVerified("26.721.3404.1"));
        Assert.False(policy.IsVerified("26.727.6591.1"));
        Assert.False(policy.IsVerified("99.0.0.0"));
    }

    [Fact]
    public void Evaluate_UnknownVersionWithCapabilities_IsCompatibleByProbe()
    {
        var policy = new CodexVersionPolicy();
        var probe = CompatibleProbe();

        Assert.Equal(
            CodexCompatibilityLevel.CompatibleByProbe,
            policy.Evaluate("99.0.0.0", probe));
    }

    [Fact]
    public void Evaluate_VerifiedVersionStillBlocksWhenCapabilityIsMissing()
    {
        var policy = new CodexVersionPolicy();
        var probe = CompatibleProbe() with
        {
            CanaryCleaned = false,
            DiagnosticCode = "capability.canary_cleanup_failed",
        };

        Assert.Equal(
            CodexCompatibilityLevel.Incompatible,
            policy.Evaluate("26.715.10079.0", probe));
    }

    private static CodexProbeResult CompatibleProbe() =>
        new(
            42,
            DateTimeOffset.UtcNow,
            "150.0.7871.124",
            1,
            ["main"],
            TimeSpan.Zero);

    [Fact]
    public void ExplicitPolicy_UsesExactOrdinalVersionRecords()
    {
        var policy = new CodexVersionPolicy(
            new HashSet<string>(StringComparer.Ordinal)
            {
                "1.2.3.4",
            });

        Assert.True(policy.IsVerified("1.2.3.4"));
        Assert.False(policy.IsVerified("1.2.3.04"));
        Assert.False(policy.IsVerified("1.2.3.4 "));
    }
}
