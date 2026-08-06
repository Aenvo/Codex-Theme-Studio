using System.Text.Json;
using CodexThemeStudio.Contracts.Models;

namespace CodexThemeStudio.Update;

public sealed class UpdateStartupCoordinator
{
    private readonly string updatesRoot;

    public UpdateStartupCoordinator(string? updatesRoot = null)
    {
        this.updatesRoot = updatesRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexThemeStudio",
            "Updates");
    }

    public static string? GetToken(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index + 1 < arguments.Count; index++)
        {
            if (arguments[index] == "--update-token" && IsToken(arguments[index + 1]))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }

    public async Task<UpdateInstallResult?> MarkHealthyAndWaitForResultAsync(
        string token,
        CancellationToken cancellationToken)
    {
        if (!IsToken(token)) return null;
        var healthDirectory = Path.Combine(updatesRoot, "health");
        Directory.CreateDirectory(healthDirectory);
        var healthPath = Path.Combine(healthDirectory, $"{token}.json");
        var temporary = healthPath + ".tmp";
        await File.WriteAllTextAsync(
            temporary,
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                token,
                processId = Environment.ProcessId,
                healthyAtUtc = DateTimeOffset.UtcNow,
            }),
            cancellationToken);
        File.Move(temporary, healthPath, overwrite: true);

        var resultPath = Path.Combine(updatesRoot, "results", $"{token}.json");
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(125);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(resultPath))
            {
                await using var stream = new FileStream(resultPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                return ParseResult(document.RootElement);
            }

            await Task.Delay(250, cancellationToken);
        }

        return new UpdateInstallResult(
            UpdateInstallOutcome.Failed,
            "unknown",
            "unknown",
            "更新程序未返回结果，请检查更新目录。",
            BackupDirectory: Path.Combine(updatesRoot, "runner", token));
    }

    private static UpdateInstallResult ParseResult(JsonElement root)
    {
        var outcomeText = root.GetProperty("outcome").GetString();
        if (!Enum.TryParse<UpdateInstallOutcome>(outcomeText, ignoreCase: true, out var outcome))
        {
            outcome = UpdateInstallOutcome.Failed;
        }

        return new UpdateInstallResult(
            outcome,
            root.GetProperty("oldVersion").GetString() ?? "unknown",
            root.GetProperty("newVersion").GetString() ?? "unknown",
            root.GetProperty("userMessage").GetString() ?? "更新结果未知。",
            root.TryGetProperty("preservedDirectory", out var preserved) ? preserved.GetString() : null,
            root.TryGetProperty("backupDirectory", out var backup) ? backup.GetString() : null,
            root.TryGetProperty("token", out var token) ? token.GetString() : null);
    }

    private static bool IsToken(string value) =>
        value.Length == 32 && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
