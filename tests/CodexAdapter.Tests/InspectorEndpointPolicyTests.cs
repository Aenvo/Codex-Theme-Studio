namespace CodexThemeStudio.CodexAdapter.Tests;

using CodexThemeStudio.CodexAdapter;

public sealed class InspectorEndpointPolicyTests
{
    private const string BrowserId = "11111111-2222-4333-8444-555555555555";

    [Theory]
    [InlineData("http://example.com:9229/json/version", "non_loopback_host")]
    [InlineData("http://127.0.0.1:9229/json/version?token=value", "endpoint_components_rejected")]
    [InlineData("http://user:secret@127.0.0.1:9229/json/version", "endpoint_components_rejected")]
    [InlineData("http://127.0.0.1:9230/json/version", "unexpected_port")]
    [InlineData("http://127.0.0.1:9229/json/list", "unexpected_path")]
    public void HttpEndpoint_RejectsUnexpectedComponents(string rawUrl, string expectedDiagnostic)
    {
        var accepted = InspectorEndpointPolicy.TryValidateHttpEndpoint(
            new Uri(rawUrl),
            9229,
            "/json/version",
            out var diagnostic);

        Assert.False(accepted);
        Assert.Equal(expectedDiagnostic, diagnostic);
    }

    [Fact]
    public void WebSocketEndpoint_AcceptsExactLoopbackBrowserTarget()
    {
        var accepted = InspectorEndpointPolicy.TryValidateWebSocketEndpoint(
            new Uri($"ws://127.0.0.1:9229/{BrowserId}"),
            9229,
            BrowserId,
            out var diagnostic);

        Assert.True(accepted);
        Assert.Null(diagnostic);
    }
}
