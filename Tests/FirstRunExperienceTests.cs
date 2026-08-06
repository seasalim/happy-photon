using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class FirstRunExperienceTests : IDisposable
{
    private readonly string _testRoot =
        Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-first-run-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public void StartupDecision_DistinguishesNewExistingAndCompletedInstalls()
    {
        Assert.Equal(
            FirstRunStartupDecision.ShowWelcome,
            MainWindowViewModel.DecideFirstRunStartup(new AppSettings()));
        Assert.Equal(
            FirstRunStartupDecision.GrandfatherExistingInstallation,
            MainWindowViewModel.DecideFirstRunStartup(new AppSettings
            {
                RootFolderPath = _testRoot
            }));
        Assert.Equal(
            FirstRunStartupDecision.Restore,
            MainWindowViewModel.DecideFirstRunStartup(new AppSettings
            {
                FirstRunExperienceVersion = 1
            }));
    }

    [Fact]
    public async Task Completion_PersistsBeforeOpeningWorkspace()
    {
        using var catalog = new CatalogService(Path.Combine(_testRoot, "catalog"));
        var vm = new MainWindowViewModel(catalog);
        string? persistedPath = null;
        var focusRequested = false;
        vm.PersistFirstRunCompletionAsync = path =>
        {
            persistedPath = path;
            return Task.CompletedTask;
        };
        vm.RequestFolderTreeFocus = () => focusRequested = true;
        vm.ShowFirstRunWelcome(_testRoot);

        await vm.CompleteFirstRunFromLocationAsync(_testRoot);

        Assert.Equal(_testRoot, persistedPath);
        Assert.Equal(StartupGateState.Ready, vm.StartupGateState);
        Assert.Equal(1, vm.FirstRunExperienceVersion);
        Assert.True(vm.CanPersistFolderSession);
        Assert.True(focusRequested);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task CompletionFailure_KeepsWelcomeVisible()
    {
        using var catalog = new CatalogService(Path.Combine(_testRoot, "catalog"));
        var vm = new MainWindowViewModel(catalog);
        vm.PersistFirstRunCompletionAsync =
            _ => Task.FromException(new IOException("write failed"));
        vm.ShowFirstRunWelcome(_testRoot);

        await vm.CompleteFirstRunFromLocationAsync(_testRoot);

        Assert.True(vm.IsFirstRunVisible);
        Assert.False(vm.CanPersistFolderSession);
        Assert.NotNull(vm.FirstRunErrorMessage);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task CatalogLocation_IsRejectedWithoutPersistingCompletion()
    {
        var catalogPath = Directory.CreateDirectory(
            Path.Combine(_testRoot, "catalog")).FullName;
        using var catalog = new CatalogService(catalogPath);
        var vm = new MainWindowViewModel(catalog);
        var persistenceRequested = false;
        vm.PersistFirstRunCompletionAsync = _ =>
        {
            persistenceRequested = true;
            return Task.CompletedTask;
        };
        vm.ShowFirstRunWelcome(_testRoot);

        await vm.CompleteFirstRunFromLocationAsync(catalogPath);

        Assert.False(persistenceRequested);
        Assert.True(vm.IsFirstRunVisible);
        Assert.NotNull(vm.FirstRunErrorMessage);
        await vm.DisposeAsync();
    }

    [Fact]
    public void BrowseRequest_DoesNotCompleteFirstRun()
    {
        using var catalog = new CatalogService(Path.Combine(_testRoot, "catalog"));
        var vm = new MainWindowViewModel(catalog);
        var browseRequested = false;
        vm.BrowseLocationRequested = () => browseRequested = true;
        vm.ShowFirstRunWelcome(_testRoot);

        vm.BrowseElsewhereCommand.Execute(null);

        Assert.True(browseRequested);
        Assert.True(vm.IsFirstRunVisible);
        Assert.Null(vm.FirstRunExperienceVersion);
    }

    [Fact]
    public async Task PreparedTree_DoesNotSelectOrLoadDefaultFolder()
    {
        using var catalog = new CatalogService(Path.Combine(_testRoot, "catalog"));
        var vm = new MainWindowViewModel(catalog);

        await vm.InitializeFolderTreeWithRootAsync(
            _testRoot,
            selectFolder: false);

        Assert.Single(vm.RootFolders);
        Assert.Null(vm.SelectedFolder);
        Assert.Null(vm.CurrentFolderPath);
        await vm.DisposeAsync();
    }

    [Fact]
    public void MissingPictures_UsesPickerLedWelcome()
    {
        using var catalog = new CatalogService(Path.Combine(_testRoot, "catalog"));
        var vm = new MainWindowViewModel(catalog);

        vm.ShowFirstRunWelcome(null);

        Assert.True(vm.IsPickerLedFirstRun);
        Assert.False(vm.HasDefaultFirstRunLocation);
    }

    [Fact]
    public void StartupFailure_PreservesActionableMessage()
    {
        using var catalog = new CatalogService(Path.Combine(_testRoot, "catalog"));
        var vm = new MainWindowViewModel(catalog);

        vm.ShowStartupFailure("Move the incompatible catalog aside, then retry.");

        Assert.True(vm.IsStartupError);
        Assert.Equal(
            "Move the incompatible catalog aside, then retry.",
            vm.FirstRunErrorMessage);
        Assert.False(vm.CanPersistFolderSession);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }
}
