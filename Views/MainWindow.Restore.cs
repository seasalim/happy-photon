using HappyPhoton.Services;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    internal Func<Task>? BeforeStartupSettingsLoad { get; set; }
    internal Func<string, Task>? ShowRestoreNotice { get; set; }

    private async Task ChooseRestoreBackupAsync(MainWindowViewModel vm)
    {
        if (_startupAttemptInProgress || _startupLocations == null || !vm.IsStartupError) return;
        var choice = await new RestoreBackupDialog(_startupLocations.CatalogRoot)
            .ShowDialog<(string Path, bool Acknowledged, CheckedCatalogBackup Check)?>(this);
        if (choice is { } chosen) await RestoreCatalogAsync(vm, chosen.Path, chosen.Acknowledged, chosen.Check);
    }

    internal async Task RestoreCatalogAsync(MainWindowViewModel vm, string path, bool acknowledged,
        CheckedCatalogBackup? check = null)
    {
        if (_startupAttemptInProgress || _startupLocations == null || _locationMigrator == null ||
            _dataLocationService == null || !vm.IsStartupError) return;
        var executor = new CatalogRestoreExecutor(_locationMigrator) { Step = _locationMigrator.RestoreStep };
        await executor.StageAsync(_startupLocations, path, check, acknowledged);
        vm.ShowInitializing();
        _appSettingsService = null;
        BackupOnQuitAsync = null;
        DataContext = null;
        await vm.DisposeAsync();
        await _appSettingsSaveGate.WaitAsync();
        _appSettingsSaveGate.Release();
        _startupCatalogService?.Dispose();
        var catalog = new CatalogService();
        var fresh = new MainWindowViewModel(catalog);
        DataContext = fresh;
        await InitializeApplicationAsync(fresh, catalog, _dataLocationService, _locationMigrator, _startupPicturesPath);
    }

    private async Task PresentRestoreNoticeAsync(MainWindowViewModel vm)
    {
        if (_startupLocations == null || !ReferenceEquals(DataContext, vm)) return;
        try
        {
            var root = _startupLocations.CatalogRoot;
            if (CatalogRestoreExecutor.ReadNotice(root) is not { } notice) return;
            if (ShowRestoreNotice is { } show) await show(notice);
            else await ConfirmationDialog.ShowMessageAsync(this, "Catalog restored", notice);
            CatalogRestoreExecutor.AcknowledgeNotice(root);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Restore notice: {ex.Message}"); }
    }
}
