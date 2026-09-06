using CodexThemeStudio.Desktop.MacOS.Infrastructure;

namespace CodexThemeStudio.Desktop.MacOS.ViewModels;

public sealed class SettingsWindowViewModel : ObservableObject
{
    private bool isDiagnosticsSelected;

    public SettingsWindowViewModel()
    {
        ShowGeneralCommand = new RelayCommand(
            () => IsDiagnosticsSelected = false,
            () => IsDiagnosticsSelected);
        ShowDiagnosticsCommand = new RelayCommand(
            () => IsDiagnosticsSelected = true,
            () => !IsDiagnosticsSelected);
    }

    public RelayCommand ShowGeneralCommand { get; }

    public RelayCommand ShowDiagnosticsCommand { get; }

    public bool IsDiagnosticsSelected
    {
        get => isDiagnosticsSelected;
        private set
        {
            if (SetProperty(ref isDiagnosticsSelected, value))
            {
                OnPropertyChanged(nameof(IsGeneralSelected));
                ShowGeneralCommand.RaiseCanExecuteChanged();
                ShowDiagnosticsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsGeneralSelected => !IsDiagnosticsSelected;
}
