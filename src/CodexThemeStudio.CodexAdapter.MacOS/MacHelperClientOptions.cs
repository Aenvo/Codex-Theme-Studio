namespace CodexThemeStudio.CodexAdapter.MacOS;

internal sealed record MacHelperClientOptions
{
    public const int MaximumRequestBytes = 64 * 1024;
    public const int MaximumResponseBytes = 256 * 1024;
    public const int MaximumErrorBytes = 32 * 1024;

    public required string HelperPath { get; init; }

    public required string ExpectedSha256 { get; init; }
}
