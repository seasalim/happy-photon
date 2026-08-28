using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

internal sealed partial class RenderOutcome
{
    public static RenderOutcome Cached(
        ImageFile image,
        long generation,
        CachedPreviewBitmap cached,
        string? settingsIdentity) => new()
    {
        SettingsIdentity = cached.SettingsMatch ? settingsIdentity : null,
        Image = image,
        Generation = generation,
        Class = RenderOutcomeClass.CachedUpgrade,
        Intent = PreviewSurfaceIntent.Edited,
        PaintSource = PreviewPaintSource.CachedJpeg,
        OriginalViewPixelSize = cached.OriginalViewPixelSize,
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
        PreviewArtifacts artifacts,
        string? settingsIdentity,
        bool matchesRequestedSettings) => new()
    {
        Image = image,
        Generation = generation,
        Class = RenderOutcomeClass.ClippingUpgrade,
        Intent = intent,
        SettingsIdentity = settingsIdentity,
        MatchesRequestedSettings = matchesRequestedSettings,
        ClippingMode = OutcomeFieldMode.Set,
        Clipping = artifacts.Clipping,
        _clippingMask = artifacts.DetachClippingMask()
    };
}
