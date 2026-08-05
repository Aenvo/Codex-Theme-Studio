namespace CodexThemeStudio.CodexAdapter;

using CodexThemeStudio.Contracts.Models;

public sealed class CodexVersionPolicy
{
    private static readonly IReadOnlySet<string> DefaultVerifiedVersions =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "26.715.4045.0",
            "26.715.10079.0",
            "26.721.3404.0",
            "26.727.6591.0",
        };

    private readonly IReadOnlySet<string> verifiedVersions;

    public CodexVersionPolicy(IReadOnlySet<string>? verifiedVersions = null)
    {
        this.verifiedVersions = verifiedVersions ?? DefaultVerifiedVersions;
    }

    public bool IsVerified(string version) =>
        verifiedVersions.Contains(version);

    public CodexCompatibilityLevel Evaluate(string version, CodexProbeResult probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        if (!probe.ElectronAvailable ||
            !probe.BrowserWindowAvailable ||
            !probe.ExecuteJavaScriptAvailable ||
            probe.EligibleWindowCount < 1 ||
            !probe.CanaryApplied ||
            !probe.CanaryCleaned)
        {
            return CodexCompatibilityLevel.Incompatible;
        }

        return IsVerified(version)
            ? CodexCompatibilityLevel.Verified
            : CodexCompatibilityLevel.CompatibleByProbe;
    }
}
