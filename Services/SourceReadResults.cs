using Avalonia.Media.Imaging;
using Avalonia;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public enum ThumbnailLoadStatus
{
    Loaded,
    DeferredForHydration,
    Failed
}

public sealed class ThumbnailLoadResult : IDisposable
{
    private Bitmap? _bitmap;

    private ThumbnailLoadResult(
        ThumbnailLoadStatus status,
        ThumbnailSizeRequest request,
        Bitmap? bitmap = null,
        bool betterResultDeferredForHydration = false,
        bool sourceCannotProvideRequestedQuality = false)
    {
        Status = status;
        _bitmap = bitmap;
        Request = request;
        PixelDimensions = bitmap?.PixelSize ?? default;
        SatisfiesMinimumDimension = bitmap != null &&
            Math.Max(PixelDimensions.Width, PixelDimensions.Height) >=
            request.MinimumDimension;
        BetterResultDeferredForHydration = betterResultDeferredForHydration;
        SourceCannotProvideRequestedQuality = sourceCannotProvideRequestedQuality;
    }

    public ThumbnailLoadStatus Status { get; }
    public Bitmap? Bitmap => _bitmap;
    public PixelSize PixelDimensions { get; }
    public ThumbnailSizeRequest Request { get; }
    public bool SatisfiesMinimumDimension { get; }
    public bool BetterResultDeferredForHydration { get; }
    public bool SourceCannotProvideRequestedQuality { get; }

    internal static ThumbnailLoadResult Loaded(
        Bitmap bitmap,
        ThumbnailSizeRequest request,
        bool betterResultDeferredForHydration = false,
        bool sourceCannotProvideRequestedQuality = false) =>
        new(
            ThumbnailLoadStatus.Loaded,
            request,
            bitmap,
            betterResultDeferredForHydration,
            sourceCannotProvideRequestedQuality);

    internal static ThumbnailLoadResult Loaded(Bitmap bitmap) =>
        Loaded(bitmap, new ThumbnailSizeRequest(150, 150));

    internal static ThumbnailLoadResult Deferred(
        ThumbnailSizeRequest request) =>
        new(ThumbnailLoadStatus.DeferredForHydration, request);

    internal static ThumbnailLoadResult Failed(
        ThumbnailSizeRequest request) =>
        new(ThumbnailLoadStatus.Failed, request);

    public Bitmap? DetachBitmap() => Interlocked.Exchange(ref _bitmap, null);

    public void Dispose() => DetachBitmap()?.Dispose();
}

public enum MetadataLoadStatus
{
    Loaded,
    DeferredForHydration,
    Failed
}

public readonly record struct ExportHydrationScope(
    int FileCount,
    long LogicalBytes)
{
    public bool IsRequired => FileCount > 0;
}

internal sealed class SourceReadDeferredException(string path)
    : IOException($"Source requires hydration: {path}")
{
}
