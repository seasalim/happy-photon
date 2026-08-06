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
        IBaseImageLoader baseLoader)
    {
        _catalogService = catalogService;
        ArgumentNullException.ThrowIfNull(baseLoader);

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
            renderedThumbnailCache);
        _previewService = new PreviewService(
            catalogService,
            baseLoader,
            renderPipeline,
            _histogramService,
            new PreviewCacheService(catalogService),
            renderedThumbnailCache);
        _exportService = new ImageExportService(
            renderPipeline,
            baseLoader,
            new ExportMetadataService());
        _metadataService = new MetadataService(_rawService);
    }

    // ===== Preview Methods (delegated to PreviewService) =====

    public Task<(Bitmap? preview, HistogramData histogram)> LoadPreviewWithHistogramAsync(
        ImageFile imageFile, EditSettings settings, bool skipHistogram = false, CancellationToken cancellationToken = default) =>
        _previewService.LoadPreviewWithHistogramAsync(imageFile, settings, skipHistogram, cancellationToken);

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

    public Task<Bitmap?> LoadThumbnailAsync(
        ImageFile imageFile,
        CancellationToken cancellationToken)
    {
        var promoted = _previewService.TryPromoteRenderedThumbnail(
            imageFile,
            imageFile.EditSettings);
        return promoted != null
            ? Task.FromResult<Bitmap?>(promoted)
            : _thumbnailService.LoadThumbnailAsync(imageFile, cancellationToken);
    }

    public Task<Bitmap?> LoadUneditedThumbnailAsync(
        ImageFile imageFile,
        CancellationToken cancellationToken) =>
        _thumbnailService.LoadUneditedThumbnailAsync(imageFile, cancellationToken);

    public bool IsThumbnailCacheValid(ImageFile imageFile) =>
        _thumbnailService.IsCacheValid(imageFile);

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

    // ===== Histogram Methods (delegated to HistogramService) =====

    public HistogramData CalculateHistogram(Bitmap bitmap) =>
        _histogramService.CalculateHistogram(bitmap);

    // ===== Metadata and Full Image Loading (kept in facade) =====

    public Task LoadMetadataAsync(ImageFile imageFile) =>
        _metadataService.LoadAsync(imageFile);

}
