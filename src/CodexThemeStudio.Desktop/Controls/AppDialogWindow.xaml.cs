using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace CodexThemeStudio.Desktop.Controls;

internal enum AppDialogKind
{
    Confirmation,
    Information,
    Error,
    TextInput,
}

internal partial class AppDialogWindow : Window
{
    private AppDialogWindow(
        string title,
        string message,
        AppDialogKind kind,
        Window? owner)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => EnableNativeChrome();

        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;

        if (owner is not null)
        {
            Owner = owner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        ConfigureKind(kind);
    }

    internal bool Accepted { get; private set; }

    internal string InputText => InputTextBox.Text.Trim();

    internal void ClearInputFocus()
    {
        FocusManager.SetFocusedElement(this, DialogChrome);
        _ = Keyboard.Focus(DialogChrome);
    }

    internal static AppDialogWindow CreateConfirmation(
        string title,
        string message,
        Window? owner) =>
        new(title, message, AppDialogKind.Confirmation, owner);

    internal static AppDialogWindow CreateInformation(
        string title,
        string message,
        Window? owner,
        bool isError = false) =>
        new(
            title,
            message,
            isError ? AppDialogKind.Error : AppDialogKind.Information,
            owner);

    internal static AppDialogWindow CreateTextInput(
        string title,
        string prompt,
        string initialValue,
        Window? owner)
    {
        var dialog = new AppDialogWindow(
            title,
            prompt,
            AppDialogKind.TextInput,
            owner);
        dialog.InputTextBox.Text = initialValue;
        return dialog;
    }

    private void ConfigureKind(AppDialogKind kind)
    {
        switch (kind)
        {
            case AppDialogKind.Confirmation:
                break;
            case AppDialogKind.Information:
                CancelButton.Visibility = Visibility.Collapsed;
                ConfirmButton.Content = "知道了";
                break;
            case AppDialogKind.Error:
                CancelButton.Visibility = Visibility.Collapsed;
                ConfirmButton.Content = "关闭";
                break;
            case AppDialogKind.TextInput:
                InputTextBox.Visibility = Visibility.Visible;
                Loaded += (_, _) =>
                {
                    InputTextBox.Focus();
                    InputTextBox.SelectAll();
                };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        Accepted = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Accepted = false;
        Close();
    }

    private void DialogChrome_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (FindVisualAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        ClearInputFocus();
    }

    private static T? FindVisualAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private void EnableNativeChrome()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var enabled = 1;
        _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));

        var roundedCornerPreference = 2;
        _ = DwmSetWindowAttribute(
            handle,
            33,
            ref roundedCornerPreference,
            sizeof(int));
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int value,
        int valueSize);
}
