using Avalonia.Media.Imaging;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

/// <summary>
/// Facade service for image operations. Coordinates sub-services for loading,
/// processing, and exporting images.
/// </summary>
public class ImageService : IAsyncDisposable
{
    private readonly ICatalogService _catalogService;
    private readonly IRawProcessingService _rawService;
    private readonly EditApplicationService _editService;
    private readonly HistogramService _histogramService;
    private readonly ThumbnailService _thumbnailService;
    private readonly PreviewService _previewService;
    private readonly ImageExportService _exportService;
    private readonly MetadataService _metadataService;

    public ImageService(ICatalogService catalogService)
    {
        _catalogService = catalogService;

        // Initialize RAW processing service
        var libRawService = new LibRawProcessingService();
        _rawService = libRawService.IsAvailable ? libRawService : new MagickNetRawService();

        // Initialize sub-services
        _editService = new EditApplicationService();
        _histogramService = new HistogramService();
        _thumbnailService = new ThumbnailService(catalogService, _rawService, _editService);
        _previewService = new PreviewService(catalogService, _rawService, _editService, _histogramService);
        _exportService = new ImageExportService(_editService, _rawService);
        _metadataService = new MetadataService(_rawService);
    }

    // ===== Preview Methods (delegated to PreviewService) =====

    public Task<(Bitmap? preview, HistogramData histogram)> LoadPreviewWithHistogramAsync(
        ImageFile imageFile, EditSettings settings, bool skipHistogram = false, CancellationToken cancellationToken = default) =>
        _previewService.LoadPreviewWithHistogramAsync(imageFile, settings, skipHistogram, cancellationToken);

    public Task<(Bitmap? preview, HistogramData histogram)> ApplyEditsToPreviewAsync(
        ImageFile imageFile, EditSettings settings, bool skipHistogram = false, CancellationToken cancellationToken = default) =>
        _previewService.ApplyEditsToPreviewAsync(imageFile, settings, skipHistogram, cancellationToken);

    public void ClearPreviewCache() =>
        _previewService.ClearPreviewCache();

    // ===== Thumbnail Methods (delegated to ThumbnailService) =====

    public Task<Bitmap?> LoadThumbnailAsync(ImageFile imageFile, CancellationToken cancellationToken) =>
        _thumbnailService.LoadThumbnailAsync(imageFile, cancellationToken);

    public Task<Bitmap?> LoadUneditedThumbnailAsync(
        ImageFile imageFile,
        CancellationToken cancellationToken) =>
        _thumbnailService.LoadUneditedThumbnailAsync(imageFile, cancellationToken);

    public bool IsThumbnailCacheValid(ImageFile imageFile) =>
        _thumbnailService.IsCacheValid(imageFile);

    public async ValueTask DisposeAsync()
    {
        var thumbnailDispose = _thumbnailService.DisposeAsync().AsTask();
        var previewDispose = _previewService.DisposeAsync().AsTask();
        await Task.WhenAll(thumbnailDispose, previewDispose);
    }

    // ===== Export Methods (delegated to ImageExportService) =====

    public Task<int> ExportBatchAsync(
        IEnumerable<ImageFile> images,
        ExportSettings settings,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default) =>
        _exportService.ExportBatchAsync(images, settings, progress, cancellationToken);

    public Task<int> ExportBatchAsync(
        IEnumerable<ImageFile> images,
        ExportSettings settings,
        IReadOnlyList<ExportVariant> variants,
        bool useSubfolders,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default) =>
        _exportService.ExportBatchAsync(
            images, settings, variants, useSubfolders, progress, cancellationToken);

    // ===== Histogram Methods (delegated to HistogramService) =====

    public HistogramData CalculateHistogram(Bitmap bitmap) =>
        _histogramService.CalculateHistogram(bitmap);

    // ===== Metadata and Full Image Loading (kept in facade) =====

    public Task LoadMetadataAsync(ImageFile imageFile) =>
        _metadataService.LoadAsync(imageFile);

}
