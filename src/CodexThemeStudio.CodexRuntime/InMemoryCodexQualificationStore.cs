namespace CodexThemeStudio.CodexRuntime;

public sealed class InMemoryCodexQualificationStore : ICodexQualificationStore
{
    private readonly object sync = new();
    private readonly HashSet<(string Fingerprint, int ProtocolVersion)> qualified = [];

    public Task<bool> IsQualifiedAsync(
        string installationFingerprint,
        int protocolVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            return Task.FromResult(
                qualified.Contains((installationFingerprint, protocolVersion)));
        }
    }

    public Task WriteQualifiedAsync(
        string installationFingerprint,
        int protocolVersion,
        string toolVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolVersion);
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            qualified.Add((installationFingerprint, protocolVersion));
        }

        return Task.CompletedTask;
    }
}
