using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CacheOffThreadTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriterTransfersOrSnapshotsOwnedPixels(bool transfer)
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        var file = CreateFile(root, "source.jpg", 1);
        var release = NewSignal();
        var recorder = new CullPerfRecorder();
        var writer = new SettingsHashedCacheWriter(catalog, catalog.GetPreviewPath,
            90, processingGate: release.Task) { CullPerf = recorder };
        var bitmap = new GuardedBitmap(file.FilePath);
        try
        {
            writer.Queue(file, bitmap, "hash", sourceWriteTime: File.GetLastWriteTimeUtc(file.FilePath),
                ownsBitmap: transfer);
            Assert.Equal(transfer ? 0 : 1, bitmap.Reads);
            if (!transfer) { bitmap.RefuseReads = true; bitmap.Dispose(); }
            release.TrySetResult();
            await writer.DisposeAsync();
            await writer.ProcessingTask.WaitAsync(TestWaits.Condition);
            Assert.Equal(transfer ? 0 : 1, bitmap.Reads);
            Assert.Equal(0, writer.DroppedWrites);
            Assert.Contains(recorder.Snapshot(), item => item.Kind == "CacheWriteComplete");
            using var written = new MagickImage(catalog.GetPreviewPath(1));
            Assert.Equal(64u, written.Width);
            Assert.Equal(48u, written.Height);
            Assert.Throws<ObjectDisposedException>(() => _ = bitmap.PixelSize);
        }
        finally { release.TrySetResult(); await writer.DisposeAsync(); bitmap.Dispose(); }
    }

    [AvaloniaFact]
    public async Task MissingProvenanceAndFullQueueAreCountedWithoutWaiting()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        var first = CreateFile(root, "first.jpg", 1);
        var second = CreateFile(root, "second.jpg", 2);
        var release = NewSignal();
        var recorder = new CullPerfRecorder();
        var writer = new SettingsHashedCacheWriter(catalog, catalog.GetPreviewPath,
            90, queueCapacity: 1, processingGate: release.Task) { CullPerf = recorder };
        using var bitmap = new GuardedBitmap(first.FilePath);
        try
        {
            writer.Queue(first, bitmap, "missing");
            Assert.Equal(0, bitmap.Reads);
            writer.Queue(first, bitmap, "first", sourceWriteTime: File.GetLastWriteTimeUtc(first.FilePath));
            writer.Queue(second, bitmap, "second", sourceWriteTime: File.GetLastWriteTimeUtc(second.FilePath));
            Assert.Equal(2, writer.DroppedWrites);
            Assert.Equal(1, writer.PendingWrites);
            File.SetLastWriteTimeUtc(second.FilePath, DateTime.UtcNow.AddMinutes(1));
            release.TrySetResult();
            await writer.DisposeAsync();
            await writer.ProcessingTask.WaitAsync(TestWaits.Condition);
            Assert.Equal(3, writer.DroppedWrites);
            Assert.Equal(3, recorder.Snapshot().Count(item => item.Kind == "CacheWriteDropped"));
            Assert.False(File.Exists(catalog.GetPreviewPath(1)));
            Assert.False(File.Exists(catalog.GetPreviewPath(2)));
        }
        finally { release.TrySetResult(); await writer.DisposeAsync(); }
    }

    [AvaloniaFact]
    public async Task TwoDecodeWorkersAdmitCancellableWaiters()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        var file = CreateFile(root, "source.jpg", 1);
        await using var service = new PreviewService(catalog, new NullBaseLoader(), new RenderPipeline());
        var release = NewSignal();
        var bothEntered = NewSignal();
        var entered = 0;
        service.CacheDecodeGateAsync = () =>
        {
            if (Interlocked.Increment(ref entered) == 2) bothEntered.TrySetResult();
            return release.Task;
        };
        var first = service.LoadCachedPreviewAsync(file, file.EditSettings);
        var second = service.LoadComparePreviewAsync(file, file.EditSettings, joinAdjacentWarm: true);
        try
        {
            await bothEntered.Task.WaitAsync(TestWaits.Condition);
            using var cancellation = new CancellationTokenSource();
            var superseded = service.LoadCachedPreviewAsync(file, file.EditSettings, cancellation.Token);
            Assert.False(superseded.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await superseded.WaitAsync(TestWaits.Condition));
            Assert.Equal(2, Volatile.Read(ref entered));
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
        }
        finally { release.TrySetResult(); }
        using var cached = await first.WaitAsync(TestWaits.Condition);
        using var compare = await second.WaitAsync(TestWaits.Condition);
        using var next = await service.LoadCachedPreviewAsync(file, file.EditSettings);
        Assert.Equal(3, entered);
    }

    [AvaloniaFact]
    public async Task SelectionPromotionAndCompletedWarmJoinKeepWorkOffDispatcher()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await catalog.InitializeAsync();
        var first = CreateFile(root, "first.dng", 0);
        var second = CreateFile(root, "second.dng", 0);
        await first.EnsureCatalogIdAsync(catalog);
        await second.EnsureCatalogIdAsync(catalog);
        first.EditSettings.Exposure = 0.5;
        var recorder = new CullPerfRecorder();
        var availability = new TestSourceAvailabilityService(SourceAvailability.AvailableLocally);
        await using var vm = new MainWindowViewModel(catalog, new RawLoader(),
            loadMetadataAsync: _ => Task.CompletedTask, availabilityService: availability);
        var service = vm.ImageService.Previews;
        service.AdjacentWarmEnabled = false;
        service.CullPerf = recorder;
        vm.Browse.SetImages([first, second]);
        vm.IsDevelopMode = true;
        vm.SelectedImage = first;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null &&
            service.GetRetainedThumbnailReference()?.TryGetTarget(out _) == true);
        var dispatcher = Environment.CurrentManagedThreadId;
        Assert.True(Dispatcher.UIThread.CheckAccess());
        var promoted = await vm.ImageService.LoadThumbnailAsync(first, new ThumbnailSizeRequest(32, 32), default);
        Assert.NotNull(promoted.Bitmap);
        Assert.Equal(32, promoted.Bitmap!.PixelSize.Width);
        promoted.Bitmap.Dispose();
        vm.SelectedImage = second;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && service.PendingCacheWrites == 0);
        // A completed retained entry with its writer stalled exercises the synchronous join-miss case.
        var warmFile = CreateFile(root, "warm.jpg", 99);
        var release = NewSignal();
        var cache = new PreviewCacheService(catalog, 8, release.Task, TimeSpan.FromSeconds(2));
        var warmService = new PreviewService(catalog, new RawLoader(), new RenderPipeline(),
            cache, sourceAvailability: availability) { CullPerf = recorder };
        try
        {
            Assert.True(warmService.TryStartAdjacentWarm(warmFile));
            await TestWaits.UntilAsync(() => warmService.AdjacentWarmEntryCount == 1 && warmService.ActiveAdjacentWarm == null);
            using var joined = await warmService.LoadComparePreviewAsync(warmFile, warmFile.EditSettings, joinAdjacentWarm: true);
            Assert.NotNull(joined);
            using var cached = await warmService.LoadCachedPreviewAsync(warmFile, warmFile.EditSettings);
            Assert.NotNull(cached);
            release.TrySetResult();
            await TestWaits.UntilAsync(() => cache.PendingWrites == 0 && warmService.AdjacentWarmEntryCount == 0);
            using var disk = await warmService.LoadComparePreviewAsync(warmFile, warmFile.EditSettings, joinAdjacentWarm: true);
            Assert.NotNull(disk);
        }
        finally { release.TrySetResult(); await warmService.DisposeAsync(); }
        var events = recorder.Snapshot();
        Assert.Contains(events, item => item.Kind == "CacheEnqueueStart" && item.WorkerId == dispatcher);
        Assert.Contains(events, item => item.Kind == "WarmJoinMiss");
        foreach (var kind in new[] { "CacheConvert", "CacheMetadataRead", "CacheSourceMetadata",
            "CacheDecodeStart", "CacheDecodeEnd", "CacheThumbnailResize", "CacheWarmDecode", "CacheJoinDecode",
            "BaseDisposeStart", "BaseDisposeEnd" })
        {
            var exercised = events.Where(item => item.Kind == kind).ToArray();
            Assert.NotEmpty(exercised);
            Assert.All(exercised, item => Assert.NotEqual(dispatcher, item.WorkerId));
        }
    }

    [AvaloniaFact]
    public async Task PromotionOwnsItsSnapshotBeforeTheRetainedThumbnailRetires()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        var file = CreateFile(root, "source.dng", 1);
        file.EditSettings.Exposure = 0.5;
        await using var service = new PreviewService(catalog, new RawLoader(), new RenderPipeline());
        var (preview, _) = await service.LoadPreviewWithHistogramAsync(file, file.EditSettings);
        using var bitmap = preview;
        await TestWaits.UntilAsync(() => service.GetRetainedThumbnailReference()?.TryGetTarget(out _) == true);
        Assert.True(service.GetRetainedThumbnailReference()!.TryGetTarget(out var retained));
        var entered = NewSignal();
        var release = NewSignal();
        service.CacheDecodeGateAsync = () => { entered.TrySetResult(); return release.Task; };
        var promotion = service.TryPromoteRenderedThumbnailAsync(file, file.EditSettings,
            new ThumbnailSizeRequest(32, 32));
        try
        {
            await entered.Task.WaitAsync(TestWaits.Condition);
            service.FlushRenderedPreviewCache();
            await TestWaits.UntilAsync(() => service.RenderedThumbnailTaskCount == 0);
            Assert.Throws<ObjectDisposedException>(() => _ = retained!.PixelSize);
        }
        finally { release.TrySetResult(); }
        using var promoted = await promotion.WaitAsync(TestWaits.Condition);
        Assert.NotNull(promoted);
        Assert.Equal(32, promoted.PixelSize.Width);
    }

    [AvaloniaFact]
    public async Task DepartingPreviewAndThumbnailKeepTheBaseDecodeTimestamp()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        var file = CreateFile(root, "source.dng", 1);
        file.EditSettings.Exposure = 0.5;
        var recorder = new CullPerfRecorder();
        var service = new PreviewService(catalog, new RawLoader(), new RenderPipeline()) { CullPerf = recorder };
        try
        {
            var (preview, _) = await service.LoadPreviewWithHistogramAsync(file, file.EditSettings);
            using var bitmap = preview;
            Assert.NotNull(bitmap);
            File.SetLastWriteTimeUtc(file.FilePath, DateTime.UtcNow.AddMinutes(1));
            service.FlushRenderedPreviewCache();
            await service.DisposeAsync();
            Assert.False(File.Exists(catalog.GetPreviewPath(1)));
            Assert.False(File.Exists(catalog.GetRenderedThumbnailPath(1)));
            Assert.Equal(2, recorder.Snapshot().Count(item => item.Kind == "CacheWriteDropped"));
        }
        finally { await service.DisposeAsync(); }
    }

    [AvaloniaFact]
    public async Task RetirementBoundsReplacementDecodesAndIsDrainedAtShutdown()
    {
        using var root = new TemporaryDirectory();
        var file = CreateFile(root, "retirement.dng", 1);
        var recorder = new CullPerfRecorder();
        await using var coordinator = new PreviewBaseCoordinator(new RawLoader()) { CullPerf = recorder };
        var first = await coordinator.GetPreviewAsync(file, BaseDecodeSettings.Default, default);
        var pixels = first!.Base;
        first.Dispose();
        var entered = NewSignal();
        var release = NewSignal();
        coordinator.RetirementGateAsync = () => { entered.TrySetResult(); return release.Task; };
        var dispatcher = Environment.CurrentManagedThreadId;
        coordinator.Clear();
        try
        {
            await entered.Task.WaitAsync(TestWaits.Condition);
            Assert.Equal(0, coordinator.RetainedPairCount);
            Assert.NotNull(pixels.Pixels);
            var next = coordinator.GetPreviewAsync(file, BaseDecodeSettings.Default, default);
            coordinator.Clear(); // No held pair: must preserve the outstanding retirement task.
            Assert.False(next.IsCompleted);
            Assert.Single(recorder.Snapshot(), item => item.Kind == "CacheSourceMetadata");
            var shutdown = coordinator.DisposeAsync().AsTask();
            Assert.False(shutdown.IsCompleted);
            release.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next.WaitAsync(TestWaits.Condition));
            await shutdown.WaitAsync(TestWaits.Condition);
            Assert.Throws<ObjectDisposedException>(() => _ = pixels.Pixels);
            var disposed = Assert.Single(recorder.Snapshot(), item => item.Kind == "BaseDisposeEnd");
            Assert.NotEqual(dispatcher, disposed.WorkerId);
        }
        finally { release.TrySetResult(); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ByteSnapshotPreservesFormatAndAlphaAfterCallerDisposal(bool rgba, bool transparent)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(2, 1), new Vector(96, 96),
            rgba ? Avalonia.Platform.PixelFormat.Rgba8888 : Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Unpremul);
        using (var buffer = bitmap.Lock())
            System.Runtime.InteropServices.Marshal.Copy(
                new byte[] { 200, 100, 50, transparent ? (byte)128 : (byte)255, 10, 20, 30, 255 },
                0, buffer.Address, 8);
        using var expected = BitmapConversionService.ConvertToMagickImage(bitmap);
        var snapshot = BitmapConversionService.SnapshotBitmap(bitmap);
        bitmap.Dispose();
        using var restored = snapshot.ToMagickImage();
        Assert.Equal(BitmapConversionService.CopyBgraPixels(expected),
            BitmapConversionService.CopyBgraPixels(restored));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OlderPromotionCannotReplaceANewerEditedThumbnail(bool olderFromPump)
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        var file = CreateFile(root, "ordering.dng", 1);
        file.EditSettings.Exposure = 0.5;
        await using var vm = new MainWindowViewModel(catalog, new RawLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        vm.Browse.SetImages([file]);
        var service = vm.ImageService.Previews;
        service.AdjacentWarmEnabled = false;
        async Task RenderAsync()
        {
            var ready = NewSignal();
            void OnReady() => ready.TrySetResult();
            service.RenderedThumbnailCreated += OnReady;
            try
            {
                var (preview, _) = await service.LoadPreviewWithHistogramAsync(file, file.EditSettings);
                using var owned = preview;
                await ready.Task.WaitAsync(TestWaits.Condition);
            }
            finally { service.RenderedThumbnailCreated -= OnReady; }
        }
        Task LoadAsync(bool pump)
        {
            var types = pump
                ? new[] { typeof(ImageFile), typeof(int), typeof(CancellationToken) }
                : new[] { typeof(ImageFile) };
            var method = typeof(MainWindowViewModel).GetMethod(
                pump ? "LoadThumbnailAsync" : "RefreshThumbnailAsync",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                types)!;
            return (Task)method.Invoke(vm, pump ? [file, 0, CancellationToken.None] : [file])!;
        }
        await RenderAsync();
        var entered = NewSignal();
        var release = NewSignal();
        var requests = 0;
        service.CacheDecodeGateAsync = () =>
        {
            if (Interlocked.Increment(ref requests) != 1) return Task.CompletedTask;
            entered.TrySetResult();
            return release.Task;
        };
        var older = LoadAsync(olderFromPump);
        try
        {
            await entered.Task.WaitAsync(TestWaits.Condition);
            file.EditSettings = new EditSettings { Exposure = 2 };
            await RenderAsync();
            await LoadAsync(false).WaitAsync(TestWaits.Condition);
            var newest = file.Thumbnail;
            Assert.NotNull(newest);
            var pixels = BitmapConversionService.CopyBgraPixels(newest);
            var generation = file.ThumbnailGeneration;
            release.TrySetResult();
            await older.WaitAsync(TestWaits.Condition);
            Assert.Same(newest, file.Thumbnail);
            Assert.Equal(generation, file.ThumbnailGeneration);
            Assert.Equal(pixels, BitmapConversionService.CopyBgraPixels(file.Thumbnail!));
        }
        finally { release.TrySetResult(); await older.WaitAsync(TestWaits.Condition); }
    }
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static ImageFile CreateFile(TemporaryDirectory root, string name, long id)
    {
        var path = Path.Combine(root.Path, name);
        using var source = new MagickImage(MagickColors.Orange, 64, 48);
        source.Write(path, MagickFormat.Jpeg);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-2));
        return new ImageFile(path) { CatalogId = id };
    }

    private sealed class GuardedBitmap(string path) : Bitmap(path)
    {
        public int Reads { get; private set; }
        public bool RefuseReads { get; set; }
        public override void CopyPixels(PixelRect rect, IntPtr buffer, int bufferSize, int stride)
        {
            if (RefuseReads) throw new InvalidOperationException("Retired caller bitmap was read.");
            Reads++;
            base.CopyPixels(rect, buffer, bufferSize, stride);
        }
    }

    private sealed class RawLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;
        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(ImageFile file,
            BaseDecodeSettings decode, CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.FromImage(new BaseImage(new MagickImage(MagickColors.Orange, 300, 200)
            { Depth = 16, ColorSpace = ColorSpace.RGB },
                new BaseImageInfo(BaseSourceKind.RawLibRaw, true, decode, null, null,
                    5500, 0, false, null, 1, 300, 200)), BaseImageLoadFailure.DecodeFailed);
        public BaseImage? LoadFullBase(ImageFile file, BaseDecodeSettings decode,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
