using Avalonia.Media.Imaging;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed class PreviewArtifacts : IDisposable
{
    private Bitmap? _bitmap;
    private ClippingMask? _clippingMask;

    public Bitmap? Bitmap => _bitmap;
    public HistogramData Histogram { get; }
    public ClippingStats? Clipping { get; }
    public bool IsRawSource { get; }
    public long Generation { get; }
    public ClippingMask? ClippingMask => _clippingMask;

    internal PreviewArtifacts(
        Bitmap? bitmap,
        HistogramData histogram,
        ClippingStats? clipping,
        bool isRawSource,
        long generation,
        ClippingMask? clippingMask)
    {
        _bitmap = bitmap;
        Histogram = histogram ?? throw new ArgumentNullException(nameof(histogram));
        Clipping = clipping;
        IsRawSource = isRawSource;
        Generation = generation;
        _clippingMask = clippingMask;
    }

    internal static PreviewArtifacts Empty(long generation, bool isRawSource) =>
        new(
            null,
            new HistogramData(),
            null,
            isRawSource,
            generation,
            null);

    public Bitmap? DetachBitmap() => Interlocked.Exchange(ref _bitmap, null);

    public ClippingMask? DetachClippingMask() =>
        Interlocked.Exchange(ref _clippingMask, null);

    internal (Bitmap? preview, HistogramData histogram) DetachLegacyResult()
    {
        var bitmap = DetachBitmap();
        Interlocked.Exchange(ref _clippingMask, null)?.Dispose();
        return (bitmap, Histogram);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _clippingMask, null)?.Dispose();
        Interlocked.Exchange(ref _bitmap, null)?.Dispose();
    }
}

public sealed class PreviewBaseRefreshState : EventArgs
{
    public ImageFile ImageFile { get; }
    public long RequestId { get; }
    public bool IsRefreshing { get; }

    internal PreviewBaseRefreshState(
        ImageFile imageFile,
        long requestId,
        bool isRefreshing)
    {
        ImageFile = imageFile;
        RequestId = requestId;
        IsRefreshing = isRefreshing;
    }
}

public sealed class PreviewRefresh : EventArgs, IDisposable
{
    private Bitmap? _bitmap;
    private ClippingMask? _clippingMask;

    public ImageFile ImageFile { get; }
    public HistogramData Histogram { get; }
    public HistogramData? RawHistogram { get; }
    public bool HasHistogram { get; }
    public ClippingStats? Clipping { get; }
    public bool IsRawSource { get; }
    public ClippingMask? ClippingMask => _clippingMask;

    /// <summary>
    /// The render generation this refresh was produced for. A newer render can
    /// settle while a delayed refresh waits on its ready gate, so the UI rejects
    /// a refresh whose generation is older than the latest it has applied.
    /// </summary>
    public long Generation { get; }
    public Bitmap Bitmap =>
        _bitmap ?? throw new ObjectDisposedException(nameof(PreviewRefresh));

    internal PreviewRefresh(
        ImageFile imageFile,
        Bitmap bitmap,
        HistogramData histogram,
        bool hasHistogram,
        long generation,
        HistogramData? rawHistogram = null,
        ClippingStats? clipping = null,
        bool isRawSource = false,
        ClippingMask? clippingMask = null)
    {
        ImageFile = imageFile;
        _bitmap = bitmap;
        Histogram = histogram;
        HasHistogram = hasHistogram;
        Generation = generation;
        RawHistogram = rawHistogram;
        Clipping = clipping;
        IsRawSource = isRawSource;
        _clippingMask = clippingMask;
    }

    public Bitmap DetachBitmap() =>
        Interlocked.Exchange(ref _bitmap, null) ??
        throw new ObjectDisposedException(nameof(PreviewRefresh));

    public ClippingMask? DetachClippingMask() =>
        Interlocked.Exchange(ref _clippingMask, null);

    public void Dispose()
    {
        Interlocked.Exchange(ref _clippingMask, null)?.Dispose();
        Interlocked.Exchange(ref _bitmap, null)?.Dispose();
    }
}

public sealed class CachedPreviewBitmap : IDisposable
{
    private Bitmap? _bitmap;

    public Bitmap Bitmap =>
        _bitmap ?? throw new ObjectDisposedException(nameof(CachedPreviewBitmap));

    public bool SettingsMatch { get; }

    public CachedPreviewBitmap(Bitmap bitmap, bool settingsMatch)
    {
        _bitmap = bitmap;
        SettingsMatch = settingsMatch;
    }

    public Bitmap DetachBitmap() =>
        Interlocked.Exchange(ref _bitmap, null) ??
        throw new ObjectDisposedException(nameof(CachedPreviewBitmap));

    public void Dispose() =>
        Interlocked.Exchange(ref _bitmap, null)?.Dispose();
}
