using System.Collections.Concurrent;
using ImageMagick;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class MetadataServiceTests
{
    [Fact]
    public void ExtractMetadata_ReadsDimensionsWithoutMutatingImageFile()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), $"HappyPhotonMetadata_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "image.jpg");
        try
        {
            using (var source = new MagickImage(MagickColors.Red, 320, 240))
            {
                source.Write(path, MagickFormat.Jpeg);
            }
            var image = new ImageFile(path);

            var metadata = MetadataService.ExtractMetadata(image, new MagickNetRawService());

            Assert.Equal(320, metadata.PixelWidth);
            Assert.Equal(240, metadata.PixelHeight);
            Assert.True(metadata.FileSize > 0);
            Assert.False(image.MetadataLoaded);
            Assert.Equal(0, image.PixelWidth);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_ConcurrentCallersShareOneExtraction()
    {
        var extractionStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseExtraction = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var extractionCount = 0;
        var service = new MetadataService(
            _ =>
            {
                Interlocked.Increment(ref extractionCount);
                extractionStarted.SetResult();
                releaseExtraction.Task.GetAwaiter().GetResult();
                return new ImageMetadata { PixelWidth = 6000, PixelHeight = 4000 };
            },
            action =>
            {
                action();
                return Task.CompletedTask;
            });
        var image = new ImageFile("test.jpg");

        var loads = Enumerable.Range(0, 40)
            .Select(_ => service.LoadAsync(image))
            .ToArray();
        await extractionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releaseExtraction.SetResult();
        await Task.WhenAll(loads);

        Assert.Equal(1, extractionCount);
        Assert.True(image.MetadataLoaded);
        Assert.Equal(6000, image.PixelWidth);
        Assert.Equal(4000, image.PixelHeight);
    }

    [Fact]
    public async Task LoadAsync_AppliesAllObservablePropertiesOnDispatcherThread()
    {
        using var dispatcher = new SingleThreadActionDispatcher();
        var service = new MetadataService(
            _ => new ImageMetadata
            {
                FileSize = 42,
                PixelWidth = 300,
                PixelHeight = 200,
                CameraMake = "Test",
                Iso = 100
            },
            dispatcher.InvokeAsync);
        var image = new ImageFile("test.jpg");
        var notificationThreads = new ConcurrentBag<int>();
        image.PropertyChanged += (_, _) =>
            notificationThreads.Add(Environment.CurrentManagedThreadId);

        await service.LoadAsync(image);

        Assert.NotEmpty(notificationThreads);
        Assert.All(notificationThreads, id => Assert.Equal(dispatcher.ThreadId, id));
        Assert.True(image.MetadataLoaded);
    }

    [Fact]
    public async Task LoadAsync_DoesNotCompleteBeforeDispatcherApplication()
    {
        var applyStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseApply = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new MetadataService(
            _ => new ImageMetadata { DateTaken = new DateTime(2026, 7, 19) },
            async action =>
            {
                applyStarted.SetResult();
                await releaseApply.Task;
                action();
            });
        var image = new ImageFile("test.jpg");

        var load = service.LoadAsync(image);
        await applyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(load.IsCompleted);
        Assert.False(image.MetadataLoaded);

        releaseApply.SetResult();
        await load;

        Assert.True(image.MetadataLoaded);
        Assert.Equal(new DateTime(2026, 7, 19), image.DateTaken);
    }

    [Fact]
    public async Task HydrationDeferral_RemainsUnloadedAndRetryable()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-metadata-hydration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "photo.dng");
        await File.WriteAllBytesAsync(path, [1]);
        try
        {
            var availability = new TestSourceAvailabilityService(
                SourceAvailability.RequiresHydration);
            var raw = new CountingRawService();
            var service = new MetadataService(
                raw,
                availability,
                action =>
                {
                    action();
                    return Task.CompletedTask;
                });
            var image = new ImageFile(path);

            var deferred = await service.LoadAsync(image);
            availability.Availability = SourceAvailability.AvailableLocally;
            var loaded = await service.LoadAsync(image);

            Assert.Equal(MetadataLoadStatus.DeferredForHydration, deferred);
            Assert.Equal(MetadataLoadStatus.Loaded, loaded);
            Assert.Equal(1, raw.ExtractCount);
            Assert.True(image.MetadataLoaded);
            Assert.Equal(4000, image.PixelWidth);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class SingleThreadActionDispatcher : IDisposable
    {
        private readonly BlockingCollection<(Action Action, TaskCompletionSource Completion)> _queue = new();
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _started = new();

        public SingleThreadActionDispatcher()
        {
            _thread = new Thread(Run) { IsBackground = true };
            _thread.Start();
            _started.Wait();
        }

        public int ThreadId { get; private set; }

        public Task InvokeAsync(Action action)
        {
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add((action, completion));
            return completion.Task;
        }

        private void Run()
        {
            ThreadId = Environment.CurrentManagedThreadId;
            _started.Set();
            foreach (var (action, completion) in _queue.GetConsumingEnumerable())
            {
                try
                {
                    action();
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            }
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            _thread.Join();
            _queue.Dispose();
            _started.Dispose();
        }
    }

    private sealed class CountingRawService : IRawProcessingService
    {
        internal int ExtractCount { get; private set; }
        public bool IsAvailable => true;
        public RawThumbnailData? ExtractThumbnail(string filePath) => null;

        public RawMetadata? ExtractMetadata(string filePath)
        {
            ExtractCount++;
            return new RawMetadata
            {
                PixelWidth = 4000,
                PixelHeight = 3000
            };
        }
    }
}
