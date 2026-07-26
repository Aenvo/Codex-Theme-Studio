using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexThemeStudio.Storage;
using CodexThemeStudio.ThemeCore;

namespace CodexThemeStudio.Desktop.Services;

public interface IColorHistoryService
{
    ReadOnlyObservableCollection<string> Colors { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    void Record(string value);

    Task FlushAsync(CancellationToken cancellationToken);
}

public sealed class ColorHistoryService : IColorHistoryService
{
    public const int MaximumColors = 20;
    private const int CurrentSchemaVersion = 3;
    private const string FileName = "color-history.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly ObservableCollection<string> colors = [];
    private readonly ReadOnlyObservableCollection<string> readOnlyColors;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly string filePath;
    private CancellationTokenSource? pendingWrite;

    public ColorHistoryService(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        filePath = Path.Combine(dataRoot, FileName);
        readOnlyColors = new ReadOnlyObservableCollection<string>(colors);
    }

    public ReadOnlyObservableCollection<string> Colors => readOnlyColors;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            var document = JsonSerializer.Deserialize<HistoryDocument>(bytes, JsonOptions);
            if (document?.SchemaVersion != CurrentSchemaVersion || document.Colors is null)
            {
                return;
            }

            foreach (var value in document.Colors.Reverse())
            {
                if (ThemeColor.TryNormalize(value, out var normalized))
                {
                    AddNormalized(normalized);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }
    }

    public void Record(string value)
    {
        if (!ThemeColor.TryNormalize(value, out var normalized))
        {
            return;
        }

        AddNormalized(normalized);
        SchedulePersist();
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        pendingWrite?.Cancel();
        await PersistAsync(cancellationToken);
    }

    private void AddNormalized(string value)
    {
        var existingIndex = colors.IndexOf(value);
        if (existingIndex >= 0)
        {
            colors.RemoveAt(existingIndex);
        }

        colors.Insert(0, value);
        while (colors.Count > MaximumColors)
        {
            colors.RemoveAt(colors.Count - 1);
        }
    }

    private void SchedulePersist()
    {
        pendingWrite?.Cancel();
        var source = new CancellationTokenSource();
        pendingWrite = source;
        _ = PersistAfterDelayAsync(source.Token);
    }

    private async Task PersistAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(180), cancellationToken);
            await PersistAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        var snapshot = colors.ToArray();
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                new HistoryDocument(CurrentSchemaVersion, snapshot),
                JsonOptions);
            _ = await AtomicFileWriter.WriteAsync(filePath, bytes, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
        }
        finally
        {
            writeGate.Release();
        }
    }

    private sealed record HistoryDocument(int SchemaVersion, IReadOnlyList<string> Colors);
}

public sealed class NullColorHistoryService : IColorHistoryService
{
    private static readonly ReadOnlyObservableCollection<string> EmptyColors =
        new(new ObservableCollection<string>());

    public static NullColorHistoryService Instance { get; } = new();

    private NullColorHistoryService()
    {
    }

    public ReadOnlyObservableCollection<string> Colors => EmptyColors;

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Record(string value)
    {
    }

    public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
