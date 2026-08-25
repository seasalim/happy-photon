using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class FirstRunWindowTests
{
    [AvaloniaFact]
    public async Task StartupGate_SuspendsAndRestoresWorkspaceKeyBindings()
    {
        using var catalog = new CatalogService(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-window-{Guid.NewGuid():N}"));
        var vm = new MainWindowViewModel(catalog);
        var window = new MainWindow
        {
            DataContext = vm
        };

        Assert.False(window.WorkspaceKeyboardEnabled);
        Assert.Empty(window.KeyBindings);
        Assert.False(
            window.FindControl<FolderTreePanel>("FolderTreePanel")!.IsEffectivelyEnabled);

        vm.ShowWorkspaceReady(1);

        Assert.True(window.WorkspaceKeyboardEnabled);
        Assert.NotEmpty(window.KeyBindings);
        Assert.True(
            window.FindControl<FolderTreePanel>("FolderTreePanel")!.IsEffectivelyEnabled);

        window.DataContext = null;
        window.Close();
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Wizard_FocusFollowsForwardOnlyStepChanges()
    {
        using var catalog = new CatalogService(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-window-{Guid.NewGuid():N}"));
        var vm = new MainWindowViewModel(catalog);
        vm.ShowFirstRunWelcome(Path.GetTempPath());
        var view = new FirstRunView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(view.FindControl<Button>("WelcomeContinueButton")!.IsFocused);
        await vm.ContinueFirstRunCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(FirstRunStep.Storage, vm.FirstRunStep);
        Assert.True(view.FindControl<Button>("StorageContinueButton")!.IsFocused);
        Assert.DoesNotContain(
            view.GetLogicalDescendants().OfType<Button>(),
            button => Equals(button.Content, "LOCATE EXISTING CATALOG"));
        view.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Left,
            KeyModifiers = KeyModifiers.Alt
        });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(FirstRunStep.Storage, vm.FirstRunStep);
        Assert.True(view.FindControl<Button>("StorageContinueButton")!.IsFocused);
        vm.ResumeFirstRunAfterStorage(Path.GetTempPath());
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(FirstRunStep.Pictures, vm.FirstRunStep);
        Assert.True(view.FindControl<Button>("PicturesDefaultButton")!.IsFocused);
        await vm.CompleteFirstRunFromLocationAsync(Path.GetTempPath());
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(FirstRunStep.AllSet, vm.FirstRunStep);
        Assert.True(view.FindControl<Button>("StartTourButton")!.IsFocused);
        window.Content = null;
        window.Close();
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task FreshStartup_CreatesCatalogOnlyAfterStorageContinues()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-window-{Guid.NewGuid():N}"));
        var pictures = Directory.CreateDirectory(Path.Combine(root.FullName, "pictures"));
        var locationService = new AppDataLocationService(new AppDataPlatformPaths(
            pictures.FullName,
            Path.Combine(root.FullName, "pointer"),
            Path.Combine(root.FullName, "data"),
            Path.Combine(root.FullName, "cache")));
        var migrator = new CatalogLocationMigrator(locationService);
        using var catalog = new CatalogService();
        var vm = new MainWindowViewModel(catalog);
        var window = new MainWindow { DataContext = vm };

        await window.InitializeApplicationAsync(
            vm, catalog, locationService, migrator, pictures.FullName);

        Assert.Equal(FirstRunStep.Welcome, vm.FirstRunStep);
        Assert.False(File.Exists(locationService.PointerPath));
        Assert.False(File.Exists(Path.Combine(
            locationService.DefaultCatalogRoot, "catalog.db")));
        await vm.ContinueFirstRunCommand.ExecuteAsync(null);
        Assert.Equal(FirstRunStep.Storage, vm.FirstRunStep);
        Assert.False(File.Exists(locationService.PointerPath));

        await vm.ContinueFirstRunCommand.ExecuteAsync(null);

        Assert.Equal(FirstRunStep.Pictures, vm.FirstRunStep);
        Assert.True(vm.IsFirstRunStorageCommitted);
        Assert.True(File.Exists(locationService.PointerPath));
        Assert.True(File.Exists(Path.Combine(
            locationService.DefaultCatalogRoot, "catalog.db")));
        window.DataContext = null;
        window.Close();
        await vm.DisposeAsync();
        catalog.Dispose();
        Directory.Delete(root.FullName, recursive: true);
    }

    [AvaloniaFact]
    public async Task ConfiguredEmptyRoot_ShowsReadOnlyStorageBeforeInitialization()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-window-{Guid.NewGuid():N}"));
        var pictures = Directory.CreateDirectory(Path.Combine(root.FullName, "pictures"));
        var locationService = new AppDataLocationService(new AppDataPlatformPaths(
            pictures.FullName,
            Path.Combine(root.FullName, "pointer"),
            Path.Combine(root.FullName, "data"),
            Path.Combine(root.FullName, "cache")));
        var configured = await locationService.CreateFreshAsync();
        var migrator = new CatalogLocationMigrator(locationService);
        using var catalog = new CatalogService();
        var vm = new MainWindowViewModel(catalog);
        var window = new MainWindow { DataContext = vm };

        await window.InitializeApplicationAsync(
            vm, catalog, locationService, migrator, pictures.FullName);

        Assert.Equal(FirstRunStep.Welcome, vm.FirstRunStep);
        Assert.True(vm.IsFirstRunStorageReadOnly);
        Assert.False(File.Exists(configured.DatabasePath));
        await vm.ContinueFirstRunCommand.ExecuteAsync(null);
        await vm.ContinueFirstRunCommand.ExecuteAsync(null);

        Assert.Equal(FirstRunStep.Pictures, vm.FirstRunStep);
        Assert.True(File.Exists(configured.DatabasePath));
        window.DataContext = null;
        window.Close();
        await vm.DisposeAsync();
        catalog.Dispose();
        Directory.Delete(root.FullName, recursive: true);
    }

    [AvaloniaFact]
    public async Task PersistedMissingRoot_RecoversToEditableFreshWizard()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-window-{Guid.NewGuid():N}"));
        var pictures = Directory.CreateDirectory(Path.Combine(root.FullName, "pictures"));
        var locationService = new AppDataLocationService(new AppDataPlatformPaths(
            pictures.FullName,
            Path.Combine(root.FullName, "pointer"),
            Path.Combine(root.FullName, "data"),
            Path.Combine(root.FullName, "cache")));
        var missing = await locationService.CreateFreshAsync();
        Directory.Delete(missing.CatalogRoot, recursive: true);
        var migrator = new CatalogLocationMigrator(locationService);
        using var catalog = new CatalogService();
        var vm = new MainWindowViewModel(catalog);
        var window = new MainWindow { DataContext = vm };

        await window.InitializeApplicationAsync(
            vm, catalog, locationService, migrator, pictures.FullName);

        Assert.Equal(StartupGateState.PointerRecovery, vm.StartupGateState);
        Assert.Contains(missing.CatalogRoot, vm.FirstRunErrorMessage);

        await vm.RecoverLocationPointerCommand.ExecuteAsync(null);

        Assert.Equal(StartupGateState.Welcome, vm.StartupGateState);
        Assert.Equal(FirstRunStep.Welcome, vm.FirstRunStep);
        Assert.False(vm.IsFirstRunStorageReadOnly);
        await vm.ContinueFirstRunCommand.ExecuteAsync(null);
        Assert.True(vm.CanChangeFirstRunStorage);
        Assert.False(File.Exists(locationService.PointerPath));
        Assert.Single(Directory.EnumerateFiles(
            Path.GetDirectoryName(locationService.PointerPath)!, "*.corrupt"));
        window.DataContext = null;
        window.Close();
        await vm.DisposeAsync();
        catalog.Dispose();
        Directory.Delete(root.FullName, recursive: true);
    }

    [AvaloniaFact]
    public async Task EnvironmentOnlyMissingRoot_ReachesWizard()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-window-{Guid.NewGuid():N}"));
        var pictures = Directory.CreateDirectory(Path.Combine(root.FullName, "pictures"));
        var environmentCatalog = Path.Combine(root.FullName, "environment-catalog");
        var locationService = new AppDataLocationService(
            new AppDataPlatformPaths(
                pictures.FullName,
                Path.Combine(root.FullName, "pointer"),
                Path.Combine(root.FullName, "data"),
                Path.Combine(root.FullName, "cache")),
            name => name == AppDataLocationService.CatalogEnvironmentVariable
                ? environmentCatalog
                : null);
        var migrator = new CatalogLocationMigrator(locationService);
        using var catalog = new CatalogService();
        var vm = new MainWindowViewModel(catalog);
        var window = new MainWindow { DataContext = vm };

        Assert.False(Directory.Exists(environmentCatalog));
        await window.InitializeApplicationAsync(
            vm, catalog, locationService, migrator, pictures.FullName);

        Assert.Equal(StartupGateState.Welcome, vm.StartupGateState);
        Assert.Equal(FirstRunStep.Welcome, vm.FirstRunStep);
        Assert.True(vm.IsFirstRunStorageReadOnly);
        Assert.False(File.Exists(locationService.PointerPath));
        window.DataContext = null;
        window.Close();
        await vm.DisposeAsync();
        catalog.Dispose();
        Directory.Delete(root.FullName, recursive: true);
    }

}
