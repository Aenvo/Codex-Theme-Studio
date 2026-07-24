using System.IO.Compression;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.CodexAdapter.Tests;

public sealed class LocalDiagnosticServiceTests
{
    [Fact]
    public async Task Read_MergesConsecutiveEventsAndPreservesRecovery()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var service = new LocalDiagnosticService(root);
            var session = Guid.NewGuid();
            var first = CreateEvent(
                session,
                new DateTimeOffset(2026, 7, 24, 1, 0, 0, TimeSpan.Zero),
                DiagnosticLevel.Information,
                "agent.state.persistent",
                DiagnosticOutcome.State);
            var second = first with
            {
                TimestampUtc = first.TimestampUtc.AddMinutes(15),
            };
            var error = CreateEvent(
                session,
                first.TimestampUtc.AddMinutes(16),
                DiagnosticLevel.Error,
                "agent.cycle.failed",
                DiagnosticOutcome.Failed,
                new OperationError(
                    OperationErrorCode.InvalidResponse,
                    "do not persist this message",
                    "injector_response_invalid"));
            var recovered = CreateEvent(
                session,
                first.TimestampUtc.AddMinutes(17),
                DiagnosticLevel.Information,
                "agent.recovered",
                DiagnosticOutcome.Recovered);

            foreach (var item in new[] { first, second, error, recovered })
            {
                Assert.True((await service.WriteAsync(
                    item,
                    CancellationToken.None)).IsSuccess);
            }

            var result = await service.ReadAsync(20, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(DiagnosticHealth.Normal, result.Value!.Health);
            Assert.Equal(3, result.Value.RecentEvents.Count);
            var persistent = Assert.Single(
                result.Value.RecentEvents,
                group => group.Event.EventName == "agent.state.persistent");
            Assert.Equal(2, persistent.RepeatCount);
            Assert.Equal(
                "injector_response_invalid",
                result.Value.LatestError!.DiagnosticCode);
            Assert.Equal(
                DiagnosticOutcome.Recovered,
                result.Value.LatestRecovery!.Outcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Write_RejectsUnsafeFreeTextFields()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var service = new LocalDiagnosticService(root);
            var unsafeEvent = CreateEvent(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                DiagnosticLevel.Error,
                "desktop operation with spaces",
                DiagnosticOutcome.Failed);

            var result = await service.WriteAsync(
                unsafeEvent,
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(
                "diagnostics.event.invalid",
                result.Error!.DiagnosticCode);
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Read_AcceptsLegacyAgentLogAndReportsInvalidLines()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, "agent.jsonl"),
                """
                {"timestampUtc":"2026-07-20T12:36:35Z","eventName":"cycle","code":"Persistent"}
                not-json

                """);
            var service = new LocalDiagnosticService(root);

            var result = await service.ReadAsync(20, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Single(result.Value!.RecentEvents);
            Assert.Equal("legacy.cycle", result.Value.RecentEvents[0].Event.EventName);
            Assert.Equal(1, result.Value.InvalidLineCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Export_UsesAllowlistAndExcludesSensitiveExceptionText()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var service = new LocalDiagnosticService(root);
            var secret =
                @"C:\Users\private-user\theme.json token=super-secret https://example.invalid/chat";
            var exception = CaptureException(secret);
            var item = DiagnosticEventFactory.Create(
                DiagnosticSource.Desktop,
                DiagnosticLevel.Error,
                "desktop.unhandled_exception",
                DiagnosticOutcome.Failed,
                Guid.NewGuid(),
                "1.1.7",
                operation: "desktop.lifecycle",
                exception: exception);
            Assert.True((await service.WriteAsync(
                item,
                CancellationToken.None)).IsSuccess);

            var destination = Path.Combine(root, "diagnostics.zip");
            var exported = await service.ExportAsync(
                destination,
                new DiagnosticIssueContext(
                    "1.1.7",
                    "25.1904.1001.0",
                    new string('a', 64),
                    "Persistent"),
                CancellationToken.None);

            Assert.True(exported.IsSuccess, exported.Error?.DiagnosticCode);
            using var archive = ZipFile.OpenRead(destination);
            Assert.Equal(
                new[]
                    {
                        "SHA256SUMS.txt",
                        "diagnostics.jsonl",
                        "environment.json",
                        "issue-summary.md",
                    }
                    .Order(StringComparer.Ordinal),
                archive.Entries
                    .Select(entry => entry.FullName)
                    .Order(StringComparer.Ordinal));
            foreach (var entry in archive.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                var content = await reader.ReadToEndAsync();
                Assert.DoesNotContain("private-user", content, StringComparison.Ordinal);
                Assert.DoesNotContain("super-secret", content, StringComparison.Ordinal);
                Assert.DoesNotContain("example.invalid", content, StringComparison.Ordinal);
                Assert.DoesNotContain("theme.json", content, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Write_RotatesTwoArchivesAndSupportsConcurrentCalls()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var service = new LocalDiagnosticService(
                root,
                maximumLogBytes: 512);
            var session = Guid.NewGuid();
            var tasks = Enumerable.Range(0, 18)
                .Select(index => service.WriteAsync(
                    CreateEvent(
                        session,
                        DateTimeOffset.UtcNow.AddSeconds(index),
                        DiagnosticLevel.Information,
                        index % 2 == 0
                            ? "desktop.operation.started"
                            : "desktop.operation.succeeded",
                        index % 2 == 0
                            ? DiagnosticOutcome.Started
                            : DiagnosticOutcome.Succeeded),
                    CancellationToken.None));

            var results = await Task.WhenAll(tasks);

            Assert.All(results, result => Assert.True(result.IsSuccess));
            Assert.True(File.Exists(Path.Combine(root, "desktop.jsonl")));
            Assert.True(File.Exists(Path.Combine(root, "desktop.jsonl.1")));
            Assert.True(File.Exists(Path.Combine(root, "desktop.jsonl.2")));
            var read = await service.ReadAsync(200, CancellationToken.None);
            Assert.True(read.IsSuccess);
            Assert.NotEmpty(read.Value!.RecentEvents);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static DiagnosticEvent CreateEvent(
        Guid session,
        DateTimeOffset timestamp,
        DiagnosticLevel level,
        string eventName,
        DiagnosticOutcome outcome,
        OperationError? error = null) =>
        new(
            DiagnosticEvent.CurrentSchemaVersion,
            timestamp,
            eventName.StartsWith("agent.", StringComparison.Ordinal)
                ? DiagnosticSource.Agent
                : DiagnosticSource.Desktop,
            level,
            eventName,
            eventName.StartsWith("agent.", StringComparison.Ordinal)
                ? "persistence.agent"
                : "desktop.operation",
            outcome,
            session,
            Guid.NewGuid(),
            error?.Code,
            error?.DiagnosticCode,
            "1.1.7",
            null,
            null,
            null);

    private static Exception CaptureException(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"CodexThemeStudio-Diagnostics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
