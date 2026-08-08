using System.Collections.Concurrent;
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
    public void ResidencyPolicy_EvictsLeastRecentUnpinnedImages()
    {
        var images = Enumerable.Range(0, 5)
            .Select(index => new ImageFile($"image-{index}.jpg"))
            .ToArray();
        var access = images.Select((image, index) => (image, index: (long)index + 1))
            .ToDictionary(item => item.image, item => item.index);
        var pinned = new HashSet<ImageFile>(ReferenceEqualityComparer.Instance)
        {
            images[0]
        };

        var evictions = ThumbnailResidencyPolicy.SelectEvictions(
            images, pinned, access, targetCount: 3);

        Assert.Equal(new[] { images[1], images[2] }, evictions);
    }

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
}
