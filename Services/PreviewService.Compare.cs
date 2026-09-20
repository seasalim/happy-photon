using Avalonia;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using static HappyPhoton.Services.BitmapConversionService;

namespace HappyPhoton.Services;

public sealed partial class PreviewService
{
    public async Task<ComparePreviewResult?> LoadComparePreviewAsync(
        ImageFile imageFile,
        EditSettings settings,
        int maxDimension = BaseImage.InteractivePreviewMaxDimension,
        CancellationToken cancellationToken = default,
        bool joinAdjacentWarm = false)
    {
        ArgumentNullException.ThrowIfNull(imageFile);
        ArgumentNullException.ThrowIfNull(settings);
        if (maxDimension <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDimension));
        }

        if (!SourceAccessPolicy.CanRead(
                _sourceAvailability.GetAvailability(imageFile.FilePath),
                SourceReadIntent.Background))
        {
            return null;
        }

        var snapshot = settings.Clone();
        if (joinAdjacentWarm)
        {
            // A pane's first paint only: a warm already decoding this image
            // for these settings is the fresh render, so join it rather than
            // decode the same thing twice. The cached read never waits, so a
            // stale preview paints first. Refinements never join; they need a
            // render at their own size.
            var warmed = await JoinAdjacentWarmAsync(
                imageFile, snapshot, cancellationToken).ConfigureAwait(false);
            if (warmed != null) return warmed;
        }
        var decode = await ResolveDecodeAsync(
            imageFile, snapshot, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        PreviewBasePair? pair = null;
        BaseImage? loadedBase;
        if (maxDimension > BaseImage.InteractivePreviewMaxDimension)
        {
            loadedBase = await Task.Run(
                () => _baseLoader.LoadFullBase(
                    imageFile,
                    decode,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var outcome = await Task.Run(
                () => _baseLoader.LoadPreviewBaseWithOutcome(
                    imageFile,
                    decode,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);
            pair = outcome.Pair;
            loadedBase = pair?.DetachInteractive();
        }

        using var pairLease = pair;
        using var baseImage = loadedBase;
        if (baseImage == null) return null;

        using var rendered = await Task.Run(
            () => _renderPipeline.Render(new RenderRequest(
                baseImage,
                snapshot,
                RenderIntent.Preview,
                maxDimension,
                new RenderOptions(false, false))),
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (maxDimension <= BaseImage.InteractivePreviewMaxDimension)
        {
            var settingsHash = RenderSettingsHash.Compute(
                snapshot,
                baseImage.Info.ProfileToken);
            var identity = CreatePreviewCacheIdentity(baseImage.Info, snapshot);
            _ = _previewCache.QueueSaveToCache(
                imageFile,
                rendered.Image,
                settingsHash,
                identity);
        }

        var originalViewSize = RenderGeometry.CalculateOriginalViewSize(
            baseImage.Info.FullWidth,
            baseImage.Info.FullHeight,
            snapshot);
        var bitmap = ConvertToBitmap(rendered.Image);
        return bitmap == null
            ? null
            : new ComparePreviewResult(
                bitmap,
                originalViewSize);
    }

    // A warm may finish between the caller's cached read and this request,
    // so the retained entry and the disk cache are both checked after the
    // optional wait: a completed warm must never be decoded a second time.
    private async Task<ComparePreviewResult?> JoinAdjacentWarmAsync(
        ImageFile imageFile,
        EditSettings settings,
        CancellationToken cancellationToken)
    {
        var hash = RenderSettingsHash.Compute(settings);
        var activeWarm = ActiveAdjacentWarmFor(imageFile, hash);
        CullPerf?.Record(activeWarm == null ? "WarmJoinMiss" : "WarmJoin", imageFile.CatalogId);
        if (activeWarm != null)
            await activeWarm.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await RunCacheWorkerAsync(imageFile, () =>
        {
            if (!CanReadAdjacentSource(imageFile)) return null;
            CullPerf?.Record("CacheJoinDecode", imageFile.CatalogId);
            using var cached = TryLoadAdjacentWarm(imageFile, hash) ??
                (_previewCache.HasSettingsMatchedEntry(imageFile, hash)
                    ? _previewCache.LoadRenderedPreview(imageFile) : null);
            // A legacy entry cannot supply original-pixel zoom geometry.
            if (cached?.SettingsHash != hash ||
                cached.OriginalViewPixelSize is not { Width: > 0, Height: > 0 } size)
                return null;
            var bitmap = ConvertToBitmap(cached.Image);
            return bitmap == null ? null : new ComparePreviewResult(bitmap, size);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static PreviewCacheIdentity CreatePreviewCacheIdentity(
        BaseImageInfo info,
        EditSettings settings) => new(
            RenderGeometry.CalculateOriginalViewSize(
                info.FullWidth,
                info.FullHeight,
                settings),
            new PixelSize(info.FullWidth, info.FullHeight));
}

public sealed class ComparePreviewResult : IDisposable
{
    private Bitmap? _bitmap;

    public Bitmap Bitmap =>
        _bitmap ?? throw new ObjectDisposedException(nameof(ComparePreviewResult));
    public PixelSize OriginalViewPixelSize { get; }

    internal ComparePreviewResult(Bitmap bitmap, PixelSize originalViewPixelSize)
    {
        _bitmap = bitmap;
        OriginalViewPixelSize = originalViewPixelSize;
    }

    public Bitmap DetachBitmap() =>
        Interlocked.Exchange(ref _bitmap, null) ??
        throw new ObjectDisposedException(nameof(ComparePreviewResult));

    public void Dispose() =>
        Interlocked.Exchange(ref _bitmap, null)?.Dispose();
}
