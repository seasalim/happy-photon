using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class ThumbnailHydrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-thumbnail-hydration-{Guid.NewGuid():N}");

    [WindowsFact]
    public async Task WarmCache_LoadsWithoutAvailabilityOrSourceRead()
    {
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.RequiresHydration);
        await using var context = await CreateContextAsync(availability);
        context.WriteWarmCache();

        using var result = await context.Service.LoadUneditedThumbnailAsync(
            context.Image);

        Assert.Equal(ThumbnailLoadStatus.Loaded, result.Status);
        Assert.NotNull(result.Bitmap);
        Assert.Equal(0, availability.CallCount);
        Assert.Equal(0, context.SourceCalls);
    }

    [WindowsFact]
    public async Task WarmUndersizedCacheDefersOnlyLargeQualityUpgrade()
    {
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.RequiresHydration);
        await using var context = await CreateContextAsync(availability);
        context.WriteWarmCache();
        using var catalog = new CatalogService(Path.Combine(_root, "view-model"));
        await catalog.InitializeAsync();
        await using var viewModel = new MainWindowViewModel(catalog);
        viewModel.Library.SetImages([context.Image]);
        viewModel.InitializeCloudSourceCount([context.Image]);

        using var result = await context.Service.LoadUneditedThumbnailAsync(
            context.Image,
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Large));
        viewModel.ApplyThumbnailLoadResult(context.Image, result);
        viewModel.Library.ReplaceThumbnail(
            context.Image,
            result.DetachBitmap());

        Assert.Equal(ThumbnailLoadStatus.Loaded, result.Status);
        Assert.True(result.BetterResultDeferredForHydration);
        Assert.False(result.SatisfiesMinimumDimension);
        Assert.Equal(512, context.Image.ThumbnailUpgradeDeferredDimension);
        Assert.False(context.Image.SourceRequiresHydration);
        Assert.False(context.Image.ShowCloudPlaceholder);
        Assert.Equal(0, viewModel.OnlineOnlyPhotoCount);
        Assert.Equal(0, context.SourceCalls);
    }

    [WindowsFact]
    public async Task KnownCloudImageKeepsBadgeCountWhileWarmBitmapHidesPlaceholder()
    {
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.RequiresHydration);
        await using var context = await CreateContextAsync(
            availability,
            SourceAvailability.RequiresHydration);
        context.WriteWarmCache();
        using var catalog = new CatalogService(Path.Combine(_root, "known-cloud-vm"));
        await catalog.InitializeAsync();
        await using var viewModel = new MainWindowViewModel(catalog);
        viewModel.Library.SetImages([context.Image]);
        viewModel.InitializeCloudSourceCount([context.Image]);

        using var result = await context.Service.LoadUneditedThumbnailAsync(
            context.Image,
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Large));
        viewModel.ApplyThumbnailLoadResult(context.Image, result);
        viewModel.Library.ReplaceThumbnail(
            context.Image,
            result.DetachBitmap());

        Assert.True(context.Image.SourceRequiresHydration);
        Assert.False(context.Image.ShowCloudPlaceholder);
        Assert.Equal(1, viewModel.OnlineOnlyPhotoCount);
    }

    [WindowsFact]
    public async Task CacheMiss_DefersWithoutSourceRead()
    {
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.RequiresHydration);
        await using var context = await CreateContextAsync(availability);

        using var result = await context.Service.LoadUneditedThumbnailAsync(
            context.Image);

        Assert.Equal(ThumbnailLoadStatus.DeferredForHydration, result.Status);
        Assert.Equal(
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Medium),
            result.Request);
        Assert.Null(result.Bitmap);
        Assert.Equal(1, availability.CallCount);
        Assert.Equal(0, context.SourceCalls);
    }

    [WindowsFact]
    public async Task EditedThumbnail_PreservesDeferralStatus()
    {
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.RequiresHydration);
        await using var context = await CreateContextAsync(availability);
        context.Image.EditSettings = new EditSettings { Exposure = 1 };

        using var result = await context.Service.LoadThumbnailAsync(context.Image);

        Assert.Equal(ThumbnailLoadStatus.DeferredForHydration, result.Status);
        Assert.Equal(0, context.SourceCalls);
    }

    [WindowsFact]
    public async Task UnavailableSource_FailsWithoutSourceRead()
    {
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.Unavailable);
        await using var context = await CreateContextAsync(availability);

        using var result = await context.Service.LoadUneditedThumbnailAsync(
            context.Image);

        Assert.Equal(ThumbnailLoadStatus.Failed, result.Status);
        Assert.Equal(
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Medium),
            result.Request);
        Assert.Equal(0, context.SourceCalls);
    }

    [WindowsFact]
    public async Task DeferredImage_RetriesOnceAfterBecomingLocal()
    {
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.RequiresHydration);
        await using var context = await CreateContextAsync(availability);

        using var deferred = await context.Service.LoadUneditedThumbnailAsync(
            context.Image);
        availability.Availability = SourceAvailability.AvailableLocally;
        using var loaded = await context.Service.LoadUneditedThumbnailAsync(
            context.Image);

        Assert.Equal(
            ThumbnailLoadStatus.DeferredForHydration,
            deferred.Status);
        Assert.Equal(ThumbnailLoadStatus.Loaded, loaded.Status);
        Assert.Equal(1, context.SourceCalls);
    }

    [WindowsFact]
    public async Task LiveGate_DoesNotTrustLocalEnumerationHint()
    {
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.RequiresHydration);
        await using var context = await CreateContextAsync(
            availability,
            SourceAvailability.AvailableLocally);

        using var result = await context.Service.LoadUneditedThumbnailAsync(
            context.Image);

        Assert.Equal(ThumbnailLoadStatus.DeferredForHydration, result.Status);
        Assert.Equal(0, context.SourceCalls);
    }

    [WindowsFact]
    public void ViewModel_DeferralIsNotAThumbnailFailure()
    {
        Directory.CreateDirectory(_root);
        WriteJpeg(Path.Combine(_root, "photo.jpg"));
        using var catalog = new CatalogService(Path.Combine(_root, "vm-catalog"));
        Complete(catalog.InitializeAsync());
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.RequiresHydration);
        var metadataStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseMetadata = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: async _ =>
            {
                metadataStarted.TrySetResult();
                await releaseMetadata.Task;
            },
            availabilityService: availability);
        var disposed = false;
        try
        {
            Complete(viewModel.LoadFolderAsync(_root));
            var image = Assert.Single(viewModel.Library.AllImages);
            WaitUntil(() => image.ThumbnailDeferredForHydration);

            Assert.False(image.ThumbnailLoadFailed);
            Assert.Null(image.Thumbnail);

            PumpFor(TimeSpan.FromMilliseconds(100));
            var callsAfterDeferral = availability.CallCount;
            for (var index = 0; index < 5; index++)
            {
                viewModel.RequestThumbnailRange(0, 1);
            }
            PumpFor(TimeSpan.FromMilliseconds(100));
            Assert.Equal(callsAfterDeferral, availability.CallCount);

            availability.Availability = SourceAvailability.AvailableLocally;
            viewModel.SelectedImage = null;
            viewModel.SelectedImage = image;
            WaitUntil(() => image.Thumbnail != null);
            WaitUntil(() => metadataStarted.Task.IsCompleted);
            Assert.False(image.ThumbnailDeferredForHydration);

            var disposeTask = viewModel.DisposeAsync().AsTask();
            Assert.False(disposeTask.IsCompleted);
            releaseMetadata.TrySetResult();
            Complete(disposeTask);
            disposed = true;
        }
        finally
        {
            releaseMetadata.TrySetResult();
            if (!disposed)
            {
                Complete(viewModel.DisposeAsync().AsTask());
            }
        }
    }

    [WindowsFact]
    public async Task LoadedThumbnail_ClearsStaleCloudHintAndFolderCount()
    {
        Directory.CreateDirectory(_root);
        var sourcePath = Path.Combine(_root, "stale-hint.jpg");
        WriteJpeg(sourcePath);
        using var catalog = new CatalogService(Path.Combine(_root, "stale-catalog"));
        await catalog.InitializeAsync();
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.AvailableLocally);
        var viewModel = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: availability);
        var image = new ImageFile(
            sourcePath,
            SourceAvailability.RequiresHydration);
        await image.EnsureCatalogIdAsync(catalog);
        await using var imageService = new ImageService(
            catalog,
            new NullBaseLoader(),
            availability);
        try
        {
            viewModel.Library.SetImages([image]);
            viewModel.InitializeCloudSourceCount([image]);

            Assert.True(image.ShowCloudPlaceholder);
            Assert.Equal(1, viewModel.OnlineOnlyPhotoCount);

            using var result = await imageService.LoadThumbnailAsync(
                image,
                CancellationToken.None);
            Assert.Equal(ThumbnailLoadStatus.Loaded, result.Status);
            viewModel.ApplyThumbnailLoadStatus(image, result.Status);

            Assert.False(image.SourceRequiresHydration);
            Assert.False(image.ShowCloudPlaceholder);
            Assert.Equal(0, viewModel.OnlineOnlyPhotoCount);
        }
        finally
        {
            await viewModel.DisposeAsync();
        }
    }

    [WindowsFact]
    public void CachedPreview_RemainsVisibleWhenFreshBaseIsDeferred()
    {
        Directory.CreateDirectory(_root);
        var sourcePath = Path.Combine(_root, "preview-source.jpg");
        WriteJpeg(sourcePath);
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-2));
        using var catalog = new CatalogService(Path.Combine(_root, "preview-catalog"));
        Complete(catalog.InitializeAsync());
        var image = new ImageFile(sourcePath);
        Complete(image.EnsureCatalogIdAsync(catalog));
        var cachePath = catalog.GetPreviewPath(image.CatalogId);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        WriteJpeg(cachePath);
        File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow);
        var viewModel = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.RequiresHydration));
        try
        {
            viewModel.Library.SetImages([image]);
            viewModel.IsDevelopMode = true;
            viewModel.Histogram = new HistogramData();

            viewModel.SelectedImage = image;
            WaitUntil(() =>
                viewModel.PreviewImage != null &&
                image.SourceRequiresHydration);

            Assert.NotNull(viewModel.PreviewImage);
            Assert.True(image.SourceRequiresHydration);
            Assert.Equal(1, viewModel.OnlineOnlyPhotoCount);
            Assert.False(viewModel.CanEditSelectedImage);
            Assert.Null(viewModel.Histogram);

            viewModel.RotateRightCommand.Execute(null);

            Assert.Equal(0, image.EditSettings.Rotation);
        }
        finally
        {
            Complete(viewModel.DisposeAsync().AsTask());
        }
    }

    private async Task<ThumbnailContext> CreateContextAsync(
        TestSourceAvailabilityService availability,
        SourceAvailability hint = SourceAvailability.Unknown)
    {
        Directory.CreateDirectory(_root);
        var sourcePath = Path.Combine(_root, $"{Guid.NewGuid():N}.jpg");
        WriteJpeg(sourcePath);
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-2));
        return await ThumbnailContext.CreateAsync(
            Path.Combine(_root, Guid.NewGuid().ToString("N")),
            sourcePath,
            hint,
            availability);
    }

    private static WriteableBitmap CreateBitmap() => new(
        new PixelSize(1, 1),
        new Vector(96, 96),
        PixelFormat.Bgra8888,
        AlphaFormat.Premul);

    private static void WriteJpeg(string path)
    {
        using var image = new MagickImage(MagickColors.Gray, 150, 100);
        image.Write(path, MagickFormat.Jpeg);
    }

    private static void WaitUntil(Func<bool> predicate)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!predicate() && DateTime.UtcNow < timeout)
        {
            Thread.Sleep(10);
        }
        Assert.True(predicate());
    }

    private static void PumpFor(TimeSpan duration)
    {
        var timeout = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < timeout)
        {
            Thread.Sleep(10);
        }
    }

    private static void Complete(Task task) => task.GetAwaiter().GetResult();

    private static T Complete<T>(Task<T> task) => task.GetAwaiter().GetResult();

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class ThumbnailContext : IAsyncDisposable
    {
        private readonly string _sourcePath;
        private int _sourceCalls;

        private ThumbnailContext(
            CatalogService catalog,
            RenderedThumbnailCacheService renderedCache,
            ThumbnailService service,
            ImageFile image,
            string sourcePath)
        {
            Catalog = catalog;
            RenderedCache = renderedCache;
            Service = service;
            Image = image;
            _sourcePath = sourcePath;
        }

        internal CatalogService Catalog { get; }
        internal RenderedThumbnailCacheService RenderedCache { get; }
        internal ThumbnailService Service { get; }
        internal ImageFile Image { get; }
        internal int SourceCalls => Volatile.Read(ref _sourceCalls);

        internal static async Task<ThumbnailContext> CreateAsync(
            string catalogPath,
            string sourcePath,
            SourceAvailability hint,
            ISourceAvailabilityService availability)
        {
            var catalog = new CatalogService(catalogPath);
            await catalog.InitializeAsync();
            var image = new ImageFile(sourcePath, hint);
            await image.EnsureCatalogIdAsync(catalog);
            var renderedCache = new RenderedThumbnailCacheService(catalog);
            ThumbnailContext? context = null;
            var service = new ThumbnailService(
                catalog,
                new MagickNetRawService(),
                new RenderPipeline(),
                renderedCache,
                availability,
                (_, _) =>
                {
                    Interlocked.Increment(ref context!._sourceCalls);
                    return new Bitmap(sourcePath);
                });
            context = new ThumbnailContext(
                catalog,
                renderedCache,
                service,
                image,
                sourcePath);
            return context;
        }

        internal void WriteWarmCache()
        {
            var path = Catalog.GetThumbnailPath(Image.CatalogId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(_sourcePath, path);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }

        public async ValueTask DisposeAsync()
        {
            await Service.DisposeAsync();
            await RenderedCache.DisposeAsync();
            Catalog.Dispose();
        }
    }

    private sealed class NullBaseLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;
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
