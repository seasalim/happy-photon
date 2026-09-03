using Avalonia;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ComparePreviewCacheTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("compare-cache");

    [Fact]
    public async Task ReentryUsesCachedIdentityWithoutFurtherBaseLoads()
    {
        using var catalog = await _fx.CreateUniqueCatalogAsync();
        using var release = new ManualResetEventSlim();
        var loader = new BlockingBaseLoader(release);
        await using var vm = _fx.CreateViewModel(
            catalog, loader, _ => Task.CompletedTask);
        var images = await PrepareImagesAsync(catalog, 4);
        SelectAll(vm, images);

        vm.EnterCompareCommand.Execute(null);
        await loader.FirstStarted.Task.WaitAsync(TestWaits.Condition);
        vm.ActivateComparePaneCommand.Execute(vm.ComparePanes[2]);
        release.Set();
        await vm.CompareLoadingTask.WaitAsync(TestWaits.Condition);

        Assert.Same(images[2], vm.SelectedImage);
        Assert.Equal(4, loader.LoadCount);
        Assert.Equal(1, loader.MaximumConcurrentLoads);
        AssertPaintedWithIdentity(vm);
        await DrainPreviewWritesAsync(vm);

        vm.ExitCompareCommand.Execute(null);
        vm.EnterCompareCommand.Execute(null);
        await vm.CompareLoadingTask.WaitAsync(TestWaits.Condition);

        Assert.Equal(4, loader.LoadCount);
        AssertPaintedWithIdentity(vm);
        Assert.All(vm.ComparePanes, pane =>
        {
            Assert.False(pane.ShowLoadingMessage);
            Assert.Equal(0, pane.AchievableLongEdge);
        });
    }

    [Fact]
    public async Task HashMismatchRendersOnlyTheStalePane()
    {
        using var catalog = await _fx.CreateUniqueCatalogAsync();
        using var release = new ManualResetEventSlim(true);
        var loader = new BlockingBaseLoader(release);
        await using var vm = _fx.CreateViewModel(
            catalog, loader, _ => Task.CompletedTask);
        var images = await PrepareImagesAsync(catalog, 2);
        SelectAll(vm, images);

        vm.EnterCompareCommand.Execute(null);
        await vm.CompareLoadingTask.WaitAsync(TestWaits.Condition);
        await DrainPreviewWritesAsync(vm);
        vm.ExitCompareCommand.Execute(null);
        images[0].EditSettings.Exposure = 0.5;

        vm.EnterCompareCommand.Execute(null);
        await vm.CompareLoadingTask.WaitAsync(TestWaits.Condition);

        Assert.Equal(3, loader.LoadCount);
        AssertPaintedWithIdentity(vm);
    }

    [Fact]
    public async Task PreShippedBareHashSidecarsRenderOnceAndAreRewritten()
    {
        using var catalog = await _fx.CreateUniqueCatalogAsync();
        var images = await PrepareImagesAsync(catalog, 2);
        var cache = new PreviewCacheService(catalog);
        // Hand-write the format every shipped build produced: the preview JPEG
        // plus a sidecar holding nothing but the settings hash. This is what an
        // upgrading user actually has on disk, so it cannot be simulated by
        // calling the writer, which now always emits a document.
        var metadataPaths = new List<string>();
        foreach (var image in images)
        {
            var previewPath = catalog.GetPreviewPath(image.CatalogId);
            Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!);
            TestImages.WriteJpeg(previewPath, MagickColors.Purple, 24, 16);
            var metadataPath = cache.GetMetadataPath(image);
            File.WriteAllText(
                metadataPath,
                RenderSettingsHash.Compute(image.EditSettings));
            metadataPaths.Add(metadataPath);
        }
        await cache.DisposeAsync();

        foreach (var metadataPath in metadataPaths)
        {
            Assert.False(
                PreviewCacheMetadata.TryRead(metadataPath, out _),
                "A bare-hash sidecar must read as absent, not as hash-only metadata.");
        }

        using var release = new ManualResetEventSlim(true);
        var loader = new BlockingBaseLoader(release);
        await using var vm = _fx.CreateViewModel(
            catalog, loader, _ => Task.CompletedTask);
        SelectAll(vm, images);

        vm.EnterCompareCommand.Execute(null);
        await vm.CompareLoadingTask.WaitAsync(TestWaits.Condition);
        Assert.Equal(2, loader.LoadCount);
        await DrainPreviewWritesAsync(vm);
        vm.ExitCompareCommand.Execute(null);
        vm.EnterCompareCommand.Execute(null);
        await vm.CompareLoadingTask.WaitAsync(TestWaits.Condition);

        Assert.Equal(2, loader.LoadCount);
        AssertPaintedWithIdentity(vm);
        foreach (var metadataPath in metadataPaths)
        {
            Assert.True(PreviewCacheMetadata.TryRead(metadataPath, out var rewritten));
            Assert.NotNull(rewritten.Identity);
        }
    }

    [Fact]
    public async Task IdentitylessWriteRoundTripsAsHashOnlyDocument()
    {
        using var catalog = await _fx.CreateUniqueCatalogAsync();
        var images = await PrepareImagesAsync(catalog, 1);
        var cache = new PreviewCacheService(catalog);
        var hash = RenderSettingsHash.Compute(images[0].EditSettings);
        using (var preview = new MagickImage(MagickColors.Purple, 24, 16))
        {
            cache.QueueSaveToCache(images[0], preview, hash);
        }
        await cache.DisposeAsync();

        // One format on disk: a write with no identity is still a document, it
        // simply carries no dimensions.
        var text = File.ReadAllText(cache.GetMetadataPath(images[0])).Trim();
        Assert.StartsWith("{", text);
        Assert.True(PreviewCacheMetadata.TryRead(
            cache.GetMetadataPath(images[0]),
            out var metadata));
        Assert.Equal(hash, metadata.SettingsHash);
        Assert.Null(metadata.Identity);
    }

    [Fact]
    public async Task EntryWithoutMetadataSidecarRenders()
    {
        using var catalog = await _fx.CreateUniqueCatalogAsync();
        var images = await PrepareImagesAsync(catalog, 2);
        foreach (var image in images)
            File.SetLastWriteTimeUtc(image.FilePath, DateTime.UtcNow.AddMinutes(-1));
        var cache = new PreviewCacheService(catalog);
        using (var preview = new MagickImage(MagickColors.Purple, 24, 16))
        {
            cache.QueueSaveToCache(
                images[0],
                preview,
                RenderSettingsHash.Compute(images[0].EditSettings),
                new PreviewCacheIdentity(
                    new PixelSize(48, 32),
                    new PixelSize(48, 32)));
        }
        await cache.DisposeAsync();
        var legacyPath = catalog.GetPreviewPath(images[1].CatalogId);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        TestImages.WriteJpeg(legacyPath, MagickColors.Orange, 24, 16);

        using var release = new ManualResetEventSlim(true);
        var loader = new BlockingBaseLoader(release);
        await using var vm = _fx.CreateViewModel(
            catalog, loader, _ => Task.CompletedTask);
        SelectAll(vm, images);

        vm.EnterCompareCommand.Execute(null);
        await vm.CompareLoadingTask.WaitAsync(TestWaits.Condition);

        Assert.Equal(1, loader.LoadCount);
        AssertPaintedWithIdentity(vm);
    }

    private async Task<ImageFile[]> PrepareImagesAsync(
        CatalogService catalog,
        int count)
    {
        var images = Enumerable.Range(0, count)
            .Select(index => new ImageFile(_fx.Path($"cache-{index}.jpg")))
            .ToArray();
        var states = await catalog.LoadOrCreateImageStatesAsync(
            images.Select(image => image.FilePath).ToArray());
        foreach (var image in images)
        {
            image.CatalogId = states[image.FilePath].Single().CatalogId;
            File.WriteAllBytes(image.FilePath, [0]);
        }
        return images;
    }

    private static void SelectAll(
        HappyPhoton.ViewModels.MainWindowViewModel vm,
        ImageFile[] images)
    {
        vm.Browse.SetImages(images);
        foreach (var image in images) vm.ToggleImageSelection(image);
        vm.SelectedImage = images[0];
    }

    private static void AssertPaintedWithIdentity(
        HappyPhoton.ViewModels.MainWindowViewModel vm) =>
        Assert.All(vm.ComparePanes, pane =>
        {
            Assert.NotNull(pane.Preview);
            Assert.Equal(new PixelSize(48, 32), pane.OriginalViewPixelSize);
        });

    private static Task DrainPreviewWritesAsync(
        HappyPhoton.ViewModels.MainWindowViewModel vm) =>
        TestWaits.UntilAsync(() =>
            vm.ImageService.Previews.PendingCacheWrites == 0);

    public void Dispose() => _fx.Dispose();

    private sealed class BlockingBaseLoader(ManualResetEventSlim release)
        : IBaseImageLoader
    {
        private int _active;
        private int _maximum;
        private int _loadCount;

        public TaskCompletionSource FirstStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public int LoadCount => Volatile.Read(ref _loadCount);
        public int MaximumConcurrentLoads => Volatile.Read(ref _maximum);
        public bool CanLoad(ImageFile file) => true;

        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _active);
            Interlocked.Increment(ref _loadCount);
            UpdateMaximum(active);
            FirstStarted.TrySetResult();
            release.Wait(cancellationToken);
            Interlocked.Decrement(ref _active);
            return BaseImageLoadOutcome.Loaded(new BaseImage(
                new MagickImage(MagickColors.DarkSlateGray, 48, 32)
                {
                    Depth = 16,
                    ColorSpace = ColorSpace.RGB
                },
                new BaseImageInfo(
                    BaseSourceKind.Standard, false, decode, null, null,
                    6504, 0, false, null, 1, 48, 32)));
        }

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) => null;

        private void UpdateMaximum(int candidate)
        {
            var current = Volatile.Read(ref _maximum);
            while (candidate > current)
            {
                var observed = Interlocked.CompareExchange(
                    ref _maximum, candidate, current);
                if (observed == current) return;
                current = observed;
            }
        }
    }
}
