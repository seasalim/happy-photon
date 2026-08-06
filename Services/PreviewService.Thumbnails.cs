using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using static HappyPhoton.Services.BitmapConversionService;

namespace HappyPhoton.Services;

public sealed partial class PreviewService
{
    public Bitmap? TryPromoteRenderedThumbnail(
        ImageFile imageFile,
        EditSettings settings)
    {
        if (!imageFile.IsRaw || !settings.HasEdits) return null;

        Bitmap promoted;
        Bitmap cacheCopy;
        string hash;
        lock (_renderedSync)
        {
            var rendered = _lastRendered;
            if (rendered == null ||
                !ReferenceEquals(rendered.ImageFile, imageFile) ||
                rendered.Thumbnail == null)
            {
                return null;
            }

            hash = RenderSettingsHash.Compute(settings);
            if (!string.Equals(rendered.SettingsHash, hash, StringComparison.Ordinal))
            {
                return null;
            }

            promoted = CloneBitmap(rendered.Thumbnail);
            try
            {
                cacheCopy = CloneBitmap(rendered.Thumbnail);
            }
            catch
            {
                promoted.Dispose();
                throw;
            }
        }

        using (cacheCopy)
        {
            _renderedThumbnailCache.QueueSaveToCache(
                imageFile,
                cacheCopy,
                hash);
        }
        return promoted;
    }

    internal WeakReference<Bitmap>? GetRetainedThumbnailReference()
    {
        lock (_renderedSync)
        {
            return _lastRendered?.Thumbnail is { } thumbnail
                ? new WeakReference<Bitmap>(thumbnail)
                : null;
        }
    }
}
