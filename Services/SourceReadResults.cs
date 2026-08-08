using Avalonia.Media.Imaging;

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
        Bitmap? bitmap = null)
    {
        Status = status;
        _bitmap = bitmap;
    }

    public ThumbnailLoadStatus Status { get; }
    public Bitmap? Bitmap => _bitmap;

    internal static ThumbnailLoadResult Loaded(Bitmap bitmap) =>
        new(ThumbnailLoadStatus.Loaded, bitmap);

    internal static ThumbnailLoadResult Deferred() =>
        new(ThumbnailLoadStatus.DeferredForHydration);

    internal static ThumbnailLoadResult Failed() =>
        new(ThumbnailLoadStatus.Failed);

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
