using Avalonia.Media.Imaging;
using ImageMagick;

namespace HappyPhoton.Services;

internal sealed class PreviewPromotionLease : IDisposable
{
    private Action<Bitmap, MagickImage?>? _commit;
    private MagickImage? _thumbnailSource;

    public PreviewPromotionLease(
        MagickImage? thumbnailSource,
        Action<Bitmap, MagickImage?> commit)
    {
        _thumbnailSource = thumbnailSource;
        _commit = commit;
    }

    public void Commit(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var commit = Interlocked.Exchange(ref _commit, null);
        if (commit == null)
        {
            return;
        }

        var source = Interlocked.Exchange(ref _thumbnailSource, null);
        try
        {
            commit(bitmap, source);
            source = null;
        }
        finally
        {
            source?.Dispose();
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _commit, null);
        Interlocked.Exchange(ref _thumbnailSource, null)?.Dispose();
    }
}
