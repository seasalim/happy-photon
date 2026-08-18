using ImageMagick;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class BackgroundActivityViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"happy-photon-activity-{Guid.NewGuid():N}");

    [Fact]
    public async Task ThumbnailOperationsHaveOneExclusiveOwner()
    {
        using var catalog = new CatalogService(_root);
        var vm = new MainWindowViewModel(catalog);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var operation = vm.TrackDirectThumbnailOperation(release.Task);

        Assert.Equal(1, vm.DirectThumbnailActivityCount);
        Assert.Equal(0, vm.InitialThumbnailBatchCount);
        Assert.Equal(1, vm.CaptureBackgroundActivitySnapshot().ThumbnailCount);

        release.SetResult();
        await operation;
        await TestWaits.UntilAsync(() => vm.DirectThumbnailActivityCount == 0);
        Assert.Equal(0, vm.DirectThumbnailActivityCount);

        using (vm.BeginInitialThumbnailBatch())
        {
            Assert.Equal(1, vm.InitialThumbnailBatchCount);
            Assert.Equal(1, vm.CaptureBackgroundActivitySnapshot().ThumbnailCount);
        }
        Assert.Equal(0, vm.InitialThumbnailBatchCount);
        await vm.DisposeAsync();
    }

    [Fact]
    public void OverlappingExportScopesAggregateAndDisposeIndependently()
    {
        var registry = new BackgroundExportActivityRegistry(() => { });
        using var first = registry.Begin(4);
        using var second = registry.Begin(6);
        first.Report(2);
        second.Report(1);

        Assert.Equal(new ExportActivitySnapshot(2, 3, 10), registry.GetSnapshot());

        first.Dispose();
        Assert.Equal(new ExportActivitySnapshot(1, 1, 6), registry.GetSnapshot());
        first.Report(4);
        Assert.Equal(new ExportActivitySnapshot(1, 1, 6), registry.GetSnapshot());

        second.Report(5);
        Assert.Equal(new ExportActivitySnapshot(1, 5, 6), registry.GetSnapshot());
    }

    [Fact]
    public async Task CacheActivityIncludesWriterInHandAfterDequeue()
    {
        Directory.CreateDirectory(_root);
        var sourcePath = Path.Combine(_root, "source.jpg");
        await File.WriteAllBytesAsync(sourcePath, [1]);
        using var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        var writerGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new PreviewCacheService(
            catalog,
            2,
            Task.CompletedTask,
            TimeSpan.FromSeconds(5),
            writerGate.Task);
        var image = new HappyPhoton.Models.ImageFile(sourcePath) { CatalogId = 1 };
        using var preview = new MagickImage(MagickColors.Blue, 32, 24);

        cache.QueueSaveToCache(image, preview, "settings");
        await TestWaits.UntilAsync(() => cache.WriterInHandCount == 1);

        Assert.Equal(1, cache.PendingWrites);
        writerGate.SetResult();
        await cache.DisposeAsync();
        Assert.Equal(0, cache.PendingWrites);
    }

    [Fact]
    public async Task MetadataActivityCountsOneUniqueSingleFlightLoad()
    {
        var extractionStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new ManualResetEventSlim();
        var service = new MetadataService(
            _ =>
            {
                extractionStarted.SetResult();
                release.Wait();
                return new HappyPhoton.Models.ImageMetadata();
            },
            action =>
            {
                action();
                return Task.CompletedTask;
            });
        var image = new HappyPhoton.Models.ImageFile("test.jpg");

        var first = service.LoadAsync(image);
        var second = service.LoadAsync(image);
        await extractionStarted.Task.WaitAsync(TestWaits.Condition);

        Assert.Same(first, second);
        Assert.Equal(1, service.InFlightCount);
        release.Set();
        await first;
        Assert.Equal(0, service.InFlightCount);
        release.Dispose();
    }

    [Fact]
    public async Task ProductionExportInitiatorsEachOpenExactlyOneScope()
    {
        Directory.CreateDirectory(_root);
        using var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        await catalog.InitializeAsync();
        var vm = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask);
        vm.ExportSettings.OutputFolder = Path.Combine(_root, "vm-export");

        var before = vm.ExportActivityScopeStartCount;
        Assert.Equal(
            0,
            (await vm.ExportBatchAsync(Array.Empty<ImageFile>())).ExportedCount);
        Assert.Equal(before + 1, vm.ExportActivityScopeStartCount);

        before = vm.ExportActivityScopeStartCount;
        Assert.Equal(
            0,
            (await vm.ExportBatchApprovedAsync(Array.Empty<ImageFile>())).ExportedCount);
        Assert.Equal(before + 1, vm.ExportActivityScopeStartCount);

        await using var imageService = new ImageService(
            catalog,
            new NullBaseLoader(),
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        var agent = new AgentToolService(vm, imageService, catalog);
        before = vm.ExportActivityScopeStartCount;
        var result = await agent.ExportResolvedImagesAsync(
            [],
            [],
            new ExportSettings { OutputFolder = Path.Combine(_root, "agent-export") },
            [],
            useSubfolders: false,
            []);

        Assert.Empty(result.Exported);
        Assert.Equal(before + 1, vm.ExportActivityScopeStartCount);
        await vm.DisposeAsync();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class NullBaseLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(ImageFile file, BaseDecodeSettings decode, CancellationToken cancellationToken) => BaseImageLoadOutcome.FromImage(LoadPreviewBase(file, decode, cancellationToken), BaseImageLoadFailure.DecodeFailed);

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) => null;

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) => null;
    }
}
