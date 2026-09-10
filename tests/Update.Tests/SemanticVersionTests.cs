using CodexThemeStudio.Update;

namespace CodexThemeStudio.Update.Tests;

public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("v1.3.3", "1.3.2", 1)]
    [InlineData("v1.3.2", "1.3.1", 1)]
    [InlineData("v1.3.1", "1.3.0", 1)]
    [InlineData("v1.3.0", "1.2.2", 1)]
    [InlineData("1.2.2", "1.2.2", 0)]
    [InlineData("1.2.1", "1.2.2", -1)]
    [InlineData("1.3.0-rc.1", "1.3.0", -1)]
    [InlineData("1.3.0-rc.2", "1.3.0-rc.1", 1)]
    public void CompareTo_UsesSemVerOrdering(string left, string right, int expected)
    {
        Assert.True(SemanticVersion.TryParse(left, out var leftVersion));
        Assert.True(SemanticVersion.TryParse(right, out var rightVersion));
        Assert.Equal(expected, Math.Sign(leftVersion.CompareTo(rightVersion)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("01.2.3")]
    [InlineData("1.2.3-")]
    [InlineData("latest")]
    public void TryParse_RejectsInvalidVersions(string value) =>
        Assert.False(SemanticVersion.TryParse(value, out _));
}
