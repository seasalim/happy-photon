using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class AdjacentPreviewWarmTests
{
    [WindowsFact]
    public async Task NextWarmWaitsForTheStalledHandoffBeforeDecodingSoCancelWastesNothing()
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync("handoff-stall");
        var first = await CreateCatalogImageAsync(catalog, "first.jpg");
        var second = await CreateCatalogImageAsync(catalog, "second.jpg");
        var writerRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new PreviewCacheService(
            catalog,
            8,
            writerRelease.Task,
            TimeSpan.FromSeconds(2));
        var loader = new RecordingLoader();
        await using var service = CreateService(
            catalog,
            loader,
            new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            cache);

        Assert.True(service.TryStartAdjacentWarm(first));
        await TestWaits.UntilAsync(() => service.AdjacentWarmEntryCount == 1);
        await TestWaits.UntilAsync(() => service.PreviewActivityCount == 0);

        // The writer is stalled, so the first entry cannot hand off yet. The
        // second warm must wait in front of its decode, not behind it.
        Assert.True(service.TryStartAdjacentWarm(second));
        await TestWaits.UntilAsync(() => service.PreviewActivityCount == 1);
        Assert.Equal(["first.jpg"], loader.Paths);

        service.InvalidateAdjacentWarm();
        await TestWaits.UntilAsync(() => service.PreviewActivityCount == 0);
        Assert.Equal(["first.jpg"], loader.Paths);
        Assert.Equal(1, service.AdjacentWarmEntryCount);

        writerRelease.TrySetResult();
        await TestWaits.UntilAsync(() => service.AdjacentWarmEntryCount == 0);
        Assert.True(service.TryStartAdjacentWarm(second));
        await TestWaits.UntilAsync(() => service.PreviewActivityCount == 0);
        Assert.Equal(["first.jpg", "second.jpg"], loader.Paths);
        await TestWaits.UntilAsync(() => cache.PendingWrites == 0);
    }

    [WindowsFact]
    public async Task SelectingTheTargetKeepsADecodingWorkerButNotOneParkedBehindAWrite()
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync("handoff-keep");
        var first = await CreateCatalogImageAsync(catalog, "first.jpg");
        var second = await CreateCatalogImageAsync(catalog, "second.jpg");
        var third = await CreateCatalogImageAsync(catalog, "third.jpg");
        var writerRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new PreviewCacheService(
            catalog,
            8,
            writerRelease.Task,
            TimeSpan.FromSeconds(2));
        var loader = new BlockingLoader { Block = false };
        await using var service = CreateService(
            catalog,
            loader,
            new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            cache);

        Assert.True(service.TryStartAdjacentWarm(first));
        await TestWaits.UntilAsync(() => service.AdjacentWarmEntryCount == 1);
        await TestWaits.UntilAsync(() => service.PreviewActivityCount == 0);

        // Parked behind the stalled write: selecting it must not wait.
        Assert.True(service.TryStartAdjacentWarm(second));
        await TestWaits.UntilAsync(() => service.PreviewActivityCount == 1);
        service.InvalidateAdjacentWarm(joinFor: second);
        await TestWaits.UntilAsync(() => service.PreviewActivityCount == 0);
        Assert.Equal(1, loader.DecodeCount);

        // Decoding: selecting it keeps the worker. The cached read never
        // waits, so a stale preview could paint; the fresh path joins it.
        writerRelease.TrySetResult();
        await TestWaits.UntilAsync(() => service.AdjacentWarmEntryCount == 0);
        loader.Block = true;
        loader.Started.Reset();
        Assert.True(service.TryStartAdjacentWarm(third));
        Assert.True(loader.Started.Wait(TestWaits.Condition));
        service.InvalidateAdjacentWarm(joinFor: third);
        Assert.Equal(1, service.PreviewActivityCount);
        using var cached = await service
            .LoadCachedPreviewAsync(third, third.EditSettings)
            .WaitAsync(TestWaits.Condition);
        Assert.Null(cached);
        var fresh = service.LoadComparePreviewAsync(
            third, third.EditSettings, joinAdjacentWarm: true);
        await Task.Delay(50);
        Assert.False(fresh.IsCompleted);
        loader.Block = false;
        using var joined = await fresh;
        Assert.NotNull(joined);
        Assert.Equal(2, loader.DecodeCount);
        await TestWaits.UntilAsync(() => cache.PendingWrites == 0);
    }

    [WindowsFact]
    public async Task ACancelledWorkerStillDrainingItsDecodeIsNotJoinedByTheCachedRead()
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync("handoff-draining");
        var target = await CreateCatalogImageAsync(catalog, "target.jpg");
        var recorder = new CullPerfRecorder();
        var loader = new DrainingLoader(recorder);
        await using var service = CreateService(
            catalog,
            loader,
            new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));

        service.CullPerf = recorder;
        try
        {
            Assert.True(service.TryStartAdjacentWarm(target));
            Assert.True(loader.Started.Wait(TestWaits.Condition));
            Assert.Equal(0, CullPerfCounters.Derive(recorder.Snapshot())["superseded-native-still-running-at-end"]);
            // Develop cancels on selection; the native decode keeps draining.
            service.InvalidateAdjacentWarm();
            Assert.Equal(1, service.PreviewActivityCount);
            var counters = CullPerfCounters.Derive(recorder.Snapshot());
            Assert.Equal(1, counters["superseded-native-still-running-maximum"]);
            Assert.Equal(1, counters["superseded-native-still-running-at-end"]);

            using var cached = await service
                .LoadCachedPreviewAsync(target, target.EditSettings)
                .WaitAsync(TestWaits.Condition);
            Assert.Null(cached);
            Assert.Equal(1, service.PreviewActivityCount);
        }
        finally
        {
            // Disposal waits for the worker, so a failed join must not hang.
            loader.Release.Set();
        }
        await TestWaits.UntilAsync(() => service.PreviewActivityCount == 0);
        Assert.Equal(0, CullPerfCounters.Derive(recorder.Snapshot())["superseded-native-still-running-at-end"]);
        Assert.Equal(0, CullPerfCounters.Derive(recorder.Snapshot())["native-active-at-end"]);
    }

    [WindowsTheory]
    [InlineData("retained")]
    [InlineData("persisted")]
    public async Task AFreshRequestAfterTheWarmCompletedUsesItsResultInsteadOfDecoding(
        string where)
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync($"join-late-{where}");
        var target = await CreateCatalogImageAsync(catalog, "target.jpg");
        var writerRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new PreviewCacheService(
            catalog,
            8,
            writerRelease.Task,
            TimeSpan.FromSeconds(2));
        var loader = new RecordingLoader();
        await using var service = CreateService(
            catalog,
            loader,
            new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            cache);

        Assert.True(service.TryStartAdjacentWarm(target));
        await TestWaits.UntilAsync(() => service.AdjacentWarmEntryCount == 1);
        await TestWaits.UntilAsync(() => service.PreviewActivityCount == 0);
        if (where == "persisted")
        {
            writerRelease.TrySetResult();
            await TestWaits.UntilAsync(() =>
                service.AdjacentWarmEntryCount == 0 && cache.PendingWrites == 0);
        }

        // The worker is gone; its result is retained or on disk.
        using var fresh = await service.LoadComparePreviewAsync(
            target, target.EditSettings, joinAdjacentWarm: true);
        Assert.NotNull(fresh);
        Assert.Equal(["target.jpg"], loader.Paths);
        writerRelease.TrySetResult();
        await TestWaits.UntilAsync(() => cache.PendingWrites == 0);
    }

    [WindowsFact]
    public async Task AMatchedCacheEntryWithoutDimensionsIsNotJoinedAndRendersFresh()
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync("join-dimensionless");
        var target = await CreateCatalogImageAsync(catalog, "target.jpg");
        var cache = new PreviewCacheService(catalog);
        var hash = RenderSettingsHash.Compute(target.EditSettings);
        using (var pixels = new MagickImage(MagickColors.Orange, 32, 24))
        {
            cache.QueueSaveToCache(target, pixels, hash);
        }
        await TestWaits.UntilAsync(() => cache.PendingWrites == 0);
        var loader = new RecordingLoader();
        await using var service = CreateService(
            catalog,
            loader,
            new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            cache);

        using var fresh = await service.LoadComparePreviewAsync(
            target, target.EditSettings, joinAdjacentWarm: true);
        Assert.NotNull(fresh);
        Assert.Equal(new Avalonia.PixelSize(48, 32), fresh!.OriginalViewPixelSize);
        Assert.Equal(["target.jpg"], loader.Paths);
        await TestWaits.UntilAsync(() => cache.PendingWrites == 0);
    }

    [WindowsFact]
    public async Task PendingWritesCountsFromEnqueueUntilTheWriteLands()
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync("handoff-pending");
        var image = await CreateCatalogImageAsync(catalog, "image.jpg");
        var inHand = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new PreviewCacheService(
            catalog,
            8,
            Task.CompletedTask,
            TimeSpan.FromSeconds(2),
            writerInHandGate: inHand.Task);
        var hash = RenderSettingsHash.Compute(image.EditSettings);
        using (var pixels = new MagickImage(MagickColors.Orange, 32, 24))
        {
            cache.QueueSaveToCache(image, pixels, hash);
        }
        // Queued, dequeued, and in hand all read as one outstanding write; the
        // handoff must never see zero before the file exists.
        Assert.Equal(1, cache.PendingWrites);
        await TestWaits.UntilAsync(() => cache.WriterInHandCount == 1);
        Assert.Equal(1, cache.PendingWrites);
        Assert.False(cache.HasSettingsMatchedEntry(image, hash));

        inHand.TrySetResult();
        await TestWaits.UntilAsync(() => cache.PendingWrites == 0);
        Assert.True(cache.HasSettingsMatchedEntry(image, hash));
        await cache.DisposeAsync();
    }

    [WindowsFact]
    public async Task OwnWriteReleasesHandoffWhileALaterDepartingPreviewRemainsParked()
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync("handoff-own-write");
        var first = await CreateCatalogImageAsync(catalog, "first.jpg");
        var next = await CreateCatalogImageAsync(catalog, "next.jpg");
        var departing = await CreateCatalogImageAsync(catalog, "departing.jpg");
        using var writerGate = new SemaphoreSlim(0);
        var cache = new PreviewCacheService(catalog, 8, Task.CompletedTask,
            TimeSpan.Zero, beforeWrite: () => writerGate.WaitAsync());
        var recorder = new CullPerfRecorder();
        var loader = new RecordingLoader();
        await using var service = CreateService(catalog, loader,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally), cache);
        service.CullPerf = recorder;
        try
        {
            Assert.True(service.TryStartAdjacentWarm(first));
            await TestWaits.UntilAsync(() => service.PreviewActivityCount == 0);
            Assert.Equal(1, service.AdjacentWarmEntryCount);
            await TestWaits.UntilAsync(() => cache.WriterInHandCount == 1);
            using var pixels = new MagickImage(MagickColors.Blue, 32, 24);
            var laterWrite = cache.QueueSaveToCache(departing, pixels, "departing", default);

            Assert.True(service.TryStartAdjacentWarm(next));
            await TestWaits.UntilAsync(() => recorder.Snapshot().Any(e =>
                e.Kind == "WarmStart" && e.ImageId == next.CatalogId));
            Assert.Equal(["first.jpg"], loader.Paths);
            writerGate.Release();

            await TestWaits.UntilAsync(() => service.PreviewActivityCount == 0);
            Assert.Equal(["first.jpg", "next.jpg"], loader.Paths);
            Assert.False(laterWrite.IsCompleted);
            Assert.Equal(2, cache.PendingWrites);
            Assert.True(cache.HasSettingsMatchedEntry(first,
                RenderSettingsHash.Compute(first.EditSettings)));
            var events = recorder.Snapshot();
            Assert.Contains(events, e => e.Kind == "WarmDrop" && e.ImageId == first.CatalogId);
            Assert.Contains(events, e => e.Kind == "WarmHandoffResolved" &&
                e.ImageId == next.CatalogId);
        }
        finally
        {
            writerGate.Release(3);
            await TestWaits.UntilAsync(() => cache.PendingWrites == 0);
        }
    }

    [WindowsTheory]
    [InlineData("capacity")]
    [InlineData("stale-source")]
    [InlineData("matched-disk")]
    public async Task DroppedWriteKeepsOnlyCopyUnlessSourceIsStaleOrDiskMatches(string reason)
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync($"handoff-dropped-{reason}");
        var first = await CreateCatalogImageAsync(catalog, "first.jpg");
        var next = await CreateCatalogImageAsync(catalog, "next.jpg");
        var departing = await CreateCatalogImageAsync(catalog, "departing.jpg");
        var writerRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new PreviewCacheService(catalog, 1, writerRelease.Task, TimeSpan.Zero);
        var recorder = new CullPerfRecorder();
        var loader = new BlockingLoader { Block = false };
        await using var service = CreateService(catalog, loader,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally), cache);
        service.CullPerf = recorder;
        try
        {
            Assert.True(service.TryStartAdjacentWarm(first));
            await TestWaits.UntilAsync(() => service.PreviewActivityCount == 0);
            Assert.Equal(1, service.AdjacentWarmEntryCount);
            if (reason == "stale-source")
                File.SetLastWriteTimeUtc(first.FilePath, DateTime.UtcNow.AddMinutes(1));
            if (reason == "matched-disk")
            {
                await using var otherCache = new PreviewCacheService(catalog);
                using var pixels = new MagickImage(MagickColors.Blue, 32, 24);
                Assert.True(await otherCache.QueueSaveToCache(first, pixels,
                    RenderSettingsHash.Compute(first.EditSettings), default).WaitAsync(TestWaits.Condition));
            }
            loader.Block = true;
            loader.Started.Reset();
            Assert.True(service.TryStartAdjacentWarm(next));
            using var departingPixels = new MagickImage(MagickColors.Blue, 32, 24);
            var laterWrite = cache.QueueSaveToCache(departing, departingPixels, "departing", default);

            Assert.True(loader.Started.Wait(TestWaits.Condition));
            Assert.Equal(reason == "capacity" ? 1 : 0, service.AdjacentWarmEntryCount);
            Assert.False(laterWrite.IsCompleted);
            Assert.Contains(recorder.Snapshot(), e =>
                e.Kind == "CacheWriteDropped" && e.ImageId == first.CatalogId);
        }
        finally
        {
            service.InvalidateAdjacentWarm();
            loader.Block = false;
            await TestWaits.UntilAsync(() => service.PreviewActivityCount == 0);
            writerRelease.TrySetResult();
            await TestWaits.UntilAsync(() => cache.PendingWrites == 0);
        }
    }

    private sealed class DrainingLoader(CullPerfRecorder recorder) : RecordingLoader
    {
        public ManualResetEventSlim Started { get; } = new();
        public ManualResetEventSlim Release { get; } = new();

        public override BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            recorder.Record("NativeStart", file.CatalogId);
            try
            {
                Started.Set();
                Release.Wait();
                return base.LoadPreviewBaseWithOutcome(file, decode, cancellationToken);
            }
            finally { recorder.Record("NativeEnd", file.CatalogId); }
        }
    }
}
