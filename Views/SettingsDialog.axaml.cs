using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HappyPhoton.ViewModels;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog() : this(null)
    {
    }

    public SettingsDialog(MainWindowViewModel? viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        if (viewModel != null)
        {
            ThemeChoice.SelectedIndex = (int)viewModel.AppTheme;
            XmpModeChoice.SelectedIndex = (int)viewModel.XmpSidecarMode;
            XmpNamingChoice.SelectedIndex = (int)viewModel.XmpSidecarNaming;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm &&
            Enum.IsDefined((AppTheme)ThemeChoice.SelectedIndex))
        {
            vm.AppTheme = (AppTheme)ThemeChoice.SelectedIndex;
        }
    }

    private void OnXmpModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm &&
            Enum.IsDefined((XmpSidecarMode)XmpModeChoice.SelectedIndex))
        {
            vm.XmpSidecarMode = (XmpSidecarMode)XmpModeChoice.SelectedIndex;
        }
    }

    private void OnXmpNamingChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm &&
            Enum.IsDefined((XmpSidecarNaming)XmpNamingChoice.SelectedIndex))
        {
            vm.XmpSidecarNaming =
                (XmpSidecarNaming)XmpNamingChoice.SelectedIndex;
        }
    }
}
