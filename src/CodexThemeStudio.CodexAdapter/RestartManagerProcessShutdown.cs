using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.CodexAdapter;

internal static class RestartManagerProcessShutdown
{
    private const int ErrorSuccess = 0;

    internal static OperationResult Request(
        CodexProcessInfo expected,
        string expectedExecutablePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return OperationResult.Failure(
                OperationErrorCode.NotImplemented,
                "Windows 正常关机通道不可用。",
                "themed_launch.restart_manager_unsupported");
        }

        Process process;
        try
        {
            process = Process.GetProcessById(expected.ProcessId);
        }
        catch (ArgumentException)
        {
            return OperationResult.Success();
        }

        using (process)
        {
            try
            {
                var executablePath = Path.GetFullPath(process.MainModule!.FileName);
                var startedAtUtc = process.StartTime.ToUniversalTime();
                if (!string.Equals(
                        executablePath,
                        Path.GetFullPath(expectedExecutablePath),
                        StringComparison.OrdinalIgnoreCase) ||
                    Math.Abs((startedAtUtc - expected.StartedAtUtc).TotalSeconds) > 2)
                {
                    return OperationResult.Failure(
                        OperationErrorCode.IdentityChanged,
                        "ChatGPT 进程身份已经变化，未执行系统正常关机。",
                        "themed_launch.process_identity_changed");
                }

                var processStartFileTime = unchecked((ulong)startedAtUtc.ToFileTimeUtc());
                var uniqueProcess = new RmUniqueProcess
                {
                    ProcessId = checked((uint)process.Id),
                    ProcessStartTime = new NativeFileTime
                    {
                        LowDateTime = unchecked((uint)processStartFileTime),
                        HighDateTime = unchecked((uint)(processStartFileTime >> 32)),
                    },
                };
                var sessionKey = new StringBuilder(33);
                var startResult = RmStartSession(out var sessionHandle, 0, sessionKey);
                if (startResult != ErrorSuccess)
                {
                    return Failure(startResult, "start");
                }

                try
                {
                    var registerResult = RmRegisterResources(
                        sessionHandle,
                        0,
                        null,
                        1,
                        [uniqueProcess],
                        0,
                        null);
                    if (registerResult != ErrorSuccess)
                    {
                        return Failure(registerResult, "register");
                    }

                    var shutdownResult = RmShutdown(
                        sessionHandle,
                        actionFlags: 0,
                        statusCallback: IntPtr.Zero);
                    return shutdownResult == ErrorSuccess
                        ? OperationResult.Success()
                        : Failure(shutdownResult, "shutdown");
                }
                finally
                {
                    _ = RmEndSession(sessionHandle);
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                InvalidOperationException or
                Win32Exception or
                NotSupportedException or
                OverflowException)
            {
                return OperationResult.Failure(
                    OperationErrorCode.ExternalToolFailure,
                    "无法通过 Windows 请求 ChatGPT 正常退出。请手动退出后重试。",
                    "themed_launch.restart_manager_failed");
            }
        }
    }

    private static OperationResult Failure(int nativeError, string stage) =>
        OperationResult.Failure(
            OperationErrorCode.ExternalToolFailure,
            "Windows 无法让 ChatGPT 正常退出。请手动退出后重试。",
            $"themed_launch.restart_manager_{stage}_failed_{nativeError}");

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint LowDateTime;

        public uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public uint ProcessId;

        public NativeFileTime ProcessStartTime;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(
        out uint sessionHandle,
        int sessionFlags,
        StringBuilder sessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint sessionHandle,
        uint fileCount,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)]
        string[]? fileNames,
        uint applicationCount,
        [MarshalAs(UnmanagedType.LPArray)] RmUniqueProcess[] applications,
        uint serviceCount,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)]
        string[]? serviceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmShutdown(
        uint sessionHandle,
        uint actionFlags,
        IntPtr statusCallback);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint sessionHandle);
}
