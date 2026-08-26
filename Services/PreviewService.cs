using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using ImageMagick;
using static HappyPhoton.Services.BitmapConversionService;
using static HappyPhoton.Services.ImageServiceHelpers;

namespace HappyPhoton.Services;

public sealed partial class PreviewService : IAsyncDisposable
{
    private readonly CatalogService _catalogService;
    private readonly PreviewCacheService _previewCache;
    private readonly RenderedThumbnailCacheService _renderedThumbnailCache;
    private readonly PreviewBaseCoordinator _baseCoordinator;
    private readonly IBaseImageLoader _baseLoader;
    private readonly RenderPipeline _renderPipeline;
    private readonly DcpProfileService _dcpProfiles;
    private readonly ISourceAvailabilityService _sourceAvailability;
    private readonly bool _createRenderedThumbnail;
    private readonly object _renderedSync = new();
    private readonly object _refreshSync = new();
    private readonly Dictionary<Task, PendingRefresh> _pendingRefreshes =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Task> _renderedThumbnailTasks = new(
        ReferenceEqualityComparer.Instance);
    private readonly ConditionalWeakTable<Bitmap, PreviewRenderIdentity>
        _previewIdentities = new();
    private RenderedPreview? _lastRendered;
    private long _renderGeneration;
    private long _latestSurfaceGeneration;
    private long _baseRefreshGeneration;
    private long _restingSerial;
    private int _activeRefreshRenders;
    private int _activeRestingRenders;
    private int _disposed;

    public event EventHandler<PreviewRefresh>? PreviewRefreshed;
    public event EventHandler<PreviewBaseRefreshState>?
        BaseRefreshStateChanged;
    public event Action? RenderedThumbnailWorkStarted;
    internal event Action? AdjacentWarmWorkStarted;
    internal event Action? RenderStarted;
    internal event Action? PreviewConverted;
    internal event Action? RenderedThumbnailCreated;
    internal event Action<long>? RenderRequestCompleted;
    internal Func<Task>? RenderedThumbnailCacheQueuedAsync { get; set; }
    internal Func<Task>? RenderGateAsync { get; set; }
    internal Func<Task>? RefreshRenderGateAsync { get; set; }
    internal Func<Task>? RefreshReadyGateAsync { get; set; }
    internal Func<Task>? WhiteBalanceSampleGateAsync { get; set; }
    internal Func<Task>? CachedPreviewGateAsync { get; set; }
    internal Func<Task>? SourceWorkGateAsync { get; set; }
    internal Action<string>? RestingStageStarted { get; set; }

    public int PreviewActivityCount
    {
        get
        {
            int pending;
            lock (_refreshSync) pending = _pendingRefreshes.Count;
            return _baseCoordinator.DecodeTaskCount +
                pending + Volatile.Read(ref _activeRefreshRenders) +
                Volatile.Read(ref _activeRestingRenders) +
                Volatile.Read(ref _activeAdjacentWarmWorkers);
        }
    }

    public int RenderedThumbnailTaskCount
    {
        get
        {
            lock (_renderedSync) return _renderedThumbnailTasks.Count;
        }
    }

    public int PendingCacheWrites =>
        _previewCache.PendingWrites + _renderedThumbnailCache.PendingWrites;

    internal int RetainedBasePairCount => _baseCoordinator.RetainedPairCount;

    internal PreviewService(
        CatalogService catalogService,
        IBaseImageLoader baseLoader,
        RenderPipeline renderPipeline,
        PreviewCacheService? previewCache = null,
        RenderedThumbnailCacheService? renderedThumbnailCache = null,
        bool createRenderedThumbnail = true,
        DcpProfileService? dcpProfiles = null,
        ISourceAvailabilityService? sourceAvailability = null)
    {
        _catalogService = catalogService;
        _previewCache = previewCache ?? new PreviewCacheService(catalogService);
        _renderedThumbnailCache = renderedThumbnailCache ??
            new RenderedThumbnailCacheService(catalogService);
        _baseLoader = baseLoader;
        _baseCoordinator = new PreviewBaseCoordinator(baseLoader);
        _renderPipeline = renderPipeline;
        _createRenderedThumbnail = createRenderedThumbnail;
        _sourceAvailability = sourceAvailability ??
            new SourceAvailabilityService();
        _dcpProfiles = dcpProfiles ?? new DcpProfileService(
            _sourceAvailability);
    }

    public async Task<CachedPreviewBitmap?> LoadCachedPreviewAsync(
        ImageFile imageFile,
        EditSettings settings,
        CancellationToken cancellationToken = default)
    {
        var settingsSnapshot = settings.Clone();
        await imageFile.EnsureCatalogIdAsync(_catalogService);
        if (CachedPreviewGateAsync is { } gate)
        {
            await gate().ConfigureAwait(false);
        }
        return await Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var expectedHash = RenderSettingsHash.Compute(settingsSnapshot);
                using var cached = TryLoadAdjacentWarm(
                        imageFile,
                        expectedHash) ??
                    _previewCache.LoadRenderedPreview(imageFile);
                if (cached == null)
                {
                    return null;
                }

                var settingsMatch = string.Equals(
                    cached.SettingsHash,
                    expectedHash,
                    StringComparison.Ordinal);
                var width = checked((int)cached.Image.Width);
                var height = checked((int)cached.Image.Height);
                var pixels = CopyBgraPixels(cached.Image);
                HistogramData? histogram = null;
                ClippingStats? clipping = null;
                if (settingsMatch)
                {
                    histogram = new HistogramData();
                    HistogramService.CalculatePreviewHistogram(
                        pixels,
                        width,
                        height,
                        histogram,
                        includeWaveform: true);
                    clipping = PreviewCacheService.CalculateDisplayFloorClipping(
                        pixels,
                        width,
                        height);
                }
                return new CachedPreviewBitmap(
                    ConvertToBitmap(pixels, width, height),
                    settingsMatch,
                    histogram,
                    clipping,
                    cached.OriginalViewPixelSize,
                    cached.OriginalImagePixelSize);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        },
            cancellationToken);
    }

    private RenderOutput Render(
        BaseImage baseImage,
        SourceSaturationMask? sourceSaturation,
        EditSettings settings,
        ThumbnailSizeRequest thumbnailRequest,
        bool skipHistogram,
        bool computeWaveform,
        ClippingOverlaySide overlaySides,
        long generation,
        CancellationToken cancellationToken,
        bool surfaceAuthorized)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var effectiveOverlaySides = overlaySides;
        using var rendered = _renderPipeline.Render(new RenderRequest(
            baseImage,
            settings,
            RenderIntent.Preview,
            BaseImage.InteractivePreviewMaxDimension,
            new RenderOptions(
                ComputeStats: !skipHistogram ||
                    effectiveOverlaySides != ClippingOverlaySide.None,
                ComputeOverlayMasks:
                    effectiveOverlaySides != ClippingOverlaySide.None,
                OverlaySides: effectiveOverlaySides,
                ComputeHistogram: !skipHistogram,
                ComputeWaveform: !skipHistogram && computeWaveform,
                PreparePreviewPixels: true))
        {
            SourceSaturation = sourceSaturation
        });
        cancellationToken.ThrowIfCancellationRequested();

        var histogram = rendered.Histogram ?? new HistogramData();
        Bitmap? preview = null;
        MagickImage? thumbnailSource = null;
        ClippingMask? clippingMask = null;
        try
        {
            preview = rendered.PreviewPixels == null
                ? ConvertToBitmap(rendered.Image)
                : BitmapConversionService.ConvertToBitmap(
                    rendered.PreviewPixels,
                    checked((int)rendered.Image.Width),
                    checked((int)rendered.Image.Height));
            cancellationToken.ThrowIfCancellationRequested();
            clippingMask = rendered.DetachOverlayMask();
            PreviewConverted?.Invoke();
            if (_createRenderedThumbnail &&
                baseImage.Info.IsRawSource &&
                settings.HasEdits &&
                (surfaceAuthorized ||
                 generation == Volatile.Read(ref _renderGeneration)))
            {
                thumbnailSource = rendered.DetachImage();
            }
            return new RenderOutput(
                preview,
                thumbnailSource,
                Math.Min(512, thumbnailRequest.GenerationDimension),
                histogram,
                !skipHistogram ||
                    effectiveOverlaySides != ClippingOverlaySide.None
                    ? rendered.Clipping
                    : null,
                baseImage.Info.IsRawSource,
                DcpProfileState.From(baseImage.Info, settings.RawProfile),
                clippingMask,
                baseImage.Info.IsMonochrome);
        }
        catch
        {
            preview?.Dispose();
            thumbnailSource?.Dispose();
            clippingMask?.Dispose();
            throw;
        }
    }

    private void CommitRenderedPreview(
        ImageFile imageFile,
        Bitmap bitmap,
        ImageMagick.MagickImage? thumbnailSource,
        int thumbnailDimension,
        string settingsHash,
        long generation,
        bool surfaceAuthorized)
    {
        _previewIdentities.TryGetValue(bitmap, out var identity);
        RenderedPreview? previous;
        lock (_renderedSync)
        {
            if (Volatile.Read(ref _disposed) != 0 ||
                !surfaceAuthorized &&
                generation != Volatile.Read(ref _renderGeneration))
            {
                thumbnailSource?.Dispose();
                return;
            }
            previous = _lastRendered;
            _lastRendered = new RenderedPreview(
                imageFile,
                new WeakReference<Bitmap>(bitmap),
                settingsHash,
                generation,
                identity,
                CreateRenderedThumbnailAsync(
                    thumbnailSource,
                    thumbnailDimension));
        }
        DisposeRenderedPreviewWhenReady(previous);
    }

    private PreviewPromotionLease CreatePromotionLease(
        ImageFile imageFile,
        RenderOutput output,
        string settingsHash,
        long generation,
        bool surfaceAuthorized)
    {
        var thumbnailSource = output.DetachThumbnailSource();
        return new PreviewPromotionLease(
            thumbnailSource,
            (bitmap, source) => CommitRenderedPreview(
                imageFile,
                bitmap,
                source,
                output.ThumbnailDimension,
                settingsHash,
                generation,
                surfaceAuthorized));
    }

    private void QueueRenderedPreviewIfLeaving(ImageFile nextImage)
    {
        RenderedPreview? leaving = null;
        lock (_renderedSync)
        {
            if (_lastRendered != null &&
                !PathsEqual(_lastRendered.ImageFile.FilePath, nextImage.FilePath))
            {
                leaving = _lastRendered;
                _lastRendered = null;
            }
        }
        Queue(leaving);
    }

    public void FlushRenderedPreviewCache() =>
        ClearPreviewState(queueRendered: true, retireBase: false);

    public void InvalidatePreviewBase() =>
        ClearPreviewState(queueRendered: false, retireBase: true);

    public void ClearPreviewCache() =>
        ClearPreviewState(queueRendered: true, retireBase: true);

    private void ClearPreviewState(bool queueRendered, bool retireBase)
    {
        Interlocked.Increment(ref _renderGeneration);
        Interlocked.Increment(ref _restingSerial);
        RenderedPreview? rendered = null;
        if (queueRendered)
        {
            lock (_renderedSync)
            {
                rendered = _lastRendered;
                _lastRendered = null;
            }
        }
        Queue(rendered);
        if (retireBase)
        {
            _baseCoordinator.Clear();
        }
    }

    private void Queue(RenderedPreview? rendered)
    {
        if (rendered == null)
        {
            return;
        }

        var bitmap = rendered.DetachStrongBitmap();
        var ownsBitmap = bitmap != null;
        if ((bitmap != null || rendered.Bitmap.TryGetTarget(out bitmap)) &&
            rendered.Identity != null)
        {
            try
            {
                _previewCache.QueueSaveToCache(
                    rendered.ImageFile,
                    bitmap,
                    rendered.SettingsHash,
                    rendered.Identity.CacheIdentity);
            }
            finally
            {
                if (ownsBitmap)
                {
                    bitmap.Dispose();
                }
            }
        }
        QueueRenderedThumbnailWhenReady(rendered);
    }

    public async ValueTask DisposeAsync()
    {
        if (!TryBeginDispose())
        {
            return;
        }

        ClearPreviewCache();
        await DisposeAdjacentWarmAsync();
        await WaitForRestingRenderTasksAsync();
        await WaitForRenderedThumbnailTasksAsync();
        await _baseCoordinator.DisposeAsync();
        await _previewCache.DisposeAsync();
        await _renderedThumbnailCache.DisposeAsync();
    }

    private sealed class RenderOutput : IDisposable
    {
        public Bitmap? Bitmap { get; private set; }
        public MagickImage? ThumbnailSource { get; private set; }
        public int ThumbnailDimension { get; }
        public HistogramData Histogram { get; }
        public ClippingStats? Clipping { get; }
        public bool IsRawSource { get; }
        public bool IsMonochrome { get; }
        public DcpProfileState ProfileState { get; }
        public ClippingMask? ClippingMask { get; private set; }

        public RenderOutput(
            Bitmap? bitmap,
            MagickImage? thumbnailSource,
            int thumbnailDimension,
            HistogramData histogram,
            ClippingStats? clipping,
            bool isRawSource,
            DcpProfileState profileState,
            ClippingMask? clippingMask,
            bool isMonochrome)
        {
            Bitmap = bitmap;
            ThumbnailSource = thumbnailSource;
            ThumbnailDimension = thumbnailDimension;
            Histogram = histogram;
            Clipping = clipping;
            IsRawSource = isRawSource;
            IsMonochrome = isMonochrome;
            ProfileState = profileState;
            ClippingMask = clippingMask;
        }

        public MagickImage? DetachThumbnailSource()
        {
            var source = ThumbnailSource;
            ThumbnailSource = null;
            return source;
        }

        public ClippingMask? DetachClippingMask()
        {
            var mask = ClippingMask;
            ClippingMask = null;
            return mask;
        }

        public Bitmap? DetachBitmap()
        {
            var bitmap = Bitmap;
            Bitmap = null;
            return bitmap;
        }

        public PreviewArtifacts DetachArtifacts(
            long generation,
            BaseImageInfo info,
            PreviewSourceAnalysis analysis,
            bool isBaseStale,
            PreviewPromotionLease? promotionLease)
        {
            var mask = ClippingMask;
            ClippingMask = null;
            return new PreviewArtifacts(
                DetachBitmap(),
                Histogram,
                Clipping,
                IsRawSource,
                ProfileState,
                generation,
                mask,
                analysis.RawHistogram,
                info.AsShotKelvin,
                info.AsShotTint,
                isBaseStale,
                promotionLease,
                IsMonochrome,
                info.LensPrescriptionSummary);
        }

        public void Dispose()
        {
            Bitmap?.Dispose();
            ThumbnailSource?.Dispose();
            ClippingMask?.Dispose();
            ThumbnailSource = null;
            ClippingMask = null;
            Bitmap = null;
        }
    }

}
