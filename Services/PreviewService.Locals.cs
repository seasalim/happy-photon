using HappyPhoton.Models;

namespace HappyPhoton.Services;

public partial class PreviewService
{
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
