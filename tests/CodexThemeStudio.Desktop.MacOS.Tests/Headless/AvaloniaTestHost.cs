using Avalonia;
using Avalonia.Headless;

namespace CodexThemeStudio.Desktop.MacOS.Tests.Headless;

internal static class AvaloniaTestHost
{
    private static readonly Lazy<HeadlessUnitTestSession> Session =
        new(() => HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestHost),
            AvaloniaTestIsolationLevel.PerAssembly));

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder
            .Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false,
            });

    public static Task DispatchAsync(Func<Task> action) =>
        Session.Value.Dispatch(
            async () =>
            {
                await action();
                return true;
            },
            CancellationToken.None);

    public static Task DispatchAsync(Action action) =>
        Session.Value.Dispatch(
            () =>
            {
                action();
                return true;
            },
            CancellationToken.None);
}
