namespace CodexThemeStudio.CodexAdapter.Tests;

public sealed class CodexVersionPolicyTests
{
    [Fact]
    public void DefaultPolicy_AcceptsRecordedVersionAndRejectsUnknownVersion()
    {
        var policy = new CodexVersionPolicy();

        Assert.True(policy.IsVerified("26.715.4045.0"));
        Assert.False(policy.IsVerified("26.715.4045.1"));
        Assert.False(policy.IsVerified("99.0.0.0"));
    }

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
