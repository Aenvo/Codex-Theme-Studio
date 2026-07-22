namespace CodexThemeStudio.CodexAdapter;

public static class InspectorEndpointPolicy
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "127.0.0.1",
        "localhost",
        "::1",
    };

    public static bool TryValidateHttpEndpoint(
        Uri endpoint,
        int expectedPort,
        string expectedPath,
        out string? diagnosticCode) =>
        TryValidate(endpoint, "http", expectedPort, expectedPath, diagnosticCode: out diagnosticCode);

    public static bool TryValidateWebSocketEndpoint(
        Uri endpoint,
        int expectedPort,
        string expectedBrowserId,
        out string? diagnosticCode)
    {
        if (!IsSafeIdentifier(expectedBrowserId))
        {
            diagnosticCode = "invalid_expected_browser_id";
            return false;
        }

        return TryValidate(
            endpoint,
            "ws",
            expectedPort,
            $"/{expectedBrowserId}",
            diagnosticCode: out diagnosticCode);
    }

    public static bool IsSafeIdentifier(string value) =>
        Guid.TryParseExact(value, "D", out _) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-');

    private static bool TryValidate(
        Uri endpoint,
        string expectedScheme,
        int expectedPort,
        string expectedPath,
        out string? diagnosticCode)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (expectedPort is < 1 or > 65535)
        {
            diagnosticCode = "invalid_expected_port";
            return false;
        }

        if (!endpoint.IsAbsoluteUri ||
            !string.Equals(endpoint.Scheme, expectedScheme, StringComparison.OrdinalIgnoreCase))
        {
            diagnosticCode = "invalid_scheme";
            return false;
        }

        if (!AllowedHosts.Contains(endpoint.Host))
        {
            diagnosticCode = "non_loopback_host";
            return false;
        }

        if (!string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
        {
            diagnosticCode = "endpoint_components_rejected";
            return false;
        }

        if (endpoint.Port != expectedPort)
        {
            diagnosticCode = "unexpected_port";
            return false;
        }

        if (!string.Equals(endpoint.AbsolutePath, expectedPath, StringComparison.Ordinal))
        {
            diagnosticCode = "unexpected_path";
            return false;
        }

        diagnosticCode = null;
        return true;
    }
}
