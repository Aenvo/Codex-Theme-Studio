using System.Text;
using System.Text.Json;

namespace CodexThemeStudio.CodexAdapter.Tests;

public sealed class InjectorCommandClientTests
{
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
