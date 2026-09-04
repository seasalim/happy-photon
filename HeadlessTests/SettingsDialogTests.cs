using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SettingsDialogTests
{
    [AvaloniaFact]
    public async Task DialogAndTitleBarExposeSettingsEntryPoints()
    {
        var root = Path.Combine(
            Path.GetTempPath(), $"happy-photon-settings-{Guid.NewGuid():N}");
        using var catalog = new CatalogService(root);
        await catalog.InitializeAsync();
        var vm = new MainWindowViewModel(catalog);
        await vm.RestoreXmpSettingsAsync();
        var dialog = new SettingsDialog(vm);
        var titleBar = new HappyPhotonTitleBar { DataContext = vm };
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, dialog.FindControl<TabControl>("SettingsTabs")!.SelectedIndex);
        Assert.Equal(3, dialog.FindControl<ComboBox>("XmpModeChoice")!.ItemCount);
        Assert.Equal(3, dialog.FindControl<TabControl>("SettingsTabs")!.ItemCount);
        var button = titleBar.FindControl<Button>("SettingsButton")!;
        Assert.True(vm.AreXmpSettingsReady);
        Assert.True(button.IsEnabled);
        Assert.Equal("Settings", AutomationProperties.GetName(button));
        Assert.Contains(ShortcutCatalog.Groups.SelectMany(group => group.Entries),
            entry => entry.Keys == "Ctrl+," &&
                     entry.Action.Contains("Settings", StringComparison.Ordinal));

        dialog.Close();
        titleBar.DataContext = null;
        await vm.DisposeAsync();
        catalog.Dispose();
        Directory.Delete(root, recursive: true);
    }

    [AvaloniaFact]
    public void PackagedAppDataCatalog_ShowsUninstallWarning()
    {
        var local = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var catalogRoot = Path.Combine(local, "Happy Photon", "data");
        var cacheRoot = Path.Combine(local, "Happy Photon", "cache");
        var locations = new AppDataLocations(
            catalogRoot,
            cacheRoot,
            AppDataLocationOrigin.Persisted,
            AppDataLocationOrigin.Persisted);
        var pointerRoot = Path.Combine(
            Path.GetTempPath(), $"happy-photon-settings-pointer-{Guid.NewGuid():N}");
        var locationService = new AppDataLocationService(new AppDataPlatformPaths(
            Path.Combine(pointerRoot, "Pictures"),
            pointerRoot,
            Path.Combine(pointerRoot, "data"),
            Path.Combine(pointerRoot, "cache")));
        var storage = new StorageSettingsViewModel(
            locations,
            new CatalogLocationMigrator(locationService),
            isPackagedWindows: true);
        var panel = new StoragePanel { DataContext = storage };
        Dispatcher.UIThread.RunJobs();

        Assert.True(storage.ShowCatalogUninstallWarning);
        Assert.True(panel.FindControl<TextBlock>("CatalogUninstallWarning")!.IsVisible);
    }

    [AvaloniaFact]
    public void PendingAppDataDestination_ShowsUninstallWarning()
    {
        // Path-only assertions: temp lives under %LOCALAPPDATA%, so a fictitious
        // non-AppData root is required for the warning to start off.
        var root = Path.Combine("C:\\", $"happy-photon-pending-warning-{Guid.NewGuid():N}");
        var locations = new AppDataLocations(
            Path.Combine(root, "Pictures", "Happy Photon Catalog"),
            Path.Combine(root, "cache"),
            AppDataLocationOrigin.Persisted,
            AppDataLocationOrigin.Persisted);
        var service = new AppDataLocationService(new AppDataPlatformPaths(
            Path.Combine(root, "Pictures"),
            Path.Combine(root, "pointer"),
            Path.Combine(root, "data"),
            Path.Combine(root, "default-cache")));
        var storage = new StorageSettingsViewModel(
            locations,
            new CatalogLocationMigrator(service),
            isPackagedWindows: true);
        Assert.False(storage.ShowCatalogUninstallWarning);

        storage.PendingCatalogRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Happy Photon", "data");

        Assert.True(storage.ShowCatalogUninstallWarning);
    }

    [AvaloniaFact]
    public async Task StagingOverlappingCacheMove_ShowsErrorInsteadOfThrowing()
    {
        var root = Path.Combine(
            Path.GetTempPath(), $"happy-photon-overlap-move-{Guid.NewGuid():N}");
        var catalogRoot = Path.Combine(root, "catalog");
        var cacheRoot = Path.Combine(root, "cache");
        try
        {
            AppDataRootOwnership.Claim(catalogRoot);
            AppDataRootOwnership.Claim(cacheRoot);
            var destination = Path.Combine(catalogRoot, "Happy Photon Cache");
            AppDataRootOwnership.Claim(destination);
            var service = new AppDataLocationService(new AppDataPlatformPaths(
                Path.Combine(root, "Pictures"),
                Path.Combine(root, "pointer"),
                Path.Combine(root, "data"),
                Path.Combine(root, "default-cache")));
            var storage = new StorageSettingsViewModel(
                new AppDataLocations(
                    catalogRoot,
                    cacheRoot,
                    AppDataLocationOrigin.Persisted,
                    AppDataLocationOrigin.Persisted),
                new CatalogLocationMigrator(service),
                isPackagedWindows: false)
            {
                PendingCacheRoot = destination
            };

            var panel = new StoragePanel { DataContext = storage };

            await storage.MoveCacheCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(storage.CacheError);
            Assert.Null(storage.CatalogError);
            Assert.Null(storage.CacheStatus);
            Assert.True(panel.FindControl<TextBlock>("CacheError")!.IsVisible);
            Assert.False(panel.FindControl<TextBlock>("CacheStatus")!.IsVisible);
            Assert.False(File.Exists(Path.Combine(
                Path.Combine(root, "pointer"), "location-move.json")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void EnvironmentManagedRoots_DisableStorageChanges()
    {
        var root = Path.Combine(
            Path.GetTempPath(), $"happy-photon-managed-storage-{Guid.NewGuid():N}");
        var locations = new AppDataLocations(
            Path.Combine(root, "catalog"),
            Path.Combine(root, "cache"),
            AppDataLocationOrigin.Environment,
            AppDataLocationOrigin.Persisted);
        var service = new AppDataLocationService(new AppDataPlatformPaths(
            Path.Combine(root, "Pictures"),
            Path.Combine(root, "pointer"),
            Path.Combine(root, "data"),
            Path.Combine(root, "default-cache")));
        var storage = new StorageSettingsViewModel(
            locations,
            new CatalogLocationMigrator(service),
            isPackagedWindows: false);

        Assert.False(storage.ChangeCatalogCommand.CanExecute(null));
        Assert.False(storage.MoveCatalogCommand.CanExecute(null));
        Assert.False(storage.ChangeCacheCommand.CanExecute(null));
        Assert.False(storage.MoveCacheCommand.CanExecute(null));
        Assert.Contains(
            AppDataLocationService.CatalogEnvironmentVariable,
            storage.CatalogManagementNote);
        Assert.Contains(
            AppDataLocationService.CatalogEnvironmentVariable,
            storage.CacheManagementNote);
    }
}
