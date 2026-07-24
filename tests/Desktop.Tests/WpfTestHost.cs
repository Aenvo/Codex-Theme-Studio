using System.Windows;
using System.Windows.Threading;

namespace CodexThemeStudio.Desktop.Tests;

internal static class WpfTestHost
{
    private static readonly TaskCompletionSource<Dispatcher> Ready = new();
    private static readonly Thread Thread = StartThread();

    public static void Invoke(Action action)
    {
        _ = Thread;
        Ready.Task.GetAwaiter().GetResult().Invoke(action);
    }

    public static Task InvokeAsync(Func<Task> action)
    {
        _ = Thread;
        var dispatcher = Ready.Task.GetAwaiter().GetResult();
        return dispatcher.InvokeAsync(action).Task.Unwrap();
    }

    private static Thread StartThread()
    {
        var thread = new Thread(() =>
        {
            var app = new App
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };
            app.InitializeComponent();
            Ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
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
