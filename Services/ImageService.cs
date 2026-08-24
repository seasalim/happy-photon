using Avalonia.Media.Imaging;
using HappyPhoton.LibRaw.Interop;
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
    private readonly DcpProfileService _dcpProfiles;
    private readonly DcpProfileDiscovery _dcpDiscovery;

    public int CacheWriteActivityCount =>
        _thumbnailService.PendingCacheWrites + _previewService.PendingCacheWrites;

    public PreviewService Previews => _previewService;

    public ThumbnailService Thumbnails => _thumbnailService;

    public HistogramService Histograms => _histogramService;

    internal MetadataService Metadata => _metadataService;

    internal DcpProfileDiscovery DcpDiscovery => _dcpDiscovery;

    internal SourceHydrationService SourceHydration => _sourceHydrationService;

    public ImageService(CatalogService catalogService)
        : this(catalogService, LibRawNativeSupport.Health)
    {
    }

    private ImageService(
        CatalogService catalogService,
        LibRawRuntimeHealth rawRuntimeHealth)
        : this(
            catalogService,
            new BaseLoaderRouter(
                new RawBaseLoader(rawRuntimeHealth),
                new StandardBaseLoader()),
            new SourceAvailabilityService(),
            rawRuntimeHealth)
    {
    }

    internal ImageService(
        CatalogService catalogService,
        IBaseImageLoader baseLoader) : this(
            catalogService,
            baseLoader,
            new SourceAvailabilityService(),
            LibRawNativeSupport.Health)
    {
    }

    internal ImageService(
        CatalogService catalogService,
        IBaseImageLoader baseLoader,
        ISourceAvailabilityService availabilityService) : this(
            catalogService,
            baseLoader,
            availabilityService,
            LibRawNativeSupport.Health)
    {
    }

    internal ImageService(
        CatalogService catalogService,
        IBaseImageLoader baseLoader,
        ISourceAvailabilityService availabilityService,
        LibRawRuntimeHealth rawRuntimeHealth)
    {
        _catalogService = catalogService;
        ArgumentNullException.ThrowIfNull(baseLoader);
        _availabilityService = availabilityService ??
            throw new ArgumentNullException(nameof(availabilityService));
        _sourceHydrationService = new SourceHydrationService(
            _availabilityService);
        _dcpProfiles = new DcpProfileService(_availabilityService);
        _dcpDiscovery = new DcpProfileDiscovery(_availabilityService);
        var gatedBaseLoader = new GatedBaseImageLoader(
            baseLoader,
            _availabilityService);

        // RAW pixels and embedded previews share the audited native runtime.
        _rawService = new LibRawProcessingService(rawRuntimeHealth);

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
            new PreviewCacheService(catalogService),
            renderedThumbnailCache,
            dcpProfiles: _dcpProfiles,
            sourceAvailability: _availabilityService);
        _exportService = new ImageExportService(
            renderPipeline,
            gatedBaseLoader,
            new ExportMetadataService(
                $"Happy Photon {AppBuildInfo.Version.ToString(3)}",
                _availabilityService),
            _dcpProfiles);
        _metadataService = new MetadataService(
            _rawService,
            _availabilityService);
    }

    // ===== Thumbnail Methods (delegated to ThumbnailService) =====

    public Task<ThumbnailLoadResult> LoadThumbnailAsync(
        ImageFile imageFile,
        CancellationToken cancellationToken)
        => LoadThumbnailAsync(
            imageFile,
            ThumbnailSizeRequest.For(BrowseThumbnailSize.Medium),
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

    public async ValueTask DisposeAsync()
    {
        var thumbnailDispose = _thumbnailService.DisposeAsync().AsTask();
        var previewDispose = _previewService.DisposeAsync().AsTask();
        await Task.WhenAll(thumbnailDispose, previewDispose);
    }

    // ===== Export Methods (delegated to ImageExportService) =====

    public async Task<ExportBatchResult> ExportBatchAsync(
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

    internal async Task<ExportBatchResult> ExportBatchApprovedAsync(
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
        CancellationToken cancellationToken = default,
        IProgress<ExportWarning>? warningProgress = null)
    {
        var imageList = images.ToList();
        await EnsureExportMetadataAsync(imageList, cancellationToken);
        return await _exportService.ExportBatchAsync(
            imageList,
            settings,
            variants,
            useSubfolders,
            progress,
            cancellationToken,
            warningProgress);
    }

    internal async Task<ExportBatchResult> ExportBatchVariantsAsync(
        IEnumerable<ImageFile> images,
        ExportSettings settings,
        IReadOnlyList<ExportVariant> variants,
        bool useSubfolders,
        CancellationToken cancellationToken)
    {
        var imageList = images.ToList();
        await EnsureExportMetadataAsync(imageList, cancellationToken);
        return await _exportService.ExportBatchVariantsAsync(
            imageList,
            settings,
            variants,
            useSubfolders,
            cancellationToken);
    }

    internal void InvalidateRawProfiles()
    {
        _dcpProfiles.Invalidate();
        _dcpDiscovery.Invalidate();
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
            if (!GetSourceAvailability(image).IsOnlineOnly())
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

    internal bool CanRetryBackgroundRead(ImageFile imageFile) =>
        SourceAccessPolicy.CanRead(
            GetSourceAvailability(imageFile),
            SourceReadIntent.Background);

    internal SourceAvailability GetSourceAvailability(ImageFile imageFile) =>
        _availabilityService.GetAvailability(imageFile.FilePath);
}
