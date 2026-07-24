using CodexThemeStudio.CodexAdapter;
using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.CodexAdapter.Tests;

public sealed class OkkSkinExternalThemeSourceTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"cts-okkskin-{Guid.NewGuid():N}");

    [Fact]
    public async Task Discover_MissingOptionalSourceReturnsSuccessfulEmptyResult()
    {
        var source = new OkkSkinExternalThemeSource(new FakeDiscovery(321), root);

        var result = await source.DiscoverCurrentAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Discover_MapsDoroAndConfirmsMatchingCodexPid()
    {
        WriteValidFixture();
        var source = new OkkSkinExternalThemeSource(new FakeDiscovery(321), root);

        var result = await source.DiscoverCurrentAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var descriptor = Assert.IsType<ExternalThemeDescriptor>(result.Value);
        Assert.Equal("okkskin:doro-q", descriptor.SourceIdentifier);
        Assert.Equal("Doro 出击", descriptor.Theme.Name);
        Assert.Equal("#B98AE933", descriptor.Theme.Palette.Border);
        Assert.True(descriptor.IsPersistenceConfigured);
        Assert.True(descriptor.IsAppliedToCurrentProcess);
        Assert.Equal(ThemeTaskMode.Hidden, descriptor.Theme.Art.TaskMode);

        var opened = await source.OpenImageAsync(
            descriptor.SourceIdentifier,
            descriptor.ImageSha256,
            CancellationToken.None);
        Assert.True(opened.IsSuccess);
        await using var image = opened.Value!;
        Assert.Equal(8, image.Length);
    }

    [Fact]
    public async Task Discover_DoesNotTreatMatchingStalePidAsVisibleTheme()
    {
        WriteValidFixture();
        var source = new OkkSkinExternalThemeSource(
            new FakeDiscovery(321),
            new FakeRenderer(knownExternalThemeActive: false),
            root);

        var result = await source.DiscoverCurrentAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var descriptor = Assert.IsType<ExternalThemeDescriptor>(result.Value);
        Assert.True(descriptor.IsPersistenceConfigured);
        Assert.False(descriptor.IsAppliedToCurrentProcess);
    }

    [Fact]
    public async Task Discover_UsesRestoredSessionToRejectStaleOkkStateWithoutInspector()
    {
        var processStartedAt = DateTimeOffset.Parse("2026-07-22T12:18:19Z");
        var okkStartedAt = DateTimeOffset.Parse("2026-07-22T12:20:00Z");
        WriteValidFixture(okkStartedAt: okkStartedAt);
        var restored = new RuntimeSessionState(
            RuntimeSessionState.CurrentSchemaVersion,
            ThemeRuntimeState.Default,
            null,
            321,
            processStartedAt,
            null,
            Guid.NewGuid(),
            okkStartedAt.AddMinutes(1));
        var source = new OkkSkinExternalThemeSource(
            new FakeDiscovery(321, processStartedAt),
            new FakeSessionStore(restored),
            root);

        var result = await source.DiscoverCurrentAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(Assert.IsType<ExternalThemeDescriptor>(result.Value)
            .IsAppliedToCurrentProcess);
    }

    [Fact]
    public async Task Discover_RejectsImagePathEscape()
    {
        WriteValidFixture(imageName: "../outside.jpg");
        var source = new OkkSkinExternalThemeSource(new FakeDiscovery(321), root);

        var result = await source.DiscoverCurrentAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("external.okkskin.metadata_invalid", result.Error!.DiagnosticCode);
    }

    [Fact]
    public async Task OpenImage_FailsWhenExpectedHashChanged()
    {
        WriteValidFixture();
        var source = new OkkSkinExternalThemeSource(new FakeDiscovery(321), root);

        var result = await source.OpenImageAsync(
            "okkskin:doro-q",
            new string('0', 64),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.Conflict, result.Error!.Code);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private void WriteValidFixture(
        string imageName = "bg.jpg",
        DateTimeOffset? okkStartedAt = null)
    {
        var current = Path.Combine(root, "current");
        Directory.CreateDirectory(current);
        var startedAtJson = okkStartedAt is null
            ? string.Empty
            : $",\"startedAt\":\"{okkStartedAt.Value:O}\"";
        File.WriteAllText(
            Path.Combine(root, "state.json"),
            $"{{\"skinId\":\"doro-q\",\"enabled\":true,\"appliedPid\":321{startedAtJson}}}");
        var themeJson =
            """{"schemaVersion":1,"id":"doro-q","name":"Doro 出击","variant":"dark","image":"__IMAGE__","colors":{"background":"#100d13","panel":"#1a151f","accent":"#b98ae9","text":"#efe9f0","muted":"#a99ab0","line":"rgba(185,138,233,.20)"}}"""
                .Replace("__IMAGE__", imageName, StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(current, "theme.json"), themeJson);
        if (imageName == "bg.jpg")
        {
            File.WriteAllBytes(Path.Combine(current, imageName), [1, 2, 3, 4, 5, 6, 7, 8]);
        }
    }

    private sealed class FakeDiscovery(
        int processId,
        DateTimeOffset? processStartedAt = null) : ICodexDiscoveryService
    {
        public Task<OperationResult<CodexDiscoverySnapshot>> DiscoverAsync(
            CancellationToken cancellationToken)
        {
            var installation =
                new CodexInstallationInfo("family", "full", "1.0.0", @"C:\Codex\codex.exe");
            return Task.FromResult(OperationResult<CodexDiscoverySnapshot>.Success(
                new CodexDiscoverySnapshot(
                    installation,
                    [new CodexProcessInfo(
                        processId,
                        processStartedAt ?? DateTimeOffset.UtcNow,
                        installation.ExecutablePath,
                        null)],
                    DateTimeOffset.UtcNow)));
        }
    }

    private sealed class FakeSessionStore(RuntimeSessionState state) : ICurrentSessionStore
    {
        public Task<OperationResult<RuntimeSessionState>> ReadAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<RuntimeSessionState>.Success(state));

        public Task<OperationResult> WriteAsync(
            RuntimeSessionState state,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRenderer(bool knownExternalThemeActive) : IInjectorRendererClient
    {
        public Task<OperationResult<RendererRuntimeResult>> ApplyAsync(
            CodexProcessInfo process,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult<RendererRuntimeResult>> GetStatusAsync(
            CodexProcessInfo process,
            CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<RendererRuntimeResult>.Success(
                new RendererRuntimeResult(
                    1,
                    false,
                    null,
                    null,
                    0,
                    0,
                    0,
                    0,
                    KnownExternalThemeActive: knownExternalThemeActive)));

        public Task<OperationResult<RendererRuntimeResult>> CleanupAsync(
            CodexProcessInfo process,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<OperationResult<CodexInspectionResult>> InspectAsync(
            CodexProcessInfo process,
            CodexInspectionMode mode,
            CancellationToken cancellationToken)
        {
            var status = await GetStatusAsync(process, cancellationToken);
            return status.IsSuccess
                ? OperationResult<CodexInspectionResult>.Success(
                    new CodexInspectionResult(null, status.Value!))
                : OperationResult<CodexInspectionResult>.Failure(status.Error!);
        }
    }
}
