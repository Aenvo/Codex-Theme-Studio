using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CodexThemeStudio.CodexAdapter;

public static class PackageDebugSettingsController
{
    public static bool Disable(string packageFullName)
    {
        return OperatingSystem.IsWindows() &&
               Invoke(settings => settings.DisableDebugging(packageFullName));
    }

    [SupportedOSPlatform("windows")]
    private static bool Invoke(Func<IPackageDebugSettings, int> operation)
    {
        object? instance = null;
        try
        {
            var type = Type.GetTypeFromCLSID(
                new Guid("B1AEC16F-2383-4852-B0E9-8F0B1DC66B4D"),
                throwOnError: true)!;
            instance = Activator.CreateInstance(type);
            return instance is IPackageDebugSettings settings && operation(settings) >= 0;
        }
        catch (Exception exception) when (
            exception is COMException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            if (instance is not null && Marshal.IsComObject(instance))
            {
                _ = Marshal.FinalReleaseComObject(instance);
            }
        }
    }

    [ComImport]
    [Guid("F27C3930-8029-4AD1-94E3-3DBA417810C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPackageDebugSettings
    {
        [PreserveSig]
        int EnableDebugging(
            [MarshalAs(UnmanagedType.LPWStr)] string packageFullName,
            [MarshalAs(UnmanagedType.LPWStr)] string debuggerCommandLine,
            IntPtr environment);

        [PreserveSig]
        int DisableDebugging(
            [MarshalAs(UnmanagedType.LPWStr)] string packageFullName);
    }
}

public static class PackagedApplicationActivator
{
    public static int Activate(
        string applicationUserModelId,
        string? arguments = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException();
        }

        return ActivateWindows(applicationUserModelId, arguments);
    }

    [SupportedOSPlatform("windows")]
    private static int ActivateWindows(
        string applicationUserModelId,
        string? arguments)
    {
        object? instance = null;
        try
        {
            var type = Type.GetTypeFromCLSID(
                new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"),
                throwOnError: true)!;
            instance = Activator.CreateInstance(type);
            if (instance is not IApplicationActivationManager manager)
            {
                throw new InvalidOperationException(
                    "Application activation manager is unavailable.");
            }

            var result = manager.ActivateApplication(
                applicationUserModelId,
                arguments,
                ActivateOptions.None,
                out var processId);
            Marshal.ThrowExceptionForHR(result);
            return checked((int)processId);
        }
        finally
        {
            if (instance is not null && Marshal.IsComObject(instance))
            {
                _ = Marshal.FinalReleaseComObject(instance);
            }
        }
    }

    [Flags]
    private enum ActivateOptions : uint
    {
        None = 0,
    }

    [ComImport]
    [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string applicationUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
            ActivateOptions options,
            out uint processId);
    }
}
