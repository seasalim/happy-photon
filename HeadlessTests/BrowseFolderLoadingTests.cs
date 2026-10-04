using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BrowseFolderLoadingTests
{
    [AvaloniaFact]
    public async Task FullStartup_ReadyDoesNotShowEmptyStateBeforeFirstPopulation()
    {
        using var directory = new TemporaryDirectory();
        var service = RestoreTestSupport.Service(directory.Path);
        var locations = await service.CreateFreshAsync(useStandardCatalog: true);
        var photos = Directory.CreateDirectory(Path.Combine(directory.Path, "pictures")).FullName;
        TestImages.WriteJpeg(Path.Combine(photos, "photo.jpg"), width: 24, height: 16);

        using (var seed = new CatalogService())
        {
            await seed.InitializeAsync(locations);
            await seed.SetAppSettingAsync("FirstRunExperienceVersion", "1");
            await seed.SetAppSettingAsync("RootFolderPath", photos);
            await seed.SetAppSettingAsync("SelectedFolderPath", photos);
        }

        using var catalog = new CatalogService();
        await using var vm = new MainWindowViewModel(catalog, baseLoader: null, postSelection: _ => { });
        using var scan = new BrowseScanBarrier();
        vm.ScanBrowseFolder = scan.Scan;
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var view = window.FindControl<BrowseGridView>("BrowseGridView")!;
        var empty = view.FindControl<Control>("EmptyState")!;
        var samples = new List<bool>();
        vm.PropertyChanged += (_, _) =>
        {
            if (vm.StartupGateState == StartupGateState.Ready)
            {
                samples.Add(empty.IsVisible);
            }
        };
        empty.PropertyChanged += (_, change) =>
        {
            if (change.Property == Visual.IsVisibleProperty && vm.StartupGateState == StartupGateState.Ready)
            {
                samples.Add(empty.IsVisible);
            }
        };

        try
        {
            await window.InitializeApplicationAsync(vm, catalog, service,
                new CatalogLocationMigrator(service), photos).WaitAsync(TestWaits.Condition);
            await scan.Entered.Task.WaitAsync(TestWaits.Condition);
            Assert.Equal(StartupGateState.Ready, vm.StartupGateState);
            Assert.True(vm.IsBrowseFolderLoading);
            Assert.False(empty.IsVisible);
            Assert.False(view.FindControl<Control>("ThumbnailGrid")!.IsVisible);
            scan.Release();
            await TestWaits.UntilAsync(() => !vm.IsBrowseFolderLoading);

            Assert.NotEmpty(samples);
            Assert.DoesNotContain(true, samples);
            Assert.Single(vm.Browse.AllImages);
            Assert.True(view.FindControl<Control>("ThumbnailGrid")!.IsVisible);
        }
        finally
        {
            scan.Release();
            await TestWaits.UntilAsync(() => !vm.IsBrowseFolderLoading);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PhotoSwitch_BlanksPreviousGridOrFilteredEmptyWithoutEmptyFlash(bool filtered)
    {
        await using var scene = await BrowseFolderLoadingTestScene.CreateAsync();
        await scene.Vm.LoadFolderAsync(scene.Folder("previous", photo: true));

        if (filtered)
        {
            scene.Vm.Browse.MinimumRating = 5;
            Assert.True(scene.FilteredEmpty.IsVisible);
        }
        else
        {
            Assert.True(scene.Grid.IsVisible);
        }

        using var scan = new BrowseScanBarrier();
        scene.Vm.ScanBrowseFolder = scan.Scan;
        var emptyFlashed = false;
        scene.Empty.PropertyChanged += (_, change) =>
        {
            if (change.Property == Visual.IsVisibleProperty && scene.Empty.IsVisible)
            {
                emptyFlashed = true;
            }
        };
        var load = scene.Vm.LoadFolderAsync(scene.Folder("next", photo: true));

        try
        {
            scene.AssertBlank();
            await scan.Entered.Task.WaitAsync(TestWaits.Condition);
            scene.AssertBlank();
            scan.Release();
            Assert.NotEqual(0, await load.WaitAsync(TestWaits.Condition));
            Assert.False(scene.Vm.IsBrowseFolderLoading);
            Assert.False(emptyFlashed);
            Assert.Equal(filtered, scene.FilteredEmpty.IsVisible);
            Assert.Equal(!filtered, scene.Grid.IsVisible);
            Assert.Single(scene.Vm.Browse.AllImages);
        }
        finally
        {
            scan.Release();
            await load.WaitAsync(TestWaits.Condition);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptySwitch_ShowsFinalHeadingOnlyAfterLoad(bool subfolders)
    {
        await using var scene = await BrowseFolderLoadingTestScene.CreateAsync();
        await scene.Vm.LoadFolderAsync(scene.Folder("previous", photo: true));
        using var scan = new BrowseScanBarrier();
        scene.Vm.ScanBrowseFolder = scan.Scan;
        scene.Vm.SelectedFolder = new FolderNode(scene.Folder("empty", subfolder: subfolders));

        try
        {
            scene.AssertBlank();
            await scan.Entered.Task.WaitAsync(TestWaits.Condition);
            scan.Release();
            await TestWaits.UntilAsync(() => !scene.Vm.IsBrowseFolderLoading);

            Assert.True(scene.Empty.IsVisible);
            Assert.False(scene.Grid.IsVisible);
            Assert.False(scene.FilteredEmpty.IsVisible);
            Assert.Empty(scene.Vm.Browse.AllImages);
            Assert.Equal(subfolders ? "No photographs directly inside empty" :
                "No supported photographs in this folder",
                scene.View.FindControl<TextBlock>("EmptyHeading")!.Text);
        }
        finally
        {
            scan.Release();
            await TestWaits.UntilAsync(() => !scene.Vm.IsBrowseFolderLoading);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostPumpCancelOrFailure_PreservesLoadedFolder(bool cancel)
    {
        await using var scene = await BrowseFolderLoadingTestScene.CreateAsync();
        var releaseThumbnails = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scene.Vm.ThumbnailLoadGateAsync = () => releaseThumbnails.Task;
        ImageFile? loadedImage = null;
        CancellationTokenSource? pumpOwner = null;
        var failureInjected = false;
        scene.Vm.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(MainWindowViewModel.IsBrowseFolderLoading) &&
                !scene.Vm.IsBrowseFolderLoading && loadedImage == null)
            {
                loadedImage = Assert.Single(scene.Vm.Browse.AllImages);
                loadedImage.PendingAssessmentAxes = AssessmentAxes.Rating;
            }

            // ReportPendingXmpAssessments publishes this status after the pump starts.
            if (change.PropertyName != nameof(MainWindowViewModel.TransientStatus) ||
                scene.Vm.TransientStatus != "XMP writes remain pending for 1 photo" ||
                failureInjected)
            {
                return;
            }

            failureInjected = true;
            pumpOwner = BrowseFolderLoadingTestScene.Field<CancellationTokenSource>(
                scene.Vm, "_thumbnailLoadingCts");

            if (cancel)
            {
                pumpOwner.Cancel();
                throw new OperationCanceledException(pumpOwner.Token);
            }

            throw new IOException("Injected post-pump failure");
        };

        try
        {
            var result = await scene.Vm.LoadFolderAsync(
                scene.Folder("photos", photo: true, subfolder: true)).WaitAsync(TestWaits.Condition);
            Assert.Equal(0, result);
            Assert.True(failureInjected);
            Assert.Same(loadedImage, Assert.Single(scene.Vm.Browse.AllImages));
            Assert.Same(pumpOwner, BrowseFolderLoadingTestScene.Field<CancellationTokenSource>(
                scene.Vm, "_thumbnailLoadingCts"));
            Assert.True(scene.Vm.CurrentFolderHasSubfolders);
            Assert.False(scene.Vm.IsBrowseFolderLoading);
            Assert.False(scene.Empty.IsVisible);
            Assert.False(scene.FilteredEmpty.IsVisible);
            Assert.True(scene.Grid.IsVisible);
            Assert.Equal("XMP writes remain pending for 1 photo", scene.Vm.TransientStatus);
            releaseThumbnails.SetResult();

            if (!cancel)
            {
                await TestWaits.UntilAsync(() => loadedImage!.Thumbnail != null);
            }
        }
        finally
        {
            releaseThumbnails.TrySetResult();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentCancelOrFailure_ClearsStaleImagesAndShowsEmpty(bool cancel)
    {
        await using var scene = await BrowseFolderLoadingTestScene.CreateAsync();
        await scene.Vm.LoadFolderAsync(scene.Folder("previous", photo: true));
        using var scan = new BrowseScanBarrier();
        scene.Vm.ScanBrowseFolder = path =>
        {
            var result = scan.Scan(path);
            if (!cancel) throw new IOException("Injected scan failure");

            return result;
        };
        var load = scene.Vm.LoadFolderAsync(scene.Folder("next", photo: true));

        try
        {
            await scan.Entered.Task.WaitAsync(TestWaits.Condition);
            scene.AssertBlank();

            if (cancel)
            {
                BrowseFolderLoadingTestScene.Field<CancellationTokenSource>(
                    scene.Vm, "_thumbnailLoadingCts").Cancel();
            }

            scan.Release();
            Assert.Equal(0, await load.WaitAsync(TestWaits.Condition));
            Assert.False(scene.Vm.IsBrowseFolderLoading);
            Assert.Empty(scene.Vm.Browse.AllImages);
            Assert.True(scene.Empty.IsVisible);
            Assert.False(scene.Grid.IsVisible);
            Assert.False(scene.FilteredEmpty.IsVisible);
        }
        finally
        {
            scan.Release();
            await load.WaitAsync(TestWaits.Condition);
        }
    }
}
