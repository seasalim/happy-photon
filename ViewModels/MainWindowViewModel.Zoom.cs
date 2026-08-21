using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private bool _isZoomFitMode = true;

    [ObservableProperty]
    private PixelSize _originalViewPixelSize;

    public double ManualZoomLevel
    {
        get => ZoomLevel;
        set => ApplyManualZoom(value);
    }

    internal void ApplyFitZoom(double zoom)
    {
        if (!double.IsFinite(zoom) || zoom <= 0) return;
        IsZoomFitMode = true;
        ZoomLevel = zoom;
    }

    internal void ApplyManualZoom(double zoom)
    {
        if (!double.IsFinite(zoom) || zoom <= 0) return;
        IsZoomFitMode = false;
        ZoomLevel = Math.Clamp(zoom, MinZoom, MaxZoom);
    }

    private void UpdateOriginalViewPixelSize(Bitmap bitmap)
    {
        var identity = ImageService.Previews.TryGetPreviewRenderIdentity(bitmap);
        if (identity == null ||
            !ReferenceEquals(identity.ImageFile, SelectedImage))
        {
            return;
        }

        var updated = identity.OriginalViewSize;
        var previous = OriginalViewPixelSize;
        if (updated == previous)
        {
            return;
        }

        // Until the first identified paint the view falls back to treating the
        // displayed bitmap as the original, so the zoom value carries that
        // provisional meaning. Re-anchor it when the true original arrives:
        // preserve on-screen geometry by rescaling, never let the same number
        // silently change meaning (the "jumps on initial render" defect).
        var effectivePrevious =
            previous.Width > 0 && previous.Height > 0
                ? previous
                : PreviewImage?.PixelSize ?? bitmap.PixelSize;
        OriginalViewPixelSize = updated;
        if (!IsZoomFitMode &&
            effectivePrevious.Width > 0 &&
            effectivePrevious.Height > 0 &&
            updated.Width > 0 &&
            updated.Height > 0 &&
            effectivePrevious != updated)
        {
            var scale =
                (double)Math.Max(
                    effectivePrevious.Width,
                    effectivePrevious.Height) /
                Math.Max(updated.Width, updated.Height);
            // Preserve the exact on-screen geometry even when the re-anchored
            // value falls outside the interactive slider bounds; the next
            // user-authored zoom re-clamps. Clamping here would jump the
            // scene by the clamp ratio.
            ZoomLevel = ZoomLevel * scale;
        }
    }

    partial void OnZoomLevelChanged(double value) =>
        OnPropertyChanged(nameof(ManualZoomLevel));
}
