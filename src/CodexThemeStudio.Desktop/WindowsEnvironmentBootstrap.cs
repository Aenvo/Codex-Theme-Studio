namespace CodexThemeStudio.Desktop;

internal static class WindowsEnvironmentBootstrap
{
    public static void EnsureWindowsDirectoryEnvironmentVariable()
    {
        if (!string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("windir")))
        {
            return;
        }

        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
        if (!string.IsNullOrWhiteSpace(systemRoot))
        {
            Environment.SetEnvironmentVariable("windir", systemRoot);
        }
    }
}
