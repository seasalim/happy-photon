using System.Collections.Concurrent;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed partial class AdjacentPreviewWarmTests : IDisposable
{
    private readonly AvaloniaTestFixture _fixture;
    private readonly TemporaryDirectory _root = new();

    public AdjacentPreviewWarmTests(AvaloniaTestFixture fixture) =>
        _fixture = fixture;

    [WindowsFact]
    public async Task WarmResultUsesCachedReadPathAndHonorsLiveAvailability()
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync("handoff");
        var image = await CreateCatalogImageAsync(catalog, "target.jpg");
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.AvailableLocally);
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
            availability,
            cache);
        var renderEvents = 0;
        var outcomeEvents = 0;
        service.RenderStarted += () => Interlocked.Increment(ref renderEvents);
        service.PreviewLoadCompleted += (_, _) =>
            Interlocked.Increment(ref outcomeEvents);

        Assert.True(service.TryStartAdjacentWarm(image));
        await TestWaits.UntilAsync(() => service.AdjacentWarmEntryCount == 1);
        using var cached = await service.LoadCachedPreviewAsync(
            image,
            image.EditSettings);

        Assert.NotNull(cached);
        Assert.True(cached!.SettingsMatch);
        Assert.Equal(new Avalonia.PixelSize(48, 32), cached.OriginalViewPixelSize);
        Assert.NotNull(cached.Histogram?.Waveform);
        Assert.NotNull(cached.Clipping);
        Assert.Equal(0, renderEvents);
        Assert.Equal(0, outcomeEvents);
        Assert.Equal(0, service.RetainedBasePairCount);

        availability.Availability = SourceAvailability.RequiresHydration;
        using var refused = await service.LoadCachedPreviewAsync(
            image,
            image.EditSettings);
        Assert.Null(refused);
        Assert.Equal(0, service.AdjacentWarmEntryCount);

        writerRelease.TrySetResult();
        await TestWaits.UntilAsync(() => cache.PendingWrites == 0);
    }

    [WindowsFact]
    public async Task ActiveWorkerExposesCapacityWaitForLatestReplacement()
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync("capacity");
        var first = await CreateCatalogImageAsync(catalog, "first.jpg");
        var second = await CreateCatalogImageAsync(catalog, "second.jpg");
        var loader = new BlockingLoader();
        await using var service = CreateService(
            catalog,
            loader,
            new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        var activityStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.AdjacentWarmWorkStarted += () => activityStarted.TrySetResult();

        Assert.True(service.TryStartAdjacentWarm(first));
        await activityStarted.Task.WaitAsync(TestWaits.Condition);
        Assert.True(loader.Started.Wait(TestWaits.Condition));
        Assert.Equal(1, service.PreviewActivityCount);
        Assert.False(service.TryStartAdjacentWarm(second, out var blockingWorker));
        loader.Block = false;
        await blockingWorker!.WaitAsync(TestWaits.Condition);
        Assert.True(service.TryStartAdjacentWarm(second));
        await TestWaits.UntilAsync(() => service.PreviewActivityCount == 0);
        Assert.Equal(2, loader.DecodeCount);
        Assert.Equal(0, service.RetainedBasePairCount);
    }

    [WindowsTheory]
    [InlineData((int)SourceAvailability.RequiresHydration)]
    [InlineData((int)SourceAvailability.Unavailable)]
    public async Task NonLocalTargetsAreSkippedWithoutDecode(int availabilityValue)
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync($"skip-{availabilityValue}");
        var image = await CreateCatalogImageAsync(catalog, "target.jpg");
        var loader = new RecordingLoader();
        await using var service = CreateService(
            catalog,
            loader,
            new TestSourceAvailabilityService(
                (SourceAvailability)availabilityValue));

        Assert.False(service.TryStartAdjacentWarm(image));
        Assert.Empty(loader.Paths);
        Assert.Equal(0, service.PreviewActivityCount);
    }

    [WindowsFact]
    public async Task MissingIdentityAndMatchingDiskEntryAreSkipped()
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync("skip-cache");
        var missingIdentity = CreateImage("missing.jpg");
        var cached = await CreateCatalogImageAsync(catalog, "cached.jpg");
        var cache = new PreviewCacheService(catalog);
        using (var pixels = new MagickImage(MagickColors.Orange, 32, 24))
        {
            cache.QueueSaveToCache(
                cached,
                pixels,
                RenderSettingsHash.Compute(cached.EditSettings));
        }
        await TestWaits.UntilAsync(() => cache.PendingWrites == 0);
        var loader = new RecordingLoader();
        await using var service = CreateService(
            catalog,
            loader,
            new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            cache);

        Assert.False(service.TryStartAdjacentWarm(missingIdentity));
        Assert.False(service.TryStartAdjacentWarm(cached));
        Assert.Empty(loader.Paths);
    }

    [WindowsTheory]
    [InlineData(".jpg", false)]
    [InlineData(".cr2", false)]
    [InlineData(".cr2", true)]
    public async Task WarmCacheHashReadsBackMatched(
        string extension,
        bool selectedProfile)
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync(
            $"hash-{extension[1..]}-{selectedProfile}");
        var image = await CreateCatalogImageAsync(
            catalog,
            $"target{extension}");
        if (selectedProfile)
        {
            var profilePath = SyntheticDcpFactory.WriteTemporary(_root.Path);
            var snapshot = new DcpProfileReader().ReadExternalSnapshot(profilePath);
            image.EditSettings.RawProfile = new RawProfileSelection
            {
                Source = RawProfileSource.UserFile,
                Location = profilePath,
                ContentHash = snapshot.ContentHash
            };
        }
        var writerRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new PreviewCacheService(
            catalog,
            8,
            writerRelease.Task,
            TimeSpan.FromSeconds(2));
        await using var service = CreateService(
            catalog,
            new RecordingLoader(),
            new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            cache);

        Assert.True(service.TryStartAdjacentWarm(image));
        await TestWaits.UntilAsync(() => service.AdjacentWarmEntryCount == 1);
        using var cached = await service.LoadCachedPreviewAsync(
            image,
            image.EditSettings);
        Assert.NotNull(cached);
        Assert.True(cached!.SettingsMatch);

        writerRelease.TrySetResult();
        await TestWaits.UntilAsync(() => cache.PendingWrites == 0);
        await TestWaits.UntilAsync(() => service.AdjacentWarmEntryCount == 0);
    }

    [WindowsTheory]
    [InlineData(".jpg", 75)]
    [InlineData(".cr2", 75)]
    public async Task SettledDevelopSelectionWarmsInTravelDirectionWithoutWrap(
        string extension, int idleMilliseconds)
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync($"direction-{extension[1..]}");
        var images = new[]
        {
            await CreateCatalogImageAsync(catalog, $"first{extension}"),
            await CreateCatalogImageAsync(catalog, $"second{extension}"),
            await CreateCatalogImageAsync(catalog, $"third{extension}"),
            await CreateCatalogImageAsync(catalog, $"fourth{extension}")
        };
        var clock = new TestTimeProvider();
        var loader = new RecordingLoader();
        var vm = new MainWindowViewModel(
            catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            timeProvider: clock)
        {
            IsDevelopMode = true
        };
        vm.Browse.SetImages(images);
        try
        {
            vm.SelectedImage = images[2];
            await TestWaits.UntilAsync(() => vm.PreviewImage != null);
            clock.Advance(TimeSpan.FromMilliseconds(idleMilliseconds - 1));
            await Task.Yield();
            Assert.Single(loader.Paths);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await TestWaits.UntilAsync(() => loader.Paths.Count >= 2);
            Assert.Equal($"fourth{extension}", loader.Paths.ElementAt(1));
            vm.SelectedImage = images[3];
            await TestWaits.UntilAsync(() =>
                vm.InitialPreviewActivityCount == 0 && vm.PreviewImage != null);
            var endDecodeCount = loader.Paths.Count;
            clock.Advance(TimeSpan.FromMilliseconds(idleMilliseconds));
            await Task.Yield();
            Assert.Equal(endDecodeCount, loader.Paths.Count);

            vm.SelectedImage = images[1];
            await TestWaits.UntilAsync(() =>
                loader.Paths.Count >= 4 && vm.PreviewImage != null);
            clock.Advance(TimeSpan.FromMilliseconds(idleMilliseconds));
            await TestWaits.UntilAsync(() => loader.Paths.Count >= 5);
            Assert.Equal($"first{extension}", loader.Paths.ElementAt(4));
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    [WindowsFact]
    public async Task WarmActivityFlowsThroughTheExistingPreviewIndicator()
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync("activity");
        var images = new[]
        {
            await CreateCatalogImageAsync(catalog, "current.jpg"),
            await CreateCatalogImageAsync(catalog, "target.jpg")
        };
        var clock = new TestTimeProvider();
        var loader = new AdjacentBlockingLoader("target.jpg");
        var vm = new MainWindowViewModel(
            catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            timeProvider: clock)
        {
            IsDevelopMode = true
        };
        vm.Browse.SetImages(images);

        try
        {
            vm.SelectedImage = images[0];
            await TestWaits.UntilAsync(() =>
                vm.PreviewImage != null && vm.InitialPreviewActivityCount == 0);
            clock.Advance(TimeSpan.FromMilliseconds(75));
            Assert.True(loader.Started.Wait(TestWaits.Condition));

            var now = DateTimeOffset.UtcNow;
            vm.PumpBackgroundActivity(now);
            vm.PumpBackgroundActivity(
                now + BackgroundActivityAggregator.ShowDelay);
            Assert.Equal(1, vm.CaptureBackgroundActivitySnapshot().PreviewCount);
            Assert.True(vm.BackgroundActivity.IsVisible);
            Assert.Contains("Preparing preview", vm.BackgroundActivity.Tooltip);

            vm.SelectedImage = null;
            await TestWaits.UntilAsync(() =>
                vm.ImageService.Previews.PreviewActivityCount == 0);
            await TestWaits.UntilAsync(() =>
                vm.CaptureBackgroundActivitySnapshot().IsEmpty);
            var emptyAt = now + BackgroundActivityAggregator.ShowDelay;
            vm.PumpBackgroundActivity(emptyAt);
            vm.PumpBackgroundActivity(
                emptyAt + BackgroundActivityAggregator.HideDelay);
            Assert.Equal(0, vm.CaptureBackgroundActivitySnapshot().PreviewCount);
            Assert.False(vm.BackgroundActivity.IsVisible);
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    [WindowsTheory]
    [InlineData("edit")]
    [InlineData("filter")]
    [InlineData("folder")]
    [InlineData("browse")]
    [InlineData("fullscreen")]
    public async Task ViewTransitionsCancelAnActiveWarm(string transition)
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync($"cancel-{transition}");
        var images = new[]
        {
            await CreateCatalogImageAsync(catalog, "current.jpg"),
            await CreateCatalogImageAsync(catalog, "target.jpg")
        };
        var clock = new TestTimeProvider();
        var loader = new AdjacentBlockingLoader("target.jpg");
        var vm = new MainWindowViewModel(catalog, loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally), timeProvider: clock)
            { IsDevelopMode = true };
        vm.Browse.SetImages(images);
        try
        {
            vm.SelectedImage = images[0];
            await TestWaits.UntilAsync(() =>
                vm.PreviewImage != null && vm.InitialPreviewActivityCount == 0);
            clock.Advance(TimeSpan.FromMilliseconds(75));
            Assert.True(loader.Started.Wait(TestWaits.Condition));
            switch (transition)
            {
                case "edit": vm.Exposure = 0.5; break;
                case "filter": vm.Browse.FlagFilter = FlagFilter.Picked; break;
                case "folder": await vm.LoadFolderAsync(
                    Directory.CreateDirectory(Path.Combine(_root.Path, "empty")).FullName); break;
                case "browse": vm.IsDevelopMode = false; break;
                case "fullscreen": vm.IsFullScreenMode = true; break;
            }
            await TestWaits.UntilAsync(() =>
                vm.ImageService.Previews.PreviewActivityCount == 0);
        }
        finally { await vm.DisposeAsync(); }
    }

    private PreviewService CreateService(
        CatalogService catalog,
        IBaseImageLoader loader,
        TestSourceAvailabilityService availability,
        PreviewCacheService? cache = null) =>
        new(
            catalog,
            new GatedBaseImageLoader(loader, availability),
            new RenderPipeline(),
            cache,
            createRenderedThumbnail: false,
            sourceAvailability: availability);

    private async Task<CatalogService> CreateCatalogAsync(string name)
    {
        var catalog = new CatalogService(Path.Combine(_root.Path, name));
        await catalog.InitializeAsync();
        return catalog;
    }

    private async Task<ImageFile> CreateCatalogImageAsync(
        CatalogService catalog,
        string name)
    {
        var image = CreateImage(name);
        await image.EnsureCatalogIdAsync(catalog);
        return image;
    }

    private ImageFile CreateImage(string name)
    {
        var path = Path.Combine(_root.Path, name);
        File.WriteAllBytes(path, [1]);
        return new ImageFile(path);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _root.Dispose();
    }

    private class RecordingLoader : IBaseImageLoader
    {
        public ConcurrentQueue<string> Paths { get; } = new();

        public virtual BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Paths.Enqueue(file.FileName);
            return BaseImageLoadOutcome.Loaded(CreateBase(file, decode));
        }

        public bool CanLoad(ImageFile file) => true;

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        protected static BaseImage CreateBase(
            ImageFile file,
            BaseDecodeSettings decode) =>
            new(
                new MagickImage(MagickColors.Gray, 48, 32)
                {
                    Depth = 16,
                    ColorSpace = ColorSpace.RGB
                },
                new BaseImageInfo(
                    file.IsRaw ? BaseSourceKind.RawLibRaw : BaseSourceKind.Standard,
                    file.IsRaw,
                    decode,
                    null,
                    null,
                    6504,
                    0,
                    false,
                    null,
                    1,
                    48,
                    32)
                {
                    ProfileToken = decode.ProfileResolution?.Token ?? string.Empty
                });
    }

    private sealed class BlockingLoader : RecordingLoader
    {
        private int _decodeCount;
        public ManualResetEventSlim Started { get; } = new();
        public bool Block { get; set; } = true;
        public int DecodeCount => Volatile.Read(ref _decodeCount);

        public override BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _decodeCount);
            Started.Set();
            while (Block)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Thread.Yield();
            }
            return base.LoadPreviewBaseWithOutcome(file, decode, cancellationToken);
        }
    }

    private sealed class AdjacentBlockingLoader(string target) : RecordingLoader
    {
        public ManualResetEventSlim Started { get; } = new();

        public override BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            if (file.FileName == target)
            {
                Started.Set();
                cancellationToken.WaitHandle.WaitOne();
                cancellationToken.ThrowIfCancellationRequested();
            }
            return base.LoadPreviewBaseWithOutcome(file, decode, cancellationToken);
        }
    }
}
