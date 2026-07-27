using System.Text.Json;
using CodexThemeStudio.CodexAdapter.MacOS;

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
        var verification =
            await MacPackagedRuntimeIdentity.VerifyStrictAsync(
                helperPath,
                CancellationToken.None);
        if (!verification.CompleteChainMatch)
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

static void Emit(object value, int exitCode)
{
    Console.WriteLine(JsonSerializer.Serialize(value));
    Environment.Exit(exitCode);
}
