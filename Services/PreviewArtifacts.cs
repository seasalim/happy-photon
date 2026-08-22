using Avalonia;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed class PreviewArtifacts : IDisposable
{
    private Bitmap? _bitmap;
    private ClippingMask? _clippingMask;
    private PreviewPromotionLease? _promotionLease;

    public Bitmap? Bitmap => _bitmap;
    public HistogramData Histogram { get; }
    public ClippingStats? Clipping { get; }
    public bool IsRawSource { get; }
    internal DcpProfileState? ProfileState { get; }
    public long Generation { get; }
    public HistogramData? RawHistogram { get; }
    public double AsShotKelvin { get; }
    public double AsShotTint { get; }
    public bool IsBaseStale { get; }
    public ClippingMask? ClippingMask => _clippingMask;

    internal PreviewArtifacts(
        Bitmap? bitmap,
        HistogramData histogram,
        ClippingStats? clipping,
        bool isRawSource,
        DcpProfileState? profileState,
        long generation,
        ClippingMask? clippingMask,
        HistogramData? rawHistogram = null,
        double asShotKelvin = 6504,
        double asShotTint = 0,
        bool isBaseStale = false,
        PreviewPromotionLease? promotionLease = null)
    {
        _bitmap = bitmap;
        Histogram = histogram ?? throw new ArgumentNullException(nameof(histogram));
        Clipping = clipping;
        IsRawSource = isRawSource;
        ProfileState = profileState;
        Generation = generation;
        _clippingMask = clippingMask;
        RawHistogram = rawHistogram;
        AsShotKelvin = asShotKelvin;
        AsShotTint = asShotTint;
        IsBaseStale = isBaseStale;
        _promotionLease = promotionLease;
    }

    internal static PreviewArtifacts Empty(long generation, bool isRawSource) =>
        new(
            null,
            new HistogramData(),
            null,
            isRawSource,
            null,
            generation,
            null);

    public Bitmap? DetachBitmap() => Interlocked.Exchange(ref _bitmap, null);

    public ClippingMask? DetachClippingMask() =>
        Interlocked.Exchange(ref _clippingMask, null);

    internal PreviewPromotionLease? DetachPromotionLease() =>
        Interlocked.Exchange(ref _promotionLease, null);

    internal void CommitPromotion()
    {
        var bitmap = _bitmap;
        if (bitmap != null)
        {
            Interlocked.Exchange(ref _promotionLease, null)?.Commit(bitmap);
        }
    }

    internal (Bitmap? preview, HistogramData histogram) DetachLegacyResult()
    {
        var bitmap = DetachBitmap();
        if (bitmap != null)
        {
            Interlocked.Exchange(ref _promotionLease, null)?.Commit(bitmap);
        }
        Interlocked.Exchange(ref _clippingMask, null)?.Dispose();
        return (bitmap, Histogram);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _clippingMask, null)?.Dispose();
        Interlocked.Exchange(ref _promotionLease, null)?.Dispose();
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
    private PreviewPromotionLease? _promotionLease;

    public ImageFile ImageFile { get; }
    public HistogramData Histogram { get; }
    public HistogramData? RawHistogram { get; }
    public bool HasHistogram { get; }
    public ClippingStats? Clipping { get; }
    public bool IsRawSource { get; }
    internal DcpProfileState? ProfileState { get; }
    public double AsShotKelvin { get; }
    public double AsShotTint { get; }
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
        DcpProfileState? profileState = null,
        ClippingMask? clippingMask = null,
        double asShotKelvin = 6504,
        double asShotTint = 0)
    {
        ImageFile = imageFile;
        _bitmap = bitmap;
        Histogram = histogram;
        HasHistogram = hasHistogram;
        Generation = generation;
        RawHistogram = rawHistogram;
        Clipping = clipping;
        IsRawSource = isRawSource;
        ProfileState = profileState;
        _clippingMask = clippingMask;
        AsShotKelvin = asShotKelvin;
        AsShotTint = asShotTint;
    }

    public Bitmap DetachBitmap() =>
        Interlocked.Exchange(ref _bitmap, null) ??
        throw new ObjectDisposedException(nameof(PreviewRefresh));

    public ClippingMask? DetachClippingMask() =>
        Interlocked.Exchange(ref _clippingMask, null);

    internal PreviewPromotionLease? DetachPromotionLease() =>
        Interlocked.Exchange(ref _promotionLease, null);

    internal void SetPromotionLease(PreviewPromotionLease lease) =>
        _promotionLease = lease;

    public void Dispose()
    {
        Interlocked.Exchange(ref _clippingMask, null)?.Dispose();
        Interlocked.Exchange(ref _promotionLease, null)?.Dispose();
        Interlocked.Exchange(ref _bitmap, null)?.Dispose();
    }
}

internal sealed record DcpProfileState(
    string Token,
    DcpProfileErrorCode Status,
    string? Message,
    string? ProfileName,
    CameraIdentity? CameraIdentity,
    RawProfileSelection? RequestedSelection = null)
{
    internal static DcpProfileState From(
        BaseImageInfo info,
        RawProfileSelection? requestedSelection) => new(
            info.ProfileToken,
            info.ProfileStatus,
            info.ProfileMessage,
            info.DcpProfile?.Name,
            info.CameraIdentity,
            requestedSelection?.Clone());
}

public sealed class CachedPreviewBitmap : IDisposable
{
    private Bitmap? _bitmap;

    public Bitmap Bitmap =>
        _bitmap ?? throw new ObjectDisposedException(nameof(CachedPreviewBitmap));

    public bool SettingsMatch { get; }
    public HistogramData? Histogram { get; }
    public ClippingStats? Clipping { get; }

    public CachedPreviewBitmap(
        Bitmap bitmap,
        bool settingsMatch,
        HistogramData? histogram = null,
        ClippingStats? clipping = null)
    {
        _bitmap = bitmap;
        SettingsMatch = settingsMatch;
        Histogram = histogram;
        Clipping = clipping;
    }

    public Bitmap DetachBitmap() =>
        Interlocked.Exchange(ref _bitmap, null) ??
        throw new ObjectDisposedException(nameof(CachedPreviewBitmap));

    public void Dispose() =>
        Interlocked.Exchange(ref _bitmap, null)?.Dispose();
}

internal sealed record PreviewRenderIdentity(
    ImageFile ImageFile,
    long Generation,
    string DecodeKey,
    string SettingsHash,
    PixelSize OriginalImageSize,
    PixelSize OriginalViewSize);

internal sealed class RestingPreview : IDisposable
{
    private Bitmap? _bitmap;

    public Bitmap Bitmap =>
        _bitmap ?? throw new ObjectDisposedException(nameof(RestingPreview));

    public long ParentGeneration { get; }
    public int RequestedLongEdge { get; }
    public int RenderedLongEdge { get; }
    public int AchievableLongEdge { get; }

    internal RestingPreview(
        Bitmap bitmap,
        long parentGeneration,
        int requestedLongEdge,
        int renderedLongEdge,
        int achievableLongEdge)
    {
        _bitmap = bitmap;
        ParentGeneration = parentGeneration;
        RequestedLongEdge = requestedLongEdge;
        RenderedLongEdge = renderedLongEdge;
        AchievableLongEdge = achievableLongEdge;
    }

    public Bitmap DetachBitmap() =>
        Interlocked.Exchange(ref _bitmap, null) ??
        throw new ObjectDisposedException(nameof(RestingPreview));

    public void Dispose() =>
        Interlocked.Exchange(ref _bitmap, null)?.Dispose();
}
