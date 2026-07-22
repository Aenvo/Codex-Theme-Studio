namespace CodexThemeStudio.CodexAdapter;

public sealed class CodexVersionPolicy
{
    private static readonly IReadOnlySet<string> DefaultVerifiedVersions =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "26.715.4045.0",
        };

    private readonly IReadOnlySet<string> verifiedVersions;

    public CodexVersionPolicy(IReadOnlySet<string>? verifiedVersions = null)
    {
        this.verifiedVersions = verifiedVersions ?? DefaultVerifiedVersions;
    }

    public bool IsVerified(string version) =>
        verifiedVersions.Contains(version);
}
