using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace CodexThemeStudio.Desktop.Services;

public sealed class SystemThemeService : IDisposable
{
    private readonly Application application;
    private bool isListening;

    public SystemThemeService(Application application)
    {
        this.application = application;
    }

    public void Start()
    {
        ApplyCurrentTheme();
        if (!isListening)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            isListening = true;
        }
    }

    public void Dispose()
    {
        if (isListening)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            isListening = false;
        }
    }

    private void OnUserPreferenceChanged(
        object sender,
        UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or
            UserPreferenceCategory.VisualStyle)
        {
            application.Dispatcher.BeginInvoke(ApplyCurrentTheme);
        }
    }

    private void ApplyCurrentTheme()
    {
        var useDarkTheme = IsSystemDarkTheme();
        SetBrush("WindowBrush", useDarkTheme ? "#0F172A" : "#F5F7FB");
        SetBrush("SurfaceBrush", useDarkTheme ? "#172033" : "#FFFFFF");
        SetBrush("SurfaceMutedBrush", useDarkTheme ? "#263247" : "#EEF2F7");
        SetBrush("TextBrush", useDarkTheme ? "#F8FAFC" : "#172033");
        SetBrush("MutedTextBrush", useDarkTheme ? "#A8B3C7" : "#657087");
        SetBrush("BorderBrush", useDarkTheme ? "#354258" : "#DDE3EC");
        SetBrush("AccentSoftBrush", useDarkTheme ? "#312E66" : "#EAE9FF");
    }

    private void SetBrush(string key, string color) =>
        application.Resources[key] =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

    private static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception exception)
            when (exception is UnauthorizedAccessException or
                System.Security.SecurityException or IOException)
        {
            return false;
        }
    }
}
