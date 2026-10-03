using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    internal Func<Task>? BackupOnQuitAsync { get; set; }

    internal void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_closeReady) return;
        e.Cancel = true;
        Close();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_isClosing && !_closeReady) { e.Cancel = true; return; }
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

        try
        {
            _locationMigrator?.RestoreStep?.Invoke("before:quit-backup");
            // Match the two-second shutdown drains. Staging still running then skips the quit
            // backup; one that failed earlier must not, so wait for it to settle, not succeed.
            var staging = _locationMigrator?.PendingStaging ?? Task.CompletedTask;
            var stagingSettled = await Task.WhenAny(staging, Task.Delay(TimeSpan.FromSeconds(2))) == staging;
            if (stagingSettled && vm.CanPersistFolderSession && vm.StartupGateState == StartupGateState.Ready &&
                !(_locationMigrator != null && File.Exists(_locationMigrator.JournalPath) &&
                  (await _locationMigrator.ReadJournalAsync()).Kind == CatalogLocationMoveKind.Restore) &&
                BackupOnQuitAsync is { } backup) await backup();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Catalog backup failed: {ex.Message}");
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
            PasteGroups = vm.CapturePasteGroups(),
            PresetGroups = new(vm.PresetGroups),
            DevelopGroups = vm.CaptureDevelopGroups(),
            StripLocationData = vm.ExportSettings.StripLocationData,
            Watermark = vm.ExportSettings.Watermark.Capture(),
            WatermarkEnabled = vm.ExportSettings.Watermark.Enabled,
            OutputSharpening = vm.ExportSettings.OutputSharpening
        };

        vm.CaptureBrushPreferences(settings);
        var saveTips = vm.CaptureTipsSettings(settings);

        return vm.CanPersistFolderSession
            ? _appSettingsService.SaveAsync(settings, saveTips)
            : _appSettingsService.SavePreferencesAsync(settings, saveTips);
    }
}
