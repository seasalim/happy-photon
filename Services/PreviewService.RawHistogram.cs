using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed partial class PreviewService
{
    public HistogramData? TryGetRawHistogram(
        ImageFile imageFile,
        BaseDecodeSettings decode)
    {
        ArgumentNullException.ThrowIfNull(imageFile);
        ArgumentNullException.ThrowIfNull(decode);
        if (Volatile.Read(ref _disposed) != 0) return null;

        try
        {
            using var snapshot = _baseCoordinator.TryAcquireCurrent(
                imageFile,
                decode);
            return snapshot?.Base.Info.RawHistogram;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }
}
