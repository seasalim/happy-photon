using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private bool _restoringAppTheme;

    [ObservableProperty]
    private AppTheme _appTheme = AppTheme.Dark;

    [ObservableProperty]
    private bool _isAppearanceSettingsReady;

    public bool IsDarkTheme => AppTheme == AppTheme.Dark;
    public bool IsMidGreyTheme => AppTheme == AppTheme.MidGrey;

    public void RestoreAppTheme(AppTheme value)
    {
        _restoringAppTheme = true;
        try
        {
            AppTheme = value;
        }
        finally
        {
            _restoringAppTheme = false;
        }

        IsAppearanceSettingsReady = true;
    }

    partial void OnAppThemeChanged(AppTheme value)
    {
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IsMidGreyTheme));

        if (!_restoringAppTheme && IsAppearanceSettingsReady)
        {
            _ = PersistAppThemeAsync();
        }
    }

    [RelayCommand]
    private void SetAppTheme(AppTheme theme)
    {
        if (IsAppearanceSettingsReady && Enum.IsDefined(theme))
        {
            AppTheme = theme;
        }
    }

    private async Task PersistAppThemeAsync()
    {
        try
        {
            if (PersistAppSettingsAsync != null)
            {
                await PersistAppSettingsAsync();
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Theme persistence failed: {exception.Message}");
        }
    }
}
