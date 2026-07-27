using System.Diagnostics;
using System.Text.Json;
using CodexThemeStudio.CodexAdapter.MacOS;

const int maximumOutputBytes = 256 * 1024;
const int maximumErrorBytes = 32 * 1024;

if (args is ["schema"])
{
    Emit(new
    {
        schemaVersion = 1,
        toolVersion = "0.2.0",
        commands = new[] { "schema", "source-self-test", "self-test" },
    }, 0);
}

if (args is ["source-self-test"])
{
    Emit(new
    {
        schemaVersion = 1,
        toolVersion = "0.2.0",
        status = "ok",
        packagedRuntimeIdentityConfigured = false,
        checks = new[]
        {
            "compiled-helper-identity",
            "bounded-helper-output",
            "single-json-output",
            "fail-closed-source-default",
        },
    }, 0);
}

if (args is ["self-test"])
{
    try
    {
        var contents = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar))?.FullName;
        if (contents is null)
        {
            throw new InvalidOperationException();
        }

        var helperPath = Path.GetFullPath(Path.Combine(
            contents,
            "Helpers",
            "CodexThemeStudio.MacHelper"));
        if (!MacPackagedRuntimeIdentity.VerifyHelper(helperPath))
        {
            throw new UnauthorizedAccessException();
        }

        var processResult = await RunHelperSelfTest(helperPath);
        using var document = JsonDocument.Parse(processResult);
        var root = document.RootElement;
        if (root.GetProperty("status").GetString() != "ok" ||
            !root.GetProperty("packagedRuntimeIdentityConfigured").GetBoolean() ||
            !root.GetProperty("manifestToHelperMatch").GetBoolean() ||
            !root.GetProperty("runtimeFilesMatch").GetBoolean())
        {
            throw new InvalidDataException();
        }

        Emit(new
        {
            schemaVersion = 1,
            toolVersion = "0.2.0",
            status = "ok",
            packagedRuntimeIdentityConfigured = true,
            manifestToHelperMatch = true,
            helperToDotNetMatch = true,
            completeChainMatch = true,
        }, 0);
    }
    catch
    {
        Emit(new
        {
            schemaVersion = 1,
            toolVersion = "0.2.0",
            status = "error",
            packagedRuntimeIdentityConfigured = false,
            error = new
            {
                code = "runtime.identity_verification_failed",
                stage = "identity",
            },
        }, 1);
    }
}

Emit(new
{
    status = "error",
    error = new { code = "usage.invalid", stage = "usage" },
}, 2);

static async Task<byte[]> RunHelperSelfTest(string helperPath)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = helperPath,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
    };
    startInfo.ArgumentList.Add("self-test");
    using var process = new Process { StartInfo = startInfo };
    if (!process.Start())
    {
        throw new InvalidOperationException();
    }

    var outputTask = ReadBounded(process.StandardOutput.BaseStream, maximumOutputBytes);
    var errorTask = ReadBounded(process.StandardError.BaseStream, maximumErrorBytes);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await process.WaitForExitAsync(timeout.Token);
    var output = await outputTask;
    _ = await errorTask;
    if (process.ExitCode != 0 || output.Length == 0)
    {
        throw new InvalidDataException();
    }
    return output;
}

static async Task<byte[]> ReadBounded(Stream input, int maximumBytes)
{
    using var output = new MemoryStream();
    var buffer = new byte[8192];
    while (true)
    {
        var count = await input.ReadAsync(buffer);
        if (count == 0)
        {
            return output.ToArray();
        }
        if (output.Length + count > maximumBytes)
        {
            throw new InvalidDataException();
        }
        output.Write(buffer, 0, count);
    }
}

static void Emit(object value, int exitCode)
{
    Console.WriteLine(JsonSerializer.Serialize(value));
    Environment.Exit(exitCode);
}
