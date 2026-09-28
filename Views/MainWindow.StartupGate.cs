using Avalonia.Input;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    private List<KeyBinding>? _suspendedWorkspaceKeyBindings;
    private bool _workspaceKeyboardEnabled = true;

    internal bool WorkspaceKeyboardEnabled => _workspaceKeyboardEnabled;

    internal async Task InitializeApplicationAsync(
        MainWindowViewModel vm,
        CatalogService catalogService,
        AppDataLocationService locationService,
        CatalogLocationMigrator locationMigrator,
        string? picturesPath)
    {
        _startupCatalogService = catalogService;
        BackupOnQuitAsync = new CatalogBackupService(catalogService).BackupIfDueAsync;
        _dataLocationService = locationService;
        _locationMigrator = locationMigrator;
        vm.BindDataLocationService(locationService);
        _startupPicturesPath = picturesPath;
        _appSettingsService = new AppSettingsService(catalogService);
        await TryInitializeApplicationAsync(vm);
    }

    private void ApplyWorkspaceKeyboardState(bool isEnabled)
    {
        if (_workspaceKeyboardEnabled == isEnabled)
        {
            return;
        }

        _workspaceKeyboardEnabled = isEnabled;
        if (!isEnabled)
        {
            _suspendedWorkspaceKeyBindings = KeyBindings.ToList();
            KeyBindings.Clear();
            return;
        }

        if (_suspendedWorkspaceKeyBindings == null)
        {
            return;
        }

        foreach (var keyBinding in _suspendedWorkspaceKeyBindings)
        {
            KeyBindings.Add(keyBinding);
        }
        _suspendedWorkspaceKeyBindings = null;
    }
}
