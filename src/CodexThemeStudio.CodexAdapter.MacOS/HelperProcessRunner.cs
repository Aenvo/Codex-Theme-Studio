using System.Diagnostics;

namespace CodexThemeStudio.CodexAdapter.MacOS;

internal sealed record HelperProcessResult(
    int ExitCode,
    byte[] StandardOutput,
    byte[] StandardError,
    bool TimedOut);

internal interface IHelperProcessRunner
{
    Task<HelperProcessResult> RunAsync(
        string executablePath,
        ReadOnlyMemory<byte> request,
        TimeSpan timeout);
}

internal sealed class HelperProcessRunner : IHelperProcessRunner
{
    public async Task<HelperProcessResult> RunAsync(
        string executablePath,
        ReadOnlyMemory<byte> request,
        TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("serve");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("The macOS helper did not start.");
        }

        var outputTask = ReadBoundedAsync(
            process.StandardOutput.BaseStream,
            MacHelperClientOptions.MaximumResponseBytes);
        var errorTask = ReadBoundedAsync(
            process.StandardError.BaseStream,
            MacHelperClientOptions.MaximumErrorBytes);
        await process.StandardInput.BaseStream.WriteAsync(request);
        await process.StandardInput.BaseStream.FlushAsync();
        process.StandardInput.Close();

        using var timeoutSource = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            // Once started, the helper owns cleanup. Do not terminate a helper
            // that may already have activated the Inspector.
            return new HelperProcessResult(-1, [], [], TimedOut: true);
        }

        return new HelperProcessResult(
            process.ExitCode,
            await outputTask,
            await errorTask,
            TimedOut: false);
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        int maximumBytes)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8 * 1024];
        while (true)
        {
            var count = await stream.ReadAsync(buffer);
            if (count == 0)
            {
                return output.ToArray();
            }

            if (output.Length + count > maximumBytes)
            {
                throw new InvalidDataException("Helper output exceeded its limit.");
            }

            output.Write(buffer, 0, count);
        }
    }
}
