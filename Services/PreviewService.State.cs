using Avalonia.Media.Imaging;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed partial class PreviewService
{
    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private sealed class RenderedPreview(
        ImageFile imageFile,
        WeakReference<Bitmap> bitmap,
        string settingsHash,
        long generation,
        Task<Bitmap?>? thumbnailTask)
    {
        private Bitmap? _strongBitmap;

        public ImageFile ImageFile { get; } = imageFile;
        public WeakReference<Bitmap> Bitmap { get; } = bitmap;
        public string SettingsHash { get; } = settingsHash;
        public long Generation { get; } = generation;
        public Task<Bitmap?>? ThumbnailTask { get; } = thumbnailTask;

        public void Retain(Bitmap value) => _strongBitmap = value;

        public Bitmap? DetachStrongBitmap() =>
            Interlocked.Exchange(ref _strongBitmap, null);
    }

    private sealed record PendingRefresh(
        ImageFile ImageFile,
        EditSettings Settings,
        ThumbnailSizeRequest ThumbnailRequest,
        bool ComputeWaveform,
        ClippingOverlaySide OverlaySides,
        BaseDecodeSettings Decode,
        long Generation,
        long SurfaceGeneration);
}
