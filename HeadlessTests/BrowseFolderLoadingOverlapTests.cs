using Avalonia.Headless.XUnit;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BrowseFolderLoadingOverlapTests
{
    [AvaloniaFact]
    public async Task OlderLoadResumingAfterXmpDrain_DoesNotCancelOrPublishOverNewest()
    {
        await using var scene = await BrowseFolderLoadingTestScene.CreateAsync();
        await scene.Vm.LoadFolderAsync(scene.Folder("previous", photo: true));
        using var xmpCts = new CancellationTokenSource();
        var xmpDrain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BrowseFolderLoadingTestScene.SetField(scene.Vm, "_xmpReconcileCts", xmpCts);
        BrowseFolderLoadingTestScene.SetField(scene.Vm, "_xmpReconcileTask", xmpDrain.Task);
        var oldPath = scene.Folder("older", photo: true);
        var newPath = scene.Folder("newest", photo: true);
        using var scan = new BrowseScanBarrier();
        var scannedPaths = new List<string>();
        scene.Vm.ScanBrowseFolder = path =>
        {
            lock (scannedPaths)
            {
                scannedPaths.Add(path);
            }

            return scan.Scan(path);
        };
        var older = scene.Vm.LoadFolderAsync(oldPath);
        Task<int>? newest = null;

        try
        {
            Assert.True(xmpCts.IsCancellationRequested);
            Assert.False(older.IsCompleted);
            scene.AssertBlank();
            newest = scene.Vm.LoadFolderAsync(newPath);
            await scan.Entered.Task.WaitAsync(TestWaits.Condition);
            var owner = BrowseFolderLoadingTestScene.Field<CancellationTokenSource>(
                scene.Vm, "_thumbnailLoadingCts");
            xmpDrain.SetResult();
            Assert.Equal(0, await older.WaitAsync(TestWaits.Condition));

            scene.AssertBlank();
            Assert.Same(owner, BrowseFolderLoadingTestScene.Field<CancellationTokenSource>(
                scene.Vm, "_thumbnailLoadingCts"));
            Assert.False(owner.IsCancellationRequested);
            Assert.Equal(newPath, scene.Vm.CurrentFolderPath);
            scan.Release();
            Assert.NotEqual(0, await newest.WaitAsync(TestWaits.Condition));
            Assert.Equal(newPath, Path.GetDirectoryName(Assert.Single(scene.Vm.Browse.AllImages).FilePath));
            Assert.Equal([newPath], scannedPaths);
            Assert.False(scene.Vm.IsBrowseFolderLoading);
            Assert.True(scene.Grid.IsVisible);
        }
        finally
        {
            xmpDrain.TrySetResult();
            scan.Release();
            await older.WaitAsync(TestWaits.Condition);

            if (newest != null)
            {
                await newest.WaitAsync(TestWaits.Condition);
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupersededScanCompletionOrFailure_DoesNotClearNewestLoading(bool fail)
    {
        await using var scene = await BrowseFolderLoadingTestScene.CreateAsync();
        var oldPath = scene.Folder("older", photo: true);
        var newPath = scene.Folder("newest", photo: true);
        using var oldScan = new BrowseScanBarrier();
        using var newScan = new BrowseScanBarrier();
        scene.Vm.ScanBrowseFolder = path =>
        {
            if (path == newPath) return newScan.Scan(path);

            var result = oldScan.Scan(path);
            if (fail) throw new IOException("Superseded scan failure");

            return result;
        };
        var older = scene.Vm.LoadFolderAsync(oldPath);
        Task<int>? newest = null;

        try
        {
            await oldScan.Entered.Task.WaitAsync(TestWaits.Condition);
            newest = scene.Vm.LoadFolderAsync(newPath);
            await newScan.Entered.Task.WaitAsync(TestWaits.Condition);
            oldScan.Release();
            Assert.Equal(0, await older.WaitAsync(TestWaits.Condition));

            scene.AssertBlank();
            Assert.Empty(scene.Vm.Browse.AllImages);
            Assert.Equal(newPath, scene.Vm.CurrentFolderPath);
            newScan.Release();
            Assert.NotEqual(0, await newest.WaitAsync(TestWaits.Condition));
            Assert.False(scene.Vm.IsBrowseFolderLoading);
            Assert.Equal(newPath, Path.GetDirectoryName(Assert.Single(scene.Vm.Browse.AllImages).FilePath));
            Assert.True(scene.Grid.IsVisible);
        }
        finally
        {
            oldScan.Release();
            newScan.Release();
            await older.WaitAsync(TestWaits.Condition);

            if (newest != null)
            {
                await newest.WaitAsync(TestWaits.Condition);
            }
        }
    }

    [AvaloniaFact]
    public async Task TeardownDuringScan_ClearsLoadingWithoutASuccessor()
    {
        await using var scene = await BrowseFolderLoadingTestScene.CreateAsync();
        using var scan = new BrowseScanBarrier();
        scene.Vm.ScanBrowseFolder = scan.Scan;
        var load = scene.Vm.LoadFolderAsync(scene.Folder("photos", photo: true));

        try
        {
            await scan.Entered.Task.WaitAsync(TestWaits.Condition);
            scene.AssertBlank();
            await scene.DisposeViewModelAsync();
            Assert.False(scene.Vm.IsBrowseFolderLoading);
            scan.Release();
            Assert.Equal(0, await load.WaitAsync(TestWaits.Condition));
            Assert.Empty(scene.Vm.Browse.AllImages);
        }
        finally
        {
            scan.Release();
            await load.WaitAsync(TestWaits.Condition);
        }
    }
}
