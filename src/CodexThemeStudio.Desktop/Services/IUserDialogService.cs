namespace CodexThemeStudio.Desktop.Services;

public interface IUserDialogService
{
    Task<string?> RequestTextAsync(
        string title,
        string prompt,
        string initialValue,
        CancellationToken cancellationToken);

    Task<bool> ConfirmAsync(
        string title,
        string message,
        CancellationToken cancellationToken);

    Task<string?> PickOpenFileAsync(
        string title,
        string filter,
        CancellationToken cancellationToken);

    Task<string?> PickSaveFileAsync(
        string title,
        string filter,
        string defaultExtension,
        string suggestedFileName,
        CancellationToken cancellationToken);

    Task<string?> PickFolderAsync(
        string title,
        string? initialDirectory,
        CancellationToken cancellationToken);

    void ShowInformation(string title, string message);
}
