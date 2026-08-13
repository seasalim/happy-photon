using Avalonia.Media.Imaging;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

/// <summary>
/// Facade service for image operations. Coordinates sub-services for loading,
/// processing, and exporting images.
/// </summary>
public class ImageService : IAsyncDisposable
{
    private readonly CatalogService _catalogService;
    private readonly IRawProcessingService _rawService;
    private readonly HistogramService _histogramService;
    private readonly ThumbnailService _thumbnailService;
    private readonly PreviewService _previewService;
    private readonly ImageExportService _exportService;
    private readonly MetadataService _metadataService;
    private readonly ISourceAvailabilityService _availabilityService;
    private readonly SourceHydrationService _sourceHydrationService;

    public event EventHandler<PreviewRefresh>? PreviewRefreshed
    {
        add => _previewService.PreviewRefreshed += value;
        remove => _previewService.PreviewRefreshed -= value;
    }

    public event EventHandler<PreviewBaseRefreshState>?
        BaseRefreshStateChanged
    {
        add => _previewService.BaseRefreshStateChanged += value;
        remove => _previewService.BaseRefreshStateChanged -= value;
    }

    public event Action? RenderedThumbnailWorkStarted
    {
        add => _previewService.RenderedThumbnailWorkStarted += value;
        remove => _previewService.RenderedThumbnailWorkStarted -= value;
    }

    public int ThumbnailActivityCount =>
        _previewService.RenderedThumbnailTaskCount;

    public int PreviewActivityCount => _previewService.PreviewActivityCount;

    public int CacheWriteActivityCount =>
        _thumbnailService.PendingCacheWrites + _previewService.PendingCacheWrites;

    public int MetadataActivityCount => _metadataService.InFlightCount;

    public ImageService(CatalogService catalogService)
        : this(
            catalogService,
            new BaseLoaderRouter(
                new RawBaseLoader(),
                new StandardBaseLoader()))
    {
    }

    internal ImageService(
        CatalogService catalogService,
        IBaseImageLoader baseLoader) : this(
            catalogService,
            baseLoader,
            new SourceAvailabilityService())
    {
    }

    internal ImageService(
        CatalogService catalogService,
        IBaseImageLoader baseLoader,
        ISourceAvailabilityService availabilityService)
    {
        _catalogService = catalogService;
        ArgumentNullException.ThrowIfNull(baseLoader);
        _availabilityService = availabilityService ??
            throw new ArgumentNullException(nameof(availabilityService));
        _sourceHydrationService = new SourceHydrationService(
            _availabilityService);
        var gatedBaseLoader = new GatedBaseImageLoader(
            baseLoader,
            _availabilityService);

        // Initialize RAW processing service
        var libRawService = new LibRawProcessingService();
        _rawService = libRawService.IsAvailable ? libRawService : new MagickNetRawService();

        // Initialize sub-services
        _histogramService = new HistogramService();
        var renderPipeline = new RenderPipeline();
        var renderedThumbnailCache =
            new RenderedThumbnailCacheService(catalogService);
        _thumbnailService = new ThumbnailService(
            catalogService,
            _rawService,
            renderPipeline,
            renderedThumbnailCache,
            _availabilityService);
        _previewService = new PreviewService(
            catalogService,
            gatedBaseLoader,
            renderPipeline,
            _histogramService,
            new PreviewCacheService(catalogService),
            renderedThumbnailCache);
        _exportService = new ImageExportService(
            renderPipeline,
            gatedBaseLoader,
            new ExportMetadataService(
                $"Happy Photon {AppBuildInfo.Version.ToString(3)}",
                _availabilityService));
        _metadataService = new MetadataService(
            _rawService,
            _availabilityService);
    }

    // ===== Preview Methods (delegated to PreviewService) =====

    public Task<(Bitmap? preview, HistogramData histogram)> LoadPreviewWithHistogramAsync(
        ImageFile imageFile, EditSettings settings, bool skipHistogram = false, CancellationToken cancellationToken = default) =>
        _previewService.LoadPreviewWithHistogramAsync(imageFile, settings, skipHistogram, cancellationToken);

    public Task<(Bitmap? preview, HistogramData histogram)> LoadPreviewWithHistogramAsync(
        ImageFile imageFile,
        EditSettings settings,
        ThumbnailSizeRequest thumbnailRequest,
        bool skipHistogram = false,
        CancellationToken cancellationToken = default) =>
        _previewService.LoadPreviewWithHistogramAsync(
            imageFile,
            settings,
            thumbnailRequest,
            skipHistogram,
            cancellationToken);

    public Task<CachedPreviewBitmap?> LoadCachedPreviewAsync(
        ImageFile imageFile,
        EditSettings settings,
        CancellationToken cancellationToken = default) =>
        _previewService.LoadCachedPreviewAsync(
            imageFile,
            settings,
            cancellationToken);

    public Task<(Bitmap? preview, HistogramData histogram)> ApplyEditsToPreviewAsync(
        ImageFile imageFile, EditSettings settings, bool skipHistogram = false, CancellationToken cancellationToken = default) =>
        _previewService.ApplyEditsToPreviewAsync(imageFile, settings, skipHistogram, cancellationToken);

    public Task<(Bitmap? preview, HistogramData histogram)> ApplyEditsToPreviewAsync(
        ImageFile imageFile,
        EditSettings settings,
        ThumbnailSizeRequest thumbnailRequest,
        bool skipHistogram = false,
        CancellationToken cancellationToken = default) =>
        _previewService.ApplyEditsToPreviewAsync(
            imageFile,
            settings,
            thumbnailRequest,
            skipHistogram,
            cancellationToken);

    public Task<WhiteBalanceBaseContext?> GetWhiteBalanceContextAsync(
        ImageFile imageFile,
        EditSettings settings,
        CancellationToken cancellationToken = default) =>
        _previewService.GetWhiteBalanceContextAsync(
            imageFile,
            settings,
            cancellationToken);

    public Task<double[]?> GetAutoWhiteBalanceAsync(
        ImageFile imageFile,
        EditSettings settings,
        CancellationToken cancellationToken = default) =>
        _previewService.GetAutoWhiteBalanceAsync(
            imageFile,
            settings,
            cancellationToken);

    public Task<double[]?> PickWhiteBalanceAsync(
        ImageFile imageFile,
        EditSettings settings,
        double normalizedX,
        double normalizedY,
        CancellationToken cancellationToken = default) =>
        _previewService.PickWhiteBalanceAsync(
            imageFile,
            settings,
            normalizedX,
            normalizedY,
            cancellationToken);

    public void ClearPreviewCache() =>
        _previewService.ClearPreviewCache();

    // ===== Thumbnail Methods (delegated to ThumbnailService) =====

    public Task<ThumbnailLoadResult> LoadThumbnailAsync(
        ImageFile imageFile,
        CancellationToken cancellationToken)
        => LoadThumbnailAsync(
            imageFile,
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Medium),
            cancellationToken);

    public Task<ThumbnailLoadResult> LoadThumbnailAsync(
        ImageFile imageFile,
        ThumbnailSizeRequest request,
        CancellationToken cancellationToken)
        => LoadThumbnailAsync(
            imageFile,
            request,
            allowUndersizedCachePlaceholder: true,
            cancellationToken);

    internal Task<ThumbnailLoadResult> LoadThumbnailAsync(
        ImageFile imageFile,
        ThumbnailSizeRequest request,
        bool allowUndersizedCachePlaceholder,
        CancellationToken cancellationToken)
    {
        var promoted = _previewService.TryPromoteRenderedThumbnail(
            imageFile,
            imageFile.EditSettings,
            request);
        return promoted != null
            ? Task.FromResult(ThumbnailLoadResult.Loaded(promoted, request))
            : _thumbnailService.LoadThumbnailAsync(
                imageFile,
                request,
                allowUndersizedCachePlaceholder,
                cancellationToken);
    }

    public Task<ThumbnailLoadResult> LoadUneditedThumbnailAsync(
        ImageFile imageFile,
        CancellationToken cancellationToken) =>
        _thumbnailService.LoadUneditedThumbnailAsync(imageFile, cancellationToken);

    public Task<ThumbnailLoadResult> LoadUneditedThumbnailAsync(
        ImageFile imageFile,
        ThumbnailSizeRequest request,
        CancellationToken cancellationToken) =>
        _thumbnailService.LoadUneditedThumbnailAsync(imageFile, request, cancellationToken);

    public bool IsThumbnailCacheValid(ImageFile imageFile) =>
        _thumbnailService.IsCacheValid(imageFile);

    public bool IsThumbnailCacheValid(
        ImageFile imageFile,
        ThumbnailSizeRequest request) =>
        _thumbnailService.IsCacheValid(imageFile, request);

    public bool HasRenderedThumbnailCacheEntry(ImageFile imageFile) =>
        _thumbnailService.HasRenderedCacheEntry(imageFile);

    public async ValueTask DisposeAsync()
    {
        var thumbnailDispose = _thumbnailService.DisposeAsync().AsTask();
        var previewDispose = _previewService.DisposeAsync().AsTask();
        await Task.WhenAll(thumbnailDispose, previewDispose);
    }

    // ===== Export Methods (delegated to ImageExportService) =====

    public async Task<int> ExportBatchAsync(
        IEnumerable<ImageFile> images,
        ExportSettings settings,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var imageList = images.ToList();
        await EnsureExportMetadataAsync(imageList, cancellationToken);
        return await _exportService.ExportBatchAsync(
            imageList,
            settings,
            progress,
            cancellationToken);
    }

    internal async Task<int> ExportBatchApprovedAsync(
        IEnumerable<ImageFile> images,
        ExportSettings settings,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var imageList = images.ToList();
        await EnsureExportMetadataAsync(imageList, cancellationToken);
        return await _exportService.ExportBatchApprovedAsync(
            imageList,
            settings,
            progress,
            cancellationToken);
    }

    public async Task<int> ExportBatchAsync(
        IEnumerable<ImageFile> images,
        ExportSettings settings,
        IReadOnlyList<ExportVariant> variants,
        bool useSubfolders,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var imageList = images.ToList();
        await EnsureExportMetadataAsync(imageList, cancellationToken);
        return await _exportService.ExportBatchAsync(
            imageList,
            settings,
            variants,
            useSubfolders,
            progress,
            cancellationToken);
    }

    private async Task EnsureExportMetadataAsync(
        IReadOnlyList<ImageFile> images,
        CancellationToken cancellationToken)
    {
        foreach (var image in images)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _metadataService.LoadAsync(image);
        }
    }

    internal ExportHydrationScope GetExportHydrationScope(
        IEnumerable<ImageFile> images)
    {
        var count = 0;
        long bytes = 0;
        foreach (var image in images)
        {
            if (GetSourceAvailability(image) !=
                SourceAvailability.RequiresHydration)
            {
                continue;
            }

            count++;
            try
            {
                bytes += new FileInfo(image.FilePath).Length;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return new ExportHydrationScope(count, bytes);
    }

    // ===== Histogram Methods (delegated to HistogramService) =====

    public HistogramData CalculateHistogram(Bitmap bitmap) =>
        _histogramService.CalculateHistogram(bitmap);

    public HistogramData CalculateLibraryHistogram(Bitmap bitmap) =>
        _histogramService.CalculateLibraryHistogram(bitmap);

    // ===== Metadata and Full Image Loading (kept in facade) =====

    public Task<MetadataLoadStatus> LoadMetadataAsync(ImageFile imageFile) =>
        _metadataService.LoadAsync(imageFile);

    internal bool CanRetryBackgroundRead(ImageFile imageFile) =>
        SourceAccessPolicy.CanRead(
            GetSourceAvailability(imageFile),
            SourceReadIntent.Background);

    internal SourceAvailability GetSourceAvailability(ImageFile imageFile) =>
        _availabilityService.GetAvailability(imageFile.FilePath);

    internal Task<bool> HydrateSourceAsync(
        ImageFile imageFile,
        CancellationToken cancellationToken) =>
        _sourceHydrationService.HydrateAsync(
            imageFile,
            cancellationToken);

}
