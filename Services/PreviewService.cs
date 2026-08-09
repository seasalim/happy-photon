using System.Diagnostics;
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
    private readonly bool _createRenderedThumbnail;
    private readonly object _renderedSync = new();
    private readonly object _refreshSync = new();
    private readonly Dictionary<Task, PendingRefresh> _pendingRefreshes =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Task> _renderedThumbnailTasks = new(
        ReferenceEqualityComparer.Instance);
    private RenderedPreview? _lastRendered;
    private long _renderGeneration;
    private long _baseRefreshGeneration;
    private int _disposed;

    public event EventHandler<PreviewRefresh>? PreviewRefreshed;
    public event EventHandler<PreviewBaseRefreshState>?
        BaseRefreshStateChanged;
    internal event Action? RenderStarted;
    internal event Action? PreviewConverted;
    internal event Action? RenderedThumbnailCreated;

    public PreviewService(
        CatalogService catalogService,
        IBaseImageLoader baseLoader,
        RenderPipeline renderPipeline,
        HistogramService histogramService) : this(
            catalogService,
            baseLoader,
            renderPipeline,
            histogramService,
            new PreviewCacheService(catalogService),
            new RenderedThumbnailCacheService(catalogService))
    {
    }

    internal PreviewService(
        CatalogService catalogService,
        IBaseImageLoader baseLoader,
        RenderPipeline renderPipeline,
        HistogramService histogramService,
        PreviewCacheService previewCache,
        RenderedThumbnailCacheService renderedThumbnailCache,
        bool createRenderedThumbnail = true)
    {
        _catalogService = catalogService;
        _previewCache = previewCache;
        _renderedThumbnailCache = renderedThumbnailCache;
        _baseCoordinator = new PreviewBaseCoordinator(baseLoader);
        _renderPipeline = renderPipeline;
        _histogramService = histogramService;
        _createRenderedThumbnail = createRenderedThumbnail;
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

    public Task<(Bitmap? preview, HistogramData histogram)>
        LoadPreviewWithHistogramAsync(
            ImageFile imageFile,
            EditSettings settings,
            bool skipHistogram = false,
            CancellationToken cancellationToken = default)
        => LoadPreviewWithHistogramAsync(
            imageFile,
            settings,
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Medium),
            skipHistogram,
            cancellationToken);

    public Task<(Bitmap? preview, HistogramData histogram)>
        LoadPreviewWithHistogramAsync(
            ImageFile imageFile,
            EditSettings settings,
            ThumbnailSizeRequest thumbnailRequest,
            bool skipHistogram = false,
            CancellationToken cancellationToken = default)
    {
        QueueRenderedPreviewIfLeaving(imageFile);
        return RenderAsync(
            imageFile,
            settings,
            thumbnailRequest,
            skipHistogram,
            cancellationToken);
    }

    public Task<(Bitmap? preview, HistogramData histogram)>
        ApplyEditsToPreviewAsync(
            ImageFile imageFile,
            EditSettings settings,
            bool skipHistogram = false,
            CancellationToken cancellationToken = default) =>
        ApplyEditsToPreviewAsync(
            imageFile,
            settings,
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Medium),
            skipHistogram,
            cancellationToken);

    public Task<(Bitmap? preview, HistogramData histogram)>
        ApplyEditsToPreviewAsync(
            ImageFile imageFile,
            EditSettings settings,
            ThumbnailSizeRequest thumbnailRequest,
            bool skipHistogram = false,
            CancellationToken cancellationToken = default) =>
        RenderAsync(
            imageFile,
            settings,
            thumbnailRequest,
            skipHistogram,
            cancellationToken);

    private async Task<(Bitmap? preview, HistogramData histogram)> RenderAsync(
        ImageFile imageFile,
        EditSettings settings,
        ThumbnailSizeRequest thumbnailRequest,
        bool skipHistogram,
        CancellationToken cancellationToken)
    {
        var settingsSnapshot = settings.Clone();
        await imageFile.EnsureCatalogIdAsync(_catalogService);
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        var generation = Interlocked.Increment(ref _renderGeneration);
        var decode = BaseDecodeSettings.From(settingsSnapshot);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var snapshot = await _baseCoordinator.GetPreviewAsync(
                imageFile,
                decode,
                cancellationToken);
            if (snapshot == null)
            {
                return (null, new HistogramData());
            }
            if (generation != Volatile.Read(ref _renderGeneration))
            {
                return (null, new HistogramData());
            }
            if (snapshot.IsStale)
            {
                QueueRefresh(
                    snapshot.RefreshTask!,
                    imageFile,
                    settingsSnapshot,
                    thumbnailRequest,
                    skipHistogram,
                    generation);
            }

            LogPerformance(
                nameof(RenderAsync),
                "Base",
                stopwatch.ElapsedMilliseconds,
                imageFile.FilePath,
                $"size={snapshot.Base.Pixels.Width}x{snapshot.Base.Pixels.Height}");
            stopwatch.Restart();

            RenderStarted?.Invoke();
            var rendered = await Task.Run(
                () => Render(
                    snapshot.Base,
                    settingsSnapshot,
                    thumbnailRequest,
                    skipHistogram,
                    generation,
                    cancellationToken),
                cancellationToken);
            if (generation != Volatile.Read(ref _renderGeneration) ||
                cancellationToken.IsCancellationRequested)
            {
                rendered.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
                return (null, new HistogramData());
            }

            if (rendered.Bitmap == null ||
                !TryRememberRendered(
                    imageFile,
                    rendered,
                    RenderSettingsHash.Compute(settingsSnapshot),
                    generation))
            {
                rendered.Dispose();
                return (null, new HistogramData());
            }
            LogPerformance(
                nameof(RenderAsync),
                $"RenderV{RenderPipeline.Version}",
                stopwatch.ElapsedMilliseconds,
                imageFile.FilePath);
            return (rendered.Bitmap, rendered.Histogram);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            HandleImageLoadError(ex, imageFile.FilePath);
            return (null, new HistogramData());
        }
    }

    private RenderOutput Render(
        BaseImage baseImage,
        EditSettings settings,
        ThumbnailSizeRequest thumbnailRequest,
        bool skipHistogram,
        long generation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var rendered = _renderPipeline.Render(new RenderRequest(
            baseImage,
            settings,
            RenderIntent.Preview,
            BaseImage.PreviewMaxDimension,
            new RenderOptions(
                ComputeStats: !skipHistogram,
                ComputeOverlayMasks: false)));
        cancellationToken.ThrowIfCancellationRequested();

        var histogram = new HistogramData();
        if (!skipHistogram)
        {
            _histogramService.CalculateHistogram(rendered, histogram);
        }
        cancellationToken.ThrowIfCancellationRequested();

        Bitmap? preview = null;
        MagickImage? thumbnailSource = null;
        try
        {
            preview = ConvertToBitmap(rendered.Image);
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
                histogram);
        }
        catch
        {
            preview?.Dispose();
            thumbnailSource?.Dispose();
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
                CreateRenderedThumbnailAsync(
                    thumbnailSource,
                    output.ThumbnailDimension));
        }
        DisposeRenderedThumbnailWhenReady(previous);
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

        if (rendered.Bitmap.TryGetTarget(out var bitmap))
        {
            _previewCache.QueueSaveToCache(
                rendered.ImageFile,
                bitmap,
                rendered.SettingsHash);
        }
        QueueRenderedThumbnailWhenReady(rendered);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        ClearPreviewCache();
        await WaitForRenderedThumbnailTasksAsync();
        await _baseCoordinator.DisposeAsync();
        await _previewCache.DisposeAsync();
        await _renderedThumbnailCache.DisposeAsync();
    }

    private sealed class RenderOutput : IDisposable
    {
        public Bitmap? Bitmap { get; }
        public MagickImage? ThumbnailSource { get; private set; }
        public int ThumbnailDimension { get; }
        public HistogramData Histogram { get; }

        public RenderOutput(
            Bitmap? bitmap,
            MagickImage? thumbnailSource,
            int thumbnailDimension,
            HistogramData histogram)
        {
            Bitmap = bitmap;
            ThumbnailSource = thumbnailSource;
            ThumbnailDimension = thumbnailDimension;
            Histogram = histogram;
        }

        public MagickImage? DetachThumbnailSource()
        {
            var source = ThumbnailSource;
            ThumbnailSource = null;
            return source;
        }

        public void Dispose()
        {
            Bitmap?.Dispose();
            ThumbnailSource?.Dispose();
            ThumbnailSource = null;
        }
    }

    private sealed record RenderedPreview(
        ImageFile ImageFile,
        WeakReference<Bitmap> Bitmap,
        string SettingsHash,
        Task<Bitmap?>? ThumbnailTask);

    private sealed record PendingRefresh(
        ImageFile ImageFile,
        EditSettings Settings,
        ThumbnailSizeRequest ThumbnailRequest,
        bool SkipHistogram,
        long Generation);
}
