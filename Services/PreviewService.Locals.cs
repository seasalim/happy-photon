using HappyPhoton.Models;
using Avalonia.Media.Imaging;
using System.Runtime.CompilerServices;

namespace HappyPhoton.Services;

public partial class PreviewService
{
    private readonly ConditionalWeakTable<Bitmap, BaseImage> _localRangeBases = new();

    internal PreviewBaseSnapshot? AcquireLocalRangeBase(ImageFile image, EditSettings settings, int edge, Bitmap? surface = null)
    {
        if (Volatile.Read(ref _disposed) != 0) return null;
        BaseImage? expected = null;
        if (surface != null && !_localRangeBases.TryGetValue(surface, out expected)) return null;
        var decode = BaseDecodeSettings.From(settings);
        var lease = _baseCoordinator.TryAcquireCurrent(image, decode, allowProfileOutcome: true);
        if (lease == null) return null;
        if (expected == null && edge <= BaseImage.InteractivePreviewMaxDimension || ReferenceEquals(expected, lease.Base))
            return new PreviewBaseSnapshot(lease.Base, lease.Dispose);
        lease.Dispose();
        var large = _baseCoordinator.TryAcquireLargeCurrent(image, decode);
        if (large != null && (expected == null || ReferenceEquals(expected, large.Base))) return large;
        large?.Dispose();
        return null;
    }

    internal LocalsFrame? GetLocalsFrame(ImageFile image, EditSettings settings)
    {
        if (Volatile.Read(ref _disposed) != 0) return null;
        using var lease = _baseCoordinator.TryAcquireCurrent(image, BaseDecodeSettings.From(settings),
            allowProfileOutcome: true);
        if (lease == null) return null;
        return RenderGeometry.CalculateLocalsFrame((int)lease.Base.Pixels.Width,
            (int)lease.Base.Pixels.Height, settings);
    }
}
