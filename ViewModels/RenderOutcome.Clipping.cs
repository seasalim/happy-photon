using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

internal sealed partial class RenderOutcome
{
    public static RenderOutcome Cached(
        ImageFile image,
        long generation,
        CachedPreviewBitmap cached) => new()
    {
        Image = image,
        Generation = generation,
        Class = RenderOutcomeClass.CachedUpgrade,
        Intent = PreviewSurfaceIntent.Edited,
        PaintSource = PreviewPaintSource.CachedJpeg,
        BitmapMode = OutcomeFieldMode.Set,
        _bitmap = cached.DetachBitmap(),
        HistogramMode = cached.SettingsMatch
            ? OutcomeFieldMode.Set
            : OutcomeFieldMode.Clear,
        Histogram = cached.Histogram,
        ClippingMode = cached.SettingsMatch
            ? OutcomeFieldMode.Set
            : OutcomeFieldMode.Clear,
        Clipping = cached.Clipping
    };

    public static RenderOutcome Resting(
        ImageFile image,
        long generation,
        Bitmap bitmap) => new()
    {
        Image = image,
        Generation = generation,
        Class = RenderOutcomeClass.RestingUpgrade,
        Intent = PreviewSurfaceIntent.Edited,
        PaintSource = PreviewPaintSource.RestingRender,
        BitmapMode = OutcomeFieldMode.Set,
        _bitmap = bitmap
    };

    public static RenderOutcome FromClippingArtifacts(
        ImageFile image,
        long generation,
        PreviewSurfaceIntent intent,
        PreviewArtifacts artifacts)
    {
        var stale = artifacts.IsBaseStale;
        return new RenderOutcome
        {
            Image = image,
            Generation = generation,
            Class = RenderOutcomeClass.ClippingUpgrade,
            Intent = intent,
            IsBaseStale = stale,
            ClippingMode = stale
                ? OutcomeFieldMode.Clear
                : OutcomeFieldMode.Set,
            Clipping = stale ? null : artifacts.Clipping,
            _clippingMask = stale ? null : artifacts.DetachClippingMask()
        };
    }
}
