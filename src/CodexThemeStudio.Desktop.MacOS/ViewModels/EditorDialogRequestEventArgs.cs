namespace CodexThemeStudio.Desktop.MacOS.ViewModels;

public sealed class EditorDialogRequestEventArgs<T> : EventArgs
{
    private readonly TaskCompletionSource<T> completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<T> Completion => completion.Task;

    public void Complete(T result) => completion.TrySetResult(result);
}
