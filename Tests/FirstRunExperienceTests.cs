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
        vm.ResumeFirstRunAfterStorage(_testRoot);

        await vm.CompleteFirstRunFromLocationAsync(_testRoot);

        Assert.Equal(FirstRunStep.AllSet, vm.FirstRunStep);
        Assert.Null(persistedPath);
        await vm.StartFirstRunTourCommand.ExecuteAsync(null);

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
        vm.ResumeFirstRunAfterStorage(_testRoot);

        await vm.CompleteFirstRunFromLocationAsync(_testRoot);
        await vm.StartFirstRunTourCommand.ExecuteAsync(null);

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
        vm.ResumeFirstRunAfterStorage(_testRoot);

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
    public void MissingPictures_UsesPickerLedPicturesStep()
    {
        using var catalog = new CatalogService(Path.Combine(_testRoot, "catalog"));
        var vm = new MainWindowViewModel(catalog);

        vm.ShowFirstRunWelcome(null);
        vm.ResumeFirstRunAfterStorage(null);

        Assert.True(vm.IsPickerLedFirstRun);
        Assert.False(vm.HasDefaultFirstRunLocation);
    }

    [Fact]
    public async Task Wizard_VisitsWelcomeStorageAndPicturesInOrder()
    {
        using var catalog = new CatalogService(Path.Combine(_testRoot, "catalog"));
        var vm = new MainWindowViewModel(catalog);
        var storageCommits = 0;
        vm.CompleteDataLocationSetupAsync = () =>
        {
            storageCommits++;
            vm.MarkFirstRunStorageCommitted();
            vm.ResumeFirstRunAfterStorage(_testRoot);
            return Task.CompletedTask;
        };
        vm.ShowFirstRunWelcome(_testRoot);

        Assert.Equal(FirstRunStep.Welcome, vm.FirstRunStep);
        await vm.ContinueFirstRunCommand.ExecuteAsync(null);
        Assert.Equal(FirstRunStep.Storage, vm.FirstRunStep);
        await vm.ContinueFirstRunCommand.ExecuteAsync(null);
        Assert.Equal(FirstRunStep.Pictures, vm.FirstRunStep);
        Assert.Equal(1, storageCommits);

        Assert.True(vm.IsFirstRunStorageReadOnly);
        vm.ResumeFirstRunAfterStorage(_testRoot);
        Assert.Equal(FirstRunStep.Pictures, vm.FirstRunStep);
        Assert.Equal(1, storageCommits);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task Welcome_DoesNotCreateStorageUntilStorageContinues()
    {
        var pictures = Path.Combine(_testRoot, "pictures");
        var pointer = Path.Combine(_testRoot, "pointer");
        var data = Path.Combine(_testRoot, "data");
        var cache = Path.Combine(_testRoot, "cache");
        var locations = new AppDataLocationService(new AppDataPlatformPaths(
            pictures, pointer, data, cache));
        using var catalog = new CatalogService(Path.Combine(_testRoot, "vm-catalog"));
        var vm = new MainWindowViewModel(catalog);
        var storageCommits = 0;
        vm.PrepareFirstRunStorage(locations, null);
        vm.CompleteDataLocationSetupAsync = async () =>
        {
            storageCommits++;
            await locations.CreateFreshAsync(
                catalogRoot: vm.SetupCatalogRoot,
                cacheRoot: vm.SetupCacheRoot);
            vm.MarkFirstRunStorageCommitted();
            vm.ResumeFirstRunAfterStorage(pictures);
        };
        vm.ShowFirstRunWelcome(pictures);

        Assert.False(Directory.Exists(pictures));
        Assert.False(Directory.Exists(cache));
        Assert.False(File.Exists(locations.PointerPath));
        await vm.ContinueFirstRunCommand.ExecuteAsync(null);
        Assert.False(File.Exists(locations.PointerPath));

        await vm.ContinueFirstRunCommand.ExecuteAsync(null);

        Assert.Equal(1, storageCommits);
        Assert.True(File.Exists(locations.PointerPath));
        Assert.True(Directory.Exists(pictures));
        Assert.True(Directory.Exists(cache));
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task PicturesDetection_DefersCompletionUntilImportApplies()
    {
        using var catalog = new CatalogService(Path.Combine(_testRoot, "catalog"));
        var vm = new MainWindowViewModel(catalog);
        var lightroomCatalog = Path.Combine(_testRoot, "photos.lrcat");
        var persisted = false;
        var applied = false;
        var focusRequested = false;
        vm.DetectLightroomAsync = (_, _) => Task.FromResult(
            new LightroomDetectionResult(true, lightroomCatalog));
        vm.RequestFirstRunCatalogImportAsync = path =>
        {
            Assert.Equal(lightroomCatalog, path);
            return Task.FromResult(applied);
        };
        vm.PersistFirstRunCompletionAsync = _ =>
        {
            persisted = true;
            return Task.CompletedTask;
        };
        vm.RequestFolderTreeFocus = () => focusRequested = true;
        vm.ShowFirstRunWelcome(_testRoot);
        vm.ResumeFirstRunAfterStorage(_testRoot);

        await vm.CompleteFirstRunFromLocationAsync(_testRoot);

        Assert.Equal(FirstRunStep.Lightroom, vm.FirstRunStep);
        Assert.False(persisted);
        Assert.Equal(StartupGateState.Welcome, vm.StartupGateState);
        await vm.ImportDetectedLightroomCommand.ExecuteAsync(null);
        Assert.Equal(FirstRunStep.Lightroom, vm.FirstRunStep);
        Assert.False(persisted);

        applied = true;
        await vm.ImportDetectedLightroomCommand.ExecuteAsync(null);

        Assert.Equal(FirstRunStep.AllSet, vm.FirstRunStep);
        Assert.False(persisted);
        await vm.StartFirstRunTourCommand.ExecuteAsync(null);

        Assert.True(persisted);
        Assert.Equal(StartupGateState.Ready, vm.StartupGateState);
        Assert.Equal(WorkflowTourStep.ChooseWhatMatters, vm.WorkflowTourStep);
        Assert.True(focusRequested);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task LightroomSkip_UsesTheSameWizardFinishPath()
    {
        using var catalog = new CatalogService(Path.Combine(_testRoot, "catalog"));
        var vm = new MainWindowViewModel(catalog);
        string? persistedPath = null;
        vm.DetectLightroomAsync = (_, _) => Task.FromResult(
            new LightroomDetectionResult(true, (string?)null));
        vm.PersistFirstRunCompletionAsync = path =>
        {
            persistedPath = path;
            return Task.CompletedTask;
        };
        vm.ShowFirstRunWelcome(_testRoot);
        vm.ResumeFirstRunAfterStorage(_testRoot);
        await vm.CompleteFirstRunFromLocationAsync(_testRoot);

        vm.SkipDetectedLightroomCommand.Execute(null);

        Assert.Equal(FirstRunStep.AllSet, vm.FirstRunStep);
        Assert.Null(persistedPath);
        await vm.SkipFirstRunTourCommand.ExecuteAsync(null);

        Assert.Equal(_testRoot, persistedPath);
        Assert.Equal(StartupGateState.Ready, vm.StartupGateState);
        Assert.Equal(WorkflowTourStep.None, vm.WorkflowTourStep);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task StorageRows_RequestTheirOwnPickerDirectly()
    {
        var service = new AppDataLocationService(new AppDataPlatformPaths(
            Path.Combine(_testRoot, "pictures"),
            Path.Combine(_testRoot, "pointer"),
            Path.Combine(_testRoot, "data"),
            Path.Combine(_testRoot, "cache")));
        using var catalog = new CatalogService(Path.Combine(_testRoot, "catalog"));
        var vm = new MainWindowViewModel(catalog);
        vm.PrepareFirstRunStorage(service, null);
        var requested = new List<bool>();
        vm.ChangeSetupLocationAsync = catalogLocation =>
        {
            requested.Add(catalogLocation);
            return Task.CompletedTask;
        };
        vm.ShowFirstRunWelcome(_testRoot);
        await vm.ContinueFirstRunCommand.ExecuteAsync(null);

        Assert.True(vm.CanChangeFirstRunStorage);
        await vm.ChangeSetupCatalogCommand.ExecuteAsync(null);
        await vm.ChangeSetupCacheCommand.ExecuteAsync(null);
        Assert.Equal([true, false], requested);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task LightroomCandidates_AreSelectableAndImportUsesSelection()
    {
        using var catalog = new CatalogService(Path.Combine(_testRoot, "catalog"));
        var vm = new MainWindowViewModel(catalog);
        var first = Path.Combine(_testRoot, "first.lrcat");
        var second = Path.Combine(_testRoot, "second.lrcat");
        var third = Path.Combine(_testRoot, "third.lrcat");
        string? imported = null;
        vm.DetectLightroomAsync = (_, _) => Task.FromResult(
            new LightroomDetectionResult(true, [first, second]));
        vm.RequestFirstRunCatalogImportAsync = path =>
        {
            imported = path;
            return Task.FromResult(false);
        };
        vm.RequestFirstRunCatalogPathAsync = () => Task.FromResult<string?>(third);
        vm.ShowFirstRunWelcome(_testRoot);
        vm.ResumeFirstRunAfterStorage(_testRoot);

        await vm.CompleteFirstRunFromLocationAsync(_testRoot);

        Assert.Equal([first, second], vm.DetectedLightroomCatalogPaths);
        Assert.Equal(first, vm.DetectedLightroomCatalogPath);
        await vm.ChooseAnotherLightroomCatalogCommand.ExecuteAsync(null);
        Assert.Equal(third, vm.DetectedLightroomCatalogPath);
        Assert.Contains(third, vm.DetectedLightroomCatalogPaths);
        vm.DetectedLightroomCatalogPath = second;
        await vm.ImportDetectedLightroomCommand.ExecuteAsync(null);
        Assert.Equal(second, imported);
        Assert.Equal(FirstRunStep.Lightroom, vm.FirstRunStep);
        await vm.DisposeAsync();
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
