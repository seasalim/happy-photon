using Avalonia;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

internal sealed partial class RenderOutcome
{
    private bool _borrowsBitmap;

    public PreviewPaintGeometry? PaintGeometry { get; init; }

    public void Dispose()
    {
        Interlocked.Exchange(ref _promotionLease, null)?.Dispose();
        Interlocked.Exchange(ref _clippingMask, null)?.Dispose();
        var bitmap = Interlocked.Exchange(ref _bitmap, null);
        if (!_borrowsBitmap) bitmap?.Dispose();
    }

    public static RenderOutcome ProvisionalRotation(
        ImageFile image, long generation, Bitmap bitmap, PixelSize originalSize, bool borrowsBitmap) => new()
    {
        Image = image,
        Generation = generation,
        Class = RenderOutcomeClass.CachedUpgrade,
        Intent = PreviewSurfaceIntent.Edited,
        PaintSource = PreviewPaintSource.ProvisionalRotate,
        OriginalViewPixelSize = originalSize,
        BitmapMode = OutcomeFieldMode.Set,
        _bitmap = bitmap,
        _borrowsBitmap = borrowsBitmap,
        ClippingMode = OutcomeFieldMode.Clear
    };

    public static RenderOutcome Cached(
        ImageFile image,
        long generation,
        CachedPreviewBitmap cached,
        string? settingsIdentity,
        EditSettings? paintedSettings = null) => new()
    {
        SettingsIdentity = cached.SettingsMatch ? settingsIdentity : null,
        PaintGeometry = cached.SettingsMatch && settingsIdentity != null && paintedSettings != null &&
            settingsIdentity == RenderSettingsHash.Compute(paintedSettings)
                ? PreviewPaintGeometry.From(paintedSettings) : null,
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
        Bitmap bitmap,
        EditSettings settings) => new()
    {
        Image = image,
        Generation = generation,
        Class = RenderOutcomeClass.RestingUpgrade,
        PaintGeometry = PreviewPaintGeometry.From(settings),
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
