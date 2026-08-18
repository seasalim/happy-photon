using System.ComponentModel;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class BackgroundActivityThumbnailOwnershipTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-activity-folders-{Guid.NewGuid():N}")).FullName;

    [WindowsFact]
    public async Task FolderSizeDoesNotChangeUiWakeOrPumpWork()
    {
        var small = await CreateFolderSessionAsync("small", 5);
        var large = await CreateFolderSessionAsync("large", 500);
        try
        {
            await small.ViewModel.LoadFolderAsync(small.Folder);
            await large.ViewModel.LoadFolderAsync(large.Folder);
            await TestWaits.UntilAsync(() =>
                small.Gate.StartedCount == 5 &&
                large.Gate.StartedCount == 6);

            Assert.Equal(1, small.ViewModel.InitialThumbnailBatchCount);
            Assert.Equal(1, large.ViewModel.InitialThumbnailBatchCount);
            Assert.Equal(
                small.ViewModel.BackgroundActivityEpoch,
                large.ViewModel.BackgroundActivityEpoch);
            Assert.Equal(1, small.ViewModel.BackgroundActivityEpoch);
            Assert.Equal(
                TimeSpan.FromMilliseconds(250),
                MainWindowViewModel.BackgroundActivitySampleInterval);

            var smallBefore = small.ViewModel.BackgroundActivityPumpCount;
            var largeBefore = large.ViewModel.BackgroundActivityPumpCount;
            var now = DateTimeOffset.UtcNow;
            for (var sample = 1; sample <= 4; sample++)
            {
                var timestamp = now + TimeSpan.FromMilliseconds(250 * sample);
                small.ViewModel.PumpBackgroundActivity(timestamp);
                large.ViewModel.PumpBackgroundActivity(timestamp);
            }

            Assert.Equal(4,
                small.ViewModel.BackgroundActivityPumpCount - smallBefore);
            Assert.Equal(4,
                large.ViewModel.BackgroundActivityPumpCount - largeBefore);

            var smallNotifications = 0;
            var largeNotifications = 0;
            small.ViewModel.PropertyChanged += CountActivityProperty;
            large.ViewModel.PropertyChanged += CountActivityProperty;
            var smallEpoch = small.ViewModel.BackgroundActivityEpoch;
            var largeEpoch = large.ViewModel.BackgroundActivityEpoch;

            small.Gate.Release(1);
            large.Gate.Release(1);
            // Settles before asserting an absence, so a slow runner only makes
            // the claim stronger; there is no signal for "nothing was raised".
            await Task.Delay(100);

            Assert.Equal(smallEpoch, small.ViewModel.BackgroundActivityEpoch);
            Assert.Equal(largeEpoch, large.ViewModel.BackgroundActivityEpoch);
            Assert.Equal(0, smallNotifications);
            Assert.Equal(0, largeNotifications);

            void CountActivityProperty(object? sender, PropertyChangedEventArgs args)
            {
                if (args.PropertyName != nameof(MainWindowViewModel.BackgroundActivity))
                {
                    return;
                }
                if (ReferenceEquals(sender, small.ViewModel)) smallNotifications++;
                if (ReferenceEquals(sender, large.ViewModel)) largeNotifications++;
            }
        }
        finally
        {
            var smallDispose = small.ViewModel.DisposeAsync().AsTask();
            var largeDispose = large.ViewModel.DisposeAsync().AsTask();
            small.Gate.Release(20);
            large.Gate.Release(20);
            await Task.WhenAll(smallDispose, largeDispose);
            small.Catalog.Dispose();
            large.Catalog.Dispose();
        }
    }

    [WindowsFact]
    public async Task SchedulerAloneOwnsSharedThumbnailLoadCallback()
    {
        var folder = CreatePhotoFolder("scheduler", 1, 1200, 800);
        using var catalog = new CatalogService(Path.Combine(_root, "scheduler-catalog"));
        await catalog.InitializeAsync();
        var secondLoadGate = new SelectiveLoadGate(2);
        var vm = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask,
            postSelection: _ => { })
        {
            LibraryThumbnailSize = LibraryThumbnailSize.Large,
            ThumbnailLoadGateAsync = secondLoadGate.EnterAsync
        };

        try
        {
            await vm.LoadFolderAsync(folder);
            await TestWaits.UntilAsync(() =>
                secondLoadGate.StartedCount >= 2 &&
                vm.SchedulerThumbnailActivityCount == 1);

            Assert.Equal(0, vm.InitialThumbnailBatchCount);
            Assert.Equal(0, vm.DirectThumbnailActivityCount);
            Assert.Equal(1, vm.SchedulerThumbnailActivityCount);
            Assert.Equal(1, vm.CaptureBackgroundActivitySnapshot().ThumbnailCount);

            secondLoadGate.Release();
            await TestWaits.UntilAsync(() => vm.SchedulerThumbnailActivityCount == 0);
            Assert.Equal(0, vm.DirectThumbnailActivityCount);
        }
        finally
        {
            secondLoadGate.Release();
            await vm.DisposeAsync();
        }
    }

    private async Task<FolderSession> CreateFolderSessionAsync(
        string name,
        int imageCount)
    {
        var folder = CreatePhotoFolder(name, imageCount, 16, 16);
        var catalog = new CatalogService(Path.Combine(_root, $"{name}-catalog"));
        await catalog.InitializeAsync();
        var gate = new LoadGate();
        var vm = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask,
            postSelection: _ => { })
        {
            ThumbnailLoadGateAsync = gate.EnterAsync
        };
        return new FolderSession(folder, catalog, vm, gate);
    }

    private string CreatePhotoFolder(
        string name,
        int count,
        uint width,
        uint height)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        using var image = new MagickImage(MagickColors.Gray, width, height);
        for (var index = 0; index < count; index++)
        {
            image.Write(Path.Combine(folder, $"{index:D4}.jpg"), MagickFormat.Jpeg);
        }
        return folder;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class LoadGate
    {
        private readonly SemaphoreSlim _release = new(0);
        private int _started;

        public int StartedCount => Volatile.Read(ref _started);

        public async Task EnterAsync()
        {
            Interlocked.Increment(ref _started);
            await _release.WaitAsync();
        }

        public void Release(int count) => _release.Release(count);
    }

    private sealed class SelectiveLoadGate(int blockedCall)
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _started;

        public int StartedCount => Volatile.Read(ref _started);

        public Task EnterAsync()
        {
            var call = Interlocked.Increment(ref _started);
            return call == blockedCall ? _release.Task : Task.CompletedTask;
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed record FolderSession(
        string Folder,
        CatalogService Catalog,
        MainWindowViewModel ViewModel,
        LoadGate Gate);
}
