using System.Text;
using System.Text.Json;
using CodexThemeStudio.Contracts.Models;

namespace CodexThemeStudio.CodexAdapter.Tests;

public sealed class InjectorCommandClientTests
{
    [Fact]
    public void RendererPortArguments_BindProcessExecutableAndRandomPort()
    {
        var process = new CodexProcessInfo(
            63540,
            DateTimeOffset.Parse("2026-09-28T14:43:00Z"),
            @"C:\Program Files\WindowsApps\OpenAI.Codex\app\ChatGPT.exe",
            null,
            13892);

        var arguments = InjectorCommandClient.RendererPortArguments(
            "renderer-port-apply",
            process);

        Assert.Equal(
            [
                "renderer-port-apply",
                "--pid",
                "63540",
                "--executable",
                process.ExecutablePath,
                "--port",
                "13892",
            ],
            arguments);
    }

    [Fact]
    public void ProcessStartInfo_ForcesUtf8ForStructuredOutput()
    {
        var startInfo = InjectorCommandClient.CreateProcessStartInfo(
            "node.exe",
            "index.mjs",
            ["probe", "--pid", "123"],
            redirectStandardInput: false);

        Assert.Equal(Encoding.UTF8.CodePage, startInfo.StandardOutputEncoding?.CodePage);
        Assert.Equal(Encoding.UTF8.CodePage, startInfo.StandardErrorEncoding?.CodePage);

        const string response = """
            {"status":"error","error":{"userMessage":"当前 ChatGPT 构建不允许启用 Inspector。"}}
            """;
        var bytes = Encoding.UTF8.GetBytes(response);
        var decoded = startInfo.StandardErrorEncoding!.GetString(bytes);
        using var document = JsonDocument.Parse(decoded);

        Assert.Equal(
            "当前 ChatGPT 构建不允许启用 Inspector。",
            document.RootElement
                .GetProperty("error")
                .GetProperty("userMessage")
                .GetString());
    }
}
