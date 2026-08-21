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
    private readonly RenderPipeline _renderPipeline;
    private readonly HistogramService _histogramService;
    private readonly DcpProfileService _dcpProfiles;
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
    private long _baseRefreshGeneration;
    private long _restingSerial;
    private int _activeRefreshRenders;
    private int _activeRestingRenders;
    private int _disposed;

    public event EventHandler<PreviewRefresh>? PreviewRefreshed;
    public event EventHandler<PreviewBaseRefreshState>?
        BaseRefreshStateChanged;
    public event Action? RenderedThumbnailWorkStarted;
    internal event Action? RenderStarted;
    internal event Action? PreviewConverted;
    internal event Action? RenderedThumbnailCreated;
    internal Func<Task>? RenderedThumbnailCacheQueuedAsync { get; set; }
    internal Func<Task>? RenderGateAsync { get; set; }
    internal Func<Task>? RefreshRenderGateAsync { get; set; }
    internal Func<Task>? RefreshReadyGateAsync { get; set; }
    internal Action<string>? RestingStageStarted { get; set; }

    public int PreviewActivityCount
    {
        get
        {
            int pending;
            lock (_refreshSync) pending = _pendingRefreshes.Count;
            return _baseCoordinator.DecodeTaskCount +
                pending + Volatile.Read(ref _activeRefreshRenders) +
                Volatile.Read(ref _activeRestingRenders);
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

    internal PreviewService(
        CatalogService catalogService,
        IBaseImageLoader baseLoader,
        RenderPipeline renderPipeline,
        HistogramService histogramService,
        PreviewCacheService? previewCache = null,
        RenderedThumbnailCacheService? renderedThumbnailCache = null,
        bool createRenderedThumbnail = true,
        DcpProfileService? dcpProfiles = null)
    {
        _catalogService = catalogService;
        _previewCache = previewCache ?? new PreviewCacheService(catalogService);
        _renderedThumbnailCache = renderedThumbnailCache ??
            new RenderedThumbnailCacheService(catalogService);
        _baseCoordinator = new PreviewBaseCoordinator(baseLoader);
        _renderPipeline = renderPipeline;
        _histogramService = histogramService;
        _createRenderedThumbnail = createRenderedThumbnail;
        _dcpProfiles = dcpProfiles ?? new DcpProfileService(
            new SourceAvailabilityService());
    }

    public async Task<CachedPreviewBitmap?> LoadCachedPreviewAsync(
        ImageFile imageFile,
        EditSettings settings,
        CancellationToken cancellationToken = default)
    {
        var settingsSnapshot = settings.Clone();
        await imageFile.EnsureCatalogIdAsync(_catalogService);
        return await Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var cached = _previewCache.LoadRenderedPreview(imageFile);
                if (cached == null)
                {
                    return null;
                }

                var bitmap = ConvertToBitmap(cached.Image);
                return bitmap == null
                    ? null
                    : new CachedPreviewBitmap(
                        bitmap,
                        string.Equals(
                            cached.SettingsHash,
                            RenderSettingsHash.Compute(settingsSnapshot),
                            StringComparison.Ordinal));
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
        EditSettings settings,
        ThumbnailSizeRequest thumbnailRequest,
        bool skipHistogram,
        ClippingOverlaySide overlaySides,
        long generation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var effectiveOverlaySides = baseImage.Info.IsRawSource
            ? overlaySides
            : overlaySides & ClippingOverlaySide.DisplayFloor;
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
                OverlaySides: effectiveOverlaySides)));
        cancellationToken.ThrowIfCancellationRequested();

        var histogram = new HistogramData();
        if (!skipHistogram)
        {
            _histogramService.CalculateHistogram(rendered, histogram);
        }
        cancellationToken.ThrowIfCancellationRequested();

        Bitmap? preview = null;
        MagickImage? thumbnailSource = null;
        ClippingMask? clippingMask = null;
        try
        {
            preview = ConvertToBitmap(rendered.Image);
            clippingMask = rendered.DetachOverlayMask();
            PreviewConverted?.Invoke();
            if (_createRenderedThumbnail &&
                baseImage.Info.IsRawSource &&
                settings.HasEdits &&
                generation == Volatile.Read(ref _renderGeneration))
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
                DcpProfileState.From(baseImage.Info),
                clippingMask);
        }
        catch
        {
            preview?.Dispose();
            thumbnailSource?.Dispose();
            clippingMask?.Dispose();
            throw;
        }
    }

    private bool TryRememberRendered(
        ImageFile imageFile,
        RenderOutput output,
        string settingsHash,
        long generation)
    {
        RenderedPreview? previous;
        lock (_renderedSync)
        {
            if (Volatile.Read(ref _disposed) != 0 ||
                generation != Volatile.Read(ref _renderGeneration))
            {
                return false;
            }
            var thumbnailSource = output.DetachThumbnailSource();
            previous = _lastRendered;
            _lastRendered = new RenderedPreview(
                imageFile,
                new WeakReference<Bitmap>(output.Bitmap!),
                settingsHash,
                generation,
                CreateRenderedThumbnailAsync(
                    thumbnailSource,
                    output.ThumbnailDimension));
        }
        DisposeRenderedPreviewWhenReady(previous);
        return true;
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

    public void ClearPreviewCache()
    {
        Interlocked.Increment(ref _renderGeneration);
        Interlocked.Increment(ref _restingSerial);
        RenderedPreview? rendered;
        lock (_renderedSync)
        {
            rendered = _lastRendered;
            _lastRendered = null;
        }
        Queue(rendered);
        _baseCoordinator.Clear();
    }

    private void Queue(RenderedPreview? rendered)
    {
        if (rendered == null)
        {
            return;
        }

        var bitmap = rendered.DetachStrongBitmap();
        var ownsBitmap = bitmap != null;
        if (bitmap != null || rendered.Bitmap.TryGetTarget(out bitmap))
        {
            try
            {
                _previewCache.QueueSaveToCache(
                    rendered.ImageFile,
                    bitmap,
                    rendered.SettingsHash);
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
            ClippingMask? clippingMask)
        {
            Bitmap = bitmap;
            ThumbnailSource = thumbnailSource;
            ThumbnailDimension = thumbnailDimension;
            Histogram = histogram;
            Clipping = clipping;
            IsRawSource = isRawSource;
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

        public PreviewArtifacts DetachArtifacts(long generation)
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
                mask);
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
