using Avalonia.Controls;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_closeReady || DataContext is not MainWindowViewModel vm)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        if (_isClosing) return;

        _isClosing = true;
        SaveWindowPlacement();
        vm.ExitCompareCommand.Execute(null);
        await PersistAppSettingsSafelyAsync(vm);

        Hide();
        DataContext = null;
        await Dispatcher.UIThread.InvokeAsync(
            static () => { },
            DispatcherPriority.Loaded);

        try
        {
            await vm.DisposeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Image service shutdown failed: {ex.Message}");
        }

        _closeReady = true;
        Close();
    }

    private async Task PersistAppSettingsSafelyAsync(MainWindowViewModel vm)
    {
        try
        {
            await SaveAppSettingsAsync(vm);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App settings persistence failed: {ex.Message}");
        }
    }

    private Task SaveAppSettingsAsync(MainWindowViewModel vm)
    {
        if (_appSettingsService == null)
        {
            return Task.CompletedTask;
        }

        var settings = new AppSettings
        {
            RootFolderPath = vm.RootFolders.FirstOrDefault()?.Path,
            SelectedFolderPath = vm.CurrentFolderPath,
            FirstRunExperienceVersion = vm.FirstRunExperienceVersion,
            FileTypeFilter = vm.Browse.FileTypeFilter,
            BrowseThumbnailSize = vm.BrowseThumbnailSize,
            ShowCapturePairs = vm.ShowCapturePairs,
            AppTheme = vm.AppTheme,
            StripLocationData = vm.ExportSettings.StripLocationData,
            OutputSharpening = vm.ExportSettings.OutputSharpening
        };

        return vm.CanPersistFolderSession
            ? _appSettingsService.SaveAsync(settings)
            : _appSettingsService.SavePreferencesAsync(settings);
    }
}
