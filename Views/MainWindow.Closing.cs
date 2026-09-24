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
        vm.CancelWatermarkSettingsSave();
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

    private readonly SemaphoreSlim _appSettingsSaveGate = new(1, 1);

    private async Task SaveAppSettingsAsync(MainWindowViewModel vm)
    {
        await _appSettingsSaveGate.WaitAsync();
        try { await SaveCurrentAppSettingsAsync(vm); }
        finally { _appSettingsSaveGate.Release(); }
    }

    private Task SaveCurrentAppSettingsAsync(MainWindowViewModel vm)
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
            Watermark = vm.ExportSettings.Watermark.Capture(),
            WatermarkEnabled = vm.ExportSettings.Watermark.Enabled,
            OutputSharpening = vm.ExportSettings.OutputSharpening
        };

        vm.CaptureBrushPreferences(settings);
        return vm.CanPersistFolderSession
            ? _appSettingsService.SaveAsync(settings)
            : _appSettingsService.SavePreferencesAsync(settings);
    }
}
