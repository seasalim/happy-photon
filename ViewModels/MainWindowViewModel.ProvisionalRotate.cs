using Avalonia;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

internal enum PreviewPaintSource
{
    CachedJpeg,
    FreshRender,
    BackgroundRefresh,
    RestingRender,
    ProvisionalRotate
}

public partial class MainWindowViewModel
{
    private bool _isProvisionalRotation;

    private PreviewPaintGeometry? _rotationPaintGeometry;

    private Bitmap? _rotationRetainedBitmap;

    private PreviewPaintGeometry? _rotationRetainedGeometry;

    private PixelSize _rotationRetainedSize;

    private bool _rotationGeometryPending;

    private double? _rotationRetainedLuma;

    private void RecordRotationPaint(RenderOutcome outcome, Bitmap bitmap)
    {
        if (outcome.PaintSource == PreviewPaintSource.ProvisionalRotate) return;

        _rotationPaintGeometry = null;
        _rotationGeometryPending = false;
        if (outcome.Intent != PreviewSurfaceIntent.Edited) return;

        _rotationPaintGeometry = outcome.PaintGeometry ??
            ImageService.Previews.TryGetPreviewRenderIdentity(bitmap)?.PaintGeometry;
    }

    private void TryPaintProvisionalRotation(ImageFile image, EditSettings settings, long generation)
    {
        var geometry = _rotationRetainedGeometry ?? _rotationPaintGeometry;

        if (_rotationGeometryPending || !IsDevelopMode || IsShowingOriginal || IsBeforeAfterSplit || _proofIsDisplayed ||
            _requestedPreviewIntent != PreviewSurfaceIntent.Edited ||
            geometry is not { CanRotate: true } || !PreviewPaintGeometry.From(settings).CanRotate)
        {
            return;
        }

        CancelRestingPreview(clearParent: true);
        if (PreviewImage == null) return;

        if (_rotationRetainedBitmap == null)
        {
            _rotationRetainedBitmap = PreviewImage;
            _rotationRetainedGeometry = geometry;
            _rotationRetainedSize = OriginalViewPixelSize;
        }

        PaintRotation(image, settings.Rotation, generation, rollback: false);
    }

    private void RestoreRotationPaint(ImageFile image, EditSettings settings, long generation)
    {
        if (_rotationRetainedBitmap == null) return;

        PaintRotation(image, settings.Rotation, generation, rollback: true);
    }

    private void PaintRotation(ImageFile image, int rotation, long generation, bool rollback)
    {
        var delta = (rotation - _rotationRetainedGeometry!.Rotation + 360) % 360;
        var restoreObject = rollback && delta == 0;
        var bitmap = restoreObject ? _rotationRetainedBitmap! :
            BgraBitmapOrientation.ApplyExifOrientation(_rotationRetainedBitmap!, delta switch
            {
                90 => 6,
                180 => 3,
                270 => 8,
                _ => 1
            });
        var size = delta is 90 or 270
            ? new PixelSize(_rotationRetainedSize.Height, _rotationRetainedSize.Width)
            : _rotationRetainedSize;
        var outcome = RenderOutcome.ProvisionalRotation(image, generation, bitmap, size, restoreObject);

        if (ApplyRenderOutcome(outcome) && Histogram != null)
        {
            Histogram.Waveform = null;
            OnPropertyChanged(nameof(Histogram));
            OnPropertyChanged(nameof(EffectiveWaveform));
        }
    }

    private void ReleaseRotationPaint()
    {
        _isProvisionalRotation = false;
        var retained = _rotationRetainedBitmap;
        _rotationRetainedBitmap = null;
        _rotationRetainedLuma = null;
        _rotationRetainedGeometry = null;
        _rotationPaintGeometry = null;

        if (retained != null && !ReferenceEquals(PreviewImage, retained))
        {
            _bitmapRetirement.Retire(retained, () => ReferenceEquals(PreviewImage, retained));
        }
    }
}
