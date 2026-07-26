using System.Windows;
using System.Windows.Threading;
using CodexThemeStudio.Desktop.Converters;

namespace CodexThemeStudio.Desktop.Tests;

internal static class WpfTestHost
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(15);
    private static readonly TaskCompletionSource<Dispatcher> Ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private static readonly Thread Thread = StartThread();

    public static void Invoke(Action action)
    {
        GetDispatcher().Invoke(action);
    }

    public static Task InvokeAsync(Func<Task> action)
    {
        return GetDispatcher().InvokeAsync(action).Task.Unwrap();
    }

    private static Dispatcher GetDispatcher()
    {
        _ = Thread;

        try
        {
            return Ready.Task.WaitAsync(StartupTimeout).GetAwaiter().GetResult();
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException(
                "WPF test host did not finish starting within 15 seconds.",
                exception);
        }
    }

    private static Thread StartThread()
    {
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                };
                foreach (var source in new[]
                    {
                         "Themes/DesignTokens.xaml",
                         "Themes/Icons.xaml",
                         "Themes/ComponentStyles.xaml",
                     })
                {
                    app.Resources.MergedDictionaries.Add(
                        new ResourceDictionary
                        {
                            Source = new Uri(
                                $"pack://application:,,,/CodexThemeStudio.Desktop;component/{source}",
                                UriKind.Absolute),
                        });
                }

                app.Resources["CssColorBrushConverter"] =
                    new CssColorBrushConverter();
                Ready.TrySetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }
            catch (Exception exception)
            {
                Ready.TrySetException(new InvalidOperationException(
                    "WPF test host failed while initializing application resources.",
                    exception));
            }
        })
        {
            IsBackground = true,
            Name = "WPF test host",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread;
    }
}
