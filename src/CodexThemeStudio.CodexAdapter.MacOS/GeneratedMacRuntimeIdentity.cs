namespace CodexThemeStudio.CodexAdapter.MacOS;

// This file is intentionally fail-closed in source checkouts. The packaging
// pipeline must generate these values after building the Swift helper and
// before building the signed .NET application.
internal static class GeneratedMacRuntimeIdentity
{
    public const string HelperSha256 = "";

    public static bool IsConfigured =>
        HelperSha256.Length == 64 &&
        HelperSha256.All(Uri.IsHexDigit);
}
