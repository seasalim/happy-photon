using Avalonia.Platform.Storage;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    private readonly LightroomDetectionService _lightroomDetectionService = new();

    private async Task CompleteDataLocationSetupAsync(MainWindowViewModel vm)
    {
        if (_dataLocationService == null ||
            string.IsNullOrWhiteSpace(vm.SetupCatalogRoot) ||
            string.IsNullOrWhiteSpace(vm.SetupCacheRoot))
        {
            throw new InvalidOperationException("Choose both storage locations.");
        }

        if (_startupLocations == null)
        {
            await _dataLocationService.CreateFreshAsync(
                catalogRoot: vm.SetupCatalogRoot,
                cacheRoot: vm.SetupCacheRoot);
        }

        vm.MarkFirstRunStorageCommitted();
        await TryInitializeApplicationAsync(vm);
    }

    private async Task ChangeSetupLocationAsync(
        MainWindowViewModel vm,
        bool catalog)
    {
        var suggestedStart = catalog
            ? _dataLocationService?.StandardCatalogRoot
            : _dataLocationService?.StandardCacheRoot;
        var destination = await PickDataLocationAsync(catalog, suggestedStart);
        if (destination == null || !ReferenceEquals(DataContext, vm)) return;
        if (catalog) vm.SetupCatalogRoot = destination;
        else vm.SetupCacheRoot = destination;
    }

    private async Task RecoverLocationPointerAsync(MainWindowViewModel vm)
    {
        if (_dataLocationService == null) return;
        await _dataLocationService.QuarantineCorruptPointerAsync();
        await TryInitializeApplicationAsync(vm);
    }

    private async Task SetAsideCatalogAsync(MainWindowViewModel vm)
    {
        if (_locationMigrator == null || _startupLocations == null) return;
        await _locationMigrator.StageMoveAsync(
            _startupLocations,
            CatalogLocationMoveKind.SetAside);
        await TryInitializeApplicationAsync(vm);
    }

    private void ConfigureStorageSettings(MainWindowViewModel vm)
    {
        if (vm.StorageSettings == null) return;
        vm.StorageSettings.RequestDestinationAsync =
            catalog => PickDataLocationAsync(catalog);
    }

    private async Task<string?> PickDataLocationAsync(
        bool catalog,
        string? suggestedStartPath = null)
    {
        var parent = await PickRawFolderAsync(
            catalog ? "Choose a Catalog Parent Folder" : "Choose a Cache Parent Folder",
            suggestedStartPath);
        return parent == null
            ? null
            : AppDataRootOwnership.CreateDedicatedChild(
                parent,
                catalog ? "Happy Photon Catalog" : "Happy Photon Cache",
                _startupLocations == null
                    ? null
                    : [_startupLocations.CatalogRoot, _startupLocations.CacheRoot]);
    }

    private async Task<string?> PickRawFolderAsync(
        string title,
        string? suggestedStartPath = null)
    {
        var suggestedStart = await TryGetSuggestedStartFolderAsync(suggestedStartPath);
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                SuggestedStartLocation = suggestedStart
            });
        return folders.Count == 0 ? null : folders[0].Path.LocalPath;
    }

    private async Task<IStorageFolder?> TryGetSuggestedStartFolderAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var current = Path.GetFullPath(path);
            while (!string.IsNullOrWhiteSpace(current))
            {
                var folder = await StorageProvider.TryGetFolderFromPathAsync(
                    new Uri(current));
                if (folder != null) return folder;
                current = Directory.GetParent(current)?.FullName;
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // A picker remains usable without a suggested start location.
        }
        return null;
    }

    private Task<LightroomDetectionResult> DetectLightroomAsync(
        string picturesRoot,
        CancellationToken cancellationToken) =>
        _lightroomDetectionService.DetectAsync(
            picturesRoot,
            _startupLocations?.CatalogRoot,
            cancellationToken);

    private async Task<bool> ShowFirstRunImportCatalogAsync(string? catalogPath)
        => await ShowImportCatalogAsync(catalogPath, returnAppliedOnClose: true);
}
