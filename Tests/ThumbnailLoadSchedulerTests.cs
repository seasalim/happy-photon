using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HappyPhoton.Models;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ThumbnailLoadSchedulerTests
{
    [Fact]
    public async Task Scheduler_NeverExceedsWorkerCount()
    {
        using var cancellation = new CancellationTokenSource();
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var workersStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allLoaded = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var maximum = 0;
        var loaded = 0;
        using var scheduler = new ThumbnailLoadScheduler(
            6,
            async (_, _) =>
            {
                var current = Interlocked.Increment(ref active);
                UpdateMaximum(ref maximum, current);
                if (current == 6) workersStarted.TrySetResult();
                await release.Task;
                Interlocked.Decrement(ref active);
                if (Interlocked.Increment(ref loaded) == 40) allLoaded.TrySetResult();
            },
            cancellation.Token);
        var images = Enumerable.Range(0, 40)
            .Select(index => new ImageFile($"image-{index}.jpg"))
            .ToArray();

        scheduler.Enqueue(images.Select(image => (image, 0)));
        await workersStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.SetResult();
        await allLoaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await scheduler.Completion;

        Assert.Equal(6, maximum);
    }

    [Fact]
    public async Task Scheduler_PrioritizesVisibleWorkAheadOfQueuedPrefetch()
    {
        using var cancellation = new CancellationTokenSource();
        var blockerStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBlocker = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new ConcurrentQueue<string>();
        var blocker = new ImageFile("blocker.jpg");
        using var scheduler = new ThumbnailLoadScheduler(
            1,
            async (image, _) =>
            {
                if (ReferenceEquals(image, blocker))
                {
                    blockerStarted.SetResult();
                    await releaseBlocker.Task;
                    return;
                }
                order.Enqueue(image.FileName);
                if (order.Count == 2) completed.SetResult();
            },
            cancellation.Token);

        scheduler.Enqueue(new[] { (blocker, 0) });
        await blockerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        scheduler.Enqueue(new[]
        {
            (new ImageFile("prefetch.jpg"), 1),
            (new ImageFile("visible.jpg"), 0)
        });
        releaseBlocker.SetResult();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await scheduler.Completion;

        Assert.Equal(new[] { "visible.jpg", "prefetch.jpg" }, order.ToArray());
    }

    [Fact]
    public async Task Scheduler_SkipsImagesWhoseThumbnailLoadAlreadyFailed()
    {
        using var cancellation = new CancellationTokenSource();
        var loaded = new TaskCompletionSource<ImageFile>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new ImageFile("corrupt.jpg") { ThumbnailLoadFailed = true };
        var healthy = new ImageFile("healthy.jpg");
        using var scheduler = new ThumbnailLoadScheduler(
            1,
            (image, _) =>
            {
                loaded.SetResult(image);
                return Task.CompletedTask;
            },
            cancellation.Token);

        scheduler.Enqueue(new[] { (failed, 0), (healthy, 0) });
        var loadedImage = await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await scheduler.Completion;

        Assert.Same(healthy, loadedImage);
    }

    [Fact]
    public async Task Scheduler_SkipsImagesDeferredForHydration()
    {
        using var cancellation = new CancellationTokenSource();
        var loaded = new TaskCompletionSource<ImageFile>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var deferred = new ImageFile("cloud.jpg")
        {
            ThumbnailDeferredForHydration = true
        };
        var local = new ImageFile("local.jpg");
        using var scheduler = new ThumbnailLoadScheduler(
            1,
            (image, _) =>
            {
                loaded.SetResult(image);
                return Task.CompletedTask;
            },
            cancellation.Token);

        scheduler.Enqueue([(deferred, 0), (local, 0)]);
        var loadedImage = await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await scheduler.Completion;

        Assert.Same(local, loadedImage);
    }

    [Fact]
    public async Task LargerRequestSupersedesSmallerQueuedRequest()
    {
        using var cancellation = new CancellationTokenSource();
        var blockerStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var loaded = new TaskCompletionSource<ThumbnailSizeRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = new ImageFile("blocker.jpg");
        var target = new ImageFile("target.jpg");
        using var scheduler = new ThumbnailLoadScheduler(
            1,
            async (image, request, _) =>
            {
                if (ReferenceEquals(image, blocker))
                {
                    blockerStarted.SetResult();
                    await release.Task;
                }
                else
                {
                    loaded.SetResult(request);
                }
            },
            cancellation.Token);

        scheduler.Enqueue([new ThumbnailLoadRequest(
            blocker,
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Small),
            0)]);
        await blockerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        scheduler.Enqueue([new ThumbnailLoadRequest(
            target,
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Small),
            0)]);
        scheduler.Enqueue([new ThumbnailLoadRequest(
            target,
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Large),
            0)]);
        release.SetResult();

        Assert.Equal(
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Large),
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        cancellation.Cancel();
        await scheduler.Completion;
    }

    [Fact]
    public async Task InFlightSmallRetainsLargeFollowUp()
    {
        using var cancellation = new CancellationTokenSource();
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new ConcurrentQueue<ThumbnailSizeRequest>();
        var image = new ImageFile("target.jpg");
        using var scheduler = new ThumbnailLoadScheduler(
            1,
            async (_, request, _) =>
            {
                requests.Enqueue(request);
                if (requests.Count == 1)
                {
                    firstStarted.SetResult();
                    await release.Task;
                }
                else
                {
                    completed.SetResult();
                }
            },
            cancellation.Token);

        scheduler.Enqueue([new ThumbnailLoadRequest(
            image,
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Small),
            0)]);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        scheduler.Enqueue([new ThumbnailLoadRequest(
            image,
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Large),
            0)]);
        release.SetResult();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await scheduler.Completion;

        Assert.Equal(
            [
                ThumbnailSizeRequest.For(LibraryThumbnailSize.Small),
                ThumbnailSizeRequest.For(LibraryThumbnailSize.Large)
            ],
            requests.ToArray());
    }

    [WindowsFact]
    public async Task UndersizedResidentDoesNotSuppressUpgrade()
    {
        using var cancellation = new CancellationTokenSource();
        using var resident = CreateBitmap(150, 100);
        var image = new ImageFile("target.jpg") { Thumbnail = resident };
        var loaded = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var scheduler = new ThumbnailLoadScheduler(
            1,
            (_, _, _) =>
            {
                loaded.SetResult();
                return Task.CompletedTask;
            },
            cancellation.Token);

        scheduler.Enqueue([new ThumbnailLoadRequest(
            image,
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Large),
            0)]);
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await scheduler.Completion;
    }

    [WindowsFact]
    public async Task FailedUpgradeIsNotRetriedWhileResidentBitmapRemains()
    {
        using var cancellation = new CancellationTokenSource();
        using var resident = CreateBitmap(150, 100);
        var failed = new ImageFile("failed.jpg")
        {
            Thumbnail = resident,
            ThumbnailUpgradeFailedDimension = 512
        };
        var healthy = new ImageFile("healthy.jpg");
        var loaded = new TaskCompletionSource<ImageFile>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var scheduler = new ThumbnailLoadScheduler(
            1,
            (image, _, _) =>
            {
                loaded.SetResult(image);
                return Task.CompletedTask;
            },
            cancellation.Token);

        var request = ThumbnailSizeRequest.For(LibraryThumbnailSize.Large);
        scheduler.Enqueue([
            new ThumbnailLoadRequest(failed, request, 0),
            new ThumbnailLoadRequest(healthy, request, 0)]);
        var loadedImage = await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await scheduler.Completion;

        Assert.Same(healthy, loadedImage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvictedTerminalUpgradeCanReload(bool deferred)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The platform bitmap integration test requires Windows WIC.");
        }

        using var cancellation = new CancellationTokenSource();
        using var resident = CreateBitmap(150, 100);
        var image = new ImageFile("evicted.jpg")
        {
            Thumbnail = resident,
            ThumbnailUpgradeDeferredDimension = deferred ? 512 : 0,
            ThumbnailUpgradeFailedDimension = deferred ? 0 : 512
        };
        image.Thumbnail = null;
        var loaded = new TaskCompletionSource<ImageFile>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var scheduler = new ThumbnailLoadScheduler(
            1,
            (candidate, _, _) =>
            {
                loaded.SetResult(candidate);
                return Task.CompletedTask;
            },
            cancellation.Token);

        scheduler.Enqueue([new ThumbnailLoadRequest(
            image,
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Large),
            0)]);
        var loadedImage = await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await scheduler.Completion;

        Assert.Same(image, loadedImage);
    }

    [Fact]
    public async Task CompletedRequestPrunesDesiredTarget()
    {
        using var cancellation = new CancellationTokenSource();
        var loaded = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var scheduler = new ThumbnailLoadScheduler(
            1,
            (_, _, _) =>
            {
                loaded.SetResult();
                return Task.CompletedTask;
            },
            cancellation.Token);

        scheduler.Enqueue([new ThumbnailLoadRequest(
            new ImageFile("complete.jpg"),
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Large),
            0)]);
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(SpinWait.SpinUntil(
            () => scheduler.DesiredCount == 0,
            TimeSpan.FromSeconds(5)));
        cancellation.Cancel();
        await scheduler.Completion;
    }

    [WindowsFact]
    public void ResidencyPolicy_EvictsLeastRecentUnpinnedImages()
    {
        var images = Enumerable.Range(0, 5)
            .Select(index => new ImageFile($"image-{index}.jpg")
            {
                Thumbnail = CreateBitmap()
            })
            .ToArray();
        var access = images.Select((image, index) => (image, index: (long)index + 1))
            .ToDictionary(item => item.image, item => item.index);
        var pinned = new HashSet<ImageFile>(ReferenceEqualityComparer.Instance)
        {
            images[0]
        };

        var evictions = ThumbnailResidencyPolicy.SelectEvictions(
            images,
            pinned,
            access,
            targetBytes: images[0].ThumbnailBytes * 3);

        Assert.Equal(new[] { images[1], images[2] }, evictions);
        foreach (var image in images) image.Thumbnail?.Dispose();
    }

    private static WriteableBitmap CreateBitmap(
        int width = 10,
        int height = 10) => new(
        new PixelSize(width, height),
        new Vector(96, 96),
        PixelFormat.Bgra8888,
        AlphaFormat.Premul);

    private static void UpdateMaximum(ref int maximum, int value)
    {
        var current = Volatile.Read(ref maximum);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref maximum, value, current);
            if (observed == current) return;
            current = observed;
        }
    }

    [Fact]
    public void PrefetchIsBoundedByViewportCountAbsoluteCountAndBytes()
    {
        var images = Enumerable.Range(0, 500)
            .Select(index => new ImageFile($"image-{index}.jpg"))
            .ToList();
        var candidates = MainWindowViewModel.BuildNearestPrefetch(
            images,
            visibleStart: 200,
            visibleCount: 100);

        Assert.Equal(128, candidates.Count);
        var overBudget = MainWindowViewModel.AdmitPrefetch(
            candidates,
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Large),
            MainWindowViewModel.ThumbnailPixelBudget);
        Assert.Empty(overBudget);
        var admitted = MainWindowViewModel.AdmitPrefetch(
            candidates,
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Large),
            startingBytes: 0);
        Assert.True(admitted.Count <= 128);
        Assert.True(admitted.Count * MainWindowViewModel.EstimateRequestBytes(
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Large)) <
            MainWindowViewModel.ThumbnailPixelBudget);
    }
}
