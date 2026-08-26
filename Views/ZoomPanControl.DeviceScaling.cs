using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Services;

namespace HappyPhoton.Views;

internal static class ZoomGeometryCalculator
{
    internal static double BitmapRelativeZoom(
        PixelSize bitmapPixels,
        PixelSize originalViewPixels,
        double originalZoomLevel)
    {
        Validate(bitmapPixels, renderScaling: 1);
        Validate(originalViewPixels, renderScaling: 1);
        if (!double.IsFinite(originalZoomLevel) || originalZoomLevel <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(originalZoomLevel));
        }

        return originalZoomLevel *
            Math.Max(originalViewPixels.Width, originalViewPixels.Height) /
            Math.Max(bitmapPixels.Width, bitmapPixels.Height);
    }

    internal static Size ImageLogicalSize(
        PixelSize pixels,
        double zoomLevel,
        double renderScaling)
    {
        Validate(pixels, renderScaling);
        if (!double.IsFinite(zoomLevel) || zoomLevel <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(zoomLevel));
        }
        return new Size(
            pixels.Width * zoomLevel / renderScaling,
            pixels.Height * zoomLevel / renderScaling);
    }

    internal static double FitZoomLevel(
        PixelSize pixels,
        Size fitBox,
        double renderScaling)
    {
        Validate(pixels, renderScaling);
        if (fitBox.Width <= 0 || fitBox.Height <= 0)
        {
            return 1;
        }
        return Math.Min(
            fitBox.Width * renderScaling / pixels.Width,
            fitBox.Height * renderScaling / pixels.Height);
    }

    internal static int FittedDeviceLongEdge(
        PixelSize pixels,
        Size fitBox,
        double renderScaling)
    {
        var fitZoom = FitZoomLevel(pixels, fitBox, renderScaling);
        return checked((int)Math.Ceiling(
            Math.Max(pixels.Width, pixels.Height) * fitZoom));
    }

    internal static int RequiredDeviceLongEdge(
        PixelSize originalViewPixels,
        double originalZoomLevel)
    {
        Validate(originalViewPixels, renderScaling: 1);
        if (!double.IsFinite(originalZoomLevel) || originalZoomLevel <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(originalZoomLevel));
        }

        return checked((int)Math.Ceiling(
            Math.Max(originalViewPixels.Width, originalViewPixels.Height) *
            originalZoomLevel));
    }

    private static void Validate(PixelSize pixels, double renderScaling)
    {
        if (pixels.Width <= 0 || pixels.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixels));
        }
        if (!double.IsFinite(renderScaling) || renderScaling <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(renderScaling));
        }
    }
}

public partial class ZoomPanControl
{
    public static readonly StyledProperty<PixelSize> OriginalViewPixelSizeProperty =
        AvaloniaProperty.Register<ZoomPanControl, PixelSize>(
            nameof(OriginalViewPixelSize));

    private TopLevel? _scalingTopLevel;
    private ScheduledAnchorRestore? _scheduledAnchorRestore;
    private long _anchorRestoreGeneration;
    private long _appliedAnchorRestoreGeneration;

    private readonly record struct ViewportAnchor(
        Point NormalizedImagePoint,
        Point FocalPoint);

    private readonly record struct ScheduledAnchorRestore(
        ViewportAnchor Anchor,
        long Generation);

    public event EventHandler<int>? RequiredDeviceLongEdgeChanged;

    public PixelSize OriginalViewPixelSize
    {
        get => GetValue(OriginalViewPixelSizeProperty);
        set => SetValue(OriginalViewPixelSizeProperty, value);
    }

    private double RenderScaling =>
        _scalingTopLevel?.RenderScaling ??
        TopLevel.GetTopLevel(this)?.RenderScaling ??
        1;

    private void InitializeDeviceScaling()
    {
        LayoutUpdated += OnAnchorLayoutUpdated;
        AttachedToVisualTree += OnDeviceScalingAttached;
        DetachedFromVisualTree += OnDeviceScalingDetached;
        if (this.IsAttachedToVisualTree())
        {
            AttachScalingTopLevel();
        }
    }

    private void OnDeviceScalingAttached(
        object? sender,
        VisualTreeAttachmentEventArgs e)
    {
        AttachScalingTopLevel();
        UpdateImageSize();
        RequestRequiredBoundPublication();
    }

    private void OnDeviceScalingDetached(
        object? sender,
        VisualTreeAttachmentEventArgs e) =>
        DetachScalingTopLevel();

    private void AttachScalingTopLevel()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (ReferenceEquals(topLevel, _scalingTopLevel)) return;
        DetachScalingTopLevel();
        _scalingTopLevel = topLevel;
        if (_scalingTopLevel != null)
        {
            _scalingTopLevel.ScalingChanged += OnProductionScalingChanged;
        }
    }

    private void DetachScalingTopLevel()
    {
        if (_scalingTopLevel != null)
        {
            _scalingTopLevel.ScalingChanged -= OnProductionScalingChanged;
            _scalingTopLevel = null;
        }
    }

    private void OnProductionScalingChanged(object? sender, EventArgs e)
    {
        var anchor = EffectiveAutoFit
            ? null
            : CapturePendingOrViewportCenterAnchor();
        ScheduleAnchorRestoreAfterLayout(anchor);
        UpdateImageSize();
        if (EffectiveAutoFit)
        {
            RequestAutoFit();
        }
        RequestRequiredBoundPublication();
        RequestVisibleRegionPublication(force: true);
        _displayChainTrace?.OnInputChanged();
    }

    private ViewportAnchor? CaptureSourceChangeAnchor(
        Bitmap? oldSource,
        Bitmap? newSource)
    {
        if (oldSource == null || newSource == null || EffectiveAutoFit)
        {
            return null;
        }

        return CapturePendingOrViewportCenterAnchor();
    }

    private PixelSize GetOriginalViewPixelSize() =>
        OriginalViewPixelSize.Width > 0 && OriginalViewPixelSize.Height > 0
            ? OriginalViewPixelSize
            : Source?.PixelSize ?? default;

    public NormalizedCenterBounds GetNormalizedCenterBounds(
        double zoomRelativeToFit)
    {
        if (Source == null || _scrollViewer == null)
        {
            return NormalizedCenterBounds.Unconstrained;
        }

        var pixels = GetOriginalViewPixelSize();
        if (pixels.Width <= 0 || pixels.Height <= 0 ||
            _scrollViewer.Viewport.Width <= 0 ||
            _scrollViewer.Viewport.Height <= 0)
        {
            return NormalizedCenterBounds.Unconstrained;
        }

        zoomRelativeToFit = Math.Max(1, zoomRelativeToFit);
        var fit = GetFitZoomLevel();
        var imageWidth = pixels.Width * fit * zoomRelativeToFit / RenderScaling;
        var imageHeight = pixels.Height * fit * zoomRelativeToFit / RenderScaling;
        var halfVisibleX = Math.Min(
            0.5,
            _scrollViewer.Viewport.Width / (2 * imageWidth));
        var halfVisibleY = Math.Min(
            0.5,
            _scrollViewer.Viewport.Height / (2 * imageHeight));
        return new NormalizedCenterBounds(
            halfVisibleX,
            1 - halfVisibleX,
            halfVisibleY,
            1 - halfVisibleY);
    }

    private void UpdateImageSize()
    {
        if (_imageControl == null || Source == null) return;

        var bitmapZoom = ZoomGeometryCalculator.BitmapRelativeZoom(
            Source.PixelSize,
            GetOriginalViewPixelSize(),
            EffectiveZoomLevel);
        var logicalSize = ZoomGeometryCalculator.ImageLogicalSize(
            Source.PixelSize,
            bitmapZoom,
            RenderScaling);
        _imageControl.Width = logicalSize.Width;
        _imageControl.Height = logicalSize.Height;
        UpdateCropOverlaySize();
        UpdateClippingOverlaySize();
        UpdateAlignmentGridSize();
    }

    private ViewportAnchor? CaptureViewportCenterAnchor()
    {
        if (_scrollViewer == null || _imageControl == null ||
            _imageControl.Bounds.Width <= 0 ||
            _imageControl.Bounds.Height <= 0 ||
            _scrollViewer.Viewport.Width <= 0 ||
            _scrollViewer.Viewport.Height <= 0)
        {
            return null;
        }

        var focalPoint = new Point(
            _scrollViewer.Viewport.Width / 2,
            _scrollViewer.Viewport.Height / 2);
        var imageOrigin = _imageControl.TranslatePoint(default, _scrollViewer);
        return imageOrigin == null
            ? null
            : CreateViewportAnchor(focalPoint - imageOrigin.Value, focalPoint);
    }

    private ViewportAnchor? CapturePendingOrViewportCenterAnchor() =>
        _scheduledAnchorRestore?.Anchor ?? CaptureViewportCenterAnchor();

    private ViewportAnchor CreateViewportAnchor(
        Vector imagePosition,
        Point focalPoint) =>
        new(
            new Point(
                Math.Clamp(
                    imagePosition.X / _imageControl!.Bounds.Width,
                    0,
                    1),
                Math.Clamp(
                    imagePosition.Y / _imageControl.Bounds.Height,
                    0,
                    1)),
            focalPoint);

    private long ScheduleAnchorRestoreAfterLayout(ViewportAnchor? anchor)
    {
        var generation = ++_anchorRestoreGeneration;
        if (anchor == null)
        {
            _scheduledAnchorRestore = null;
            return generation;
        }

        _scheduledAnchorRestore = new ScheduledAnchorRestore(
            anchor.Value,
            generation);
        InvalidateArrange();
        // Layout runs above Background priority. Keep the first anchor alive
        // through the batch so the layout hook applies it before rendering.
        Dispatcher.UIThread.Post(
            () => CompleteAnchorRestore(generation),
            DispatcherPriority.Background);
        return generation;
    }

    private void OnAnchorLayoutUpdated(object? sender, EventArgs e)
    {
        var request = _scheduledAnchorRestore;
        if (request == null ||
            request.Value.Generation == _appliedAnchorRestoreGeneration)
        {
            return;
        }

        _appliedAnchorRestoreGeneration = request.Value.Generation;
        if (RestorePendingAnchor(request.Value.Anchor))
        {
            OnNormalizedViewportAnchorRestored(request.Value.Generation);
        }
        else
        {
            _appliedAnchorRestoreGeneration = 0;
            AbandonNormalizedViewportApplication();
        }
    }

    private bool RestorePendingAnchor(ViewportAnchor anchor)
    {
        if (_scrollViewer == null || _imageControl == null)
        {
            return false;
        }

        var imageOrigin = _imageControl.TranslatePoint(default, _scrollViewer);
        if (imageOrigin == null)
        {
            return false;
        }

        var anchoredPoint = imageOrigin.Value + new Vector(
            _imageControl.Bounds.Width * anchor.NormalizedImagePoint.X,
            _imageControl.Bounds.Height * anchor.NormalizedImagePoint.Y);
        var adjustment = anchoredPoint - anchor.FocalPoint;

        _scrollViewer.Offset += adjustment;
        return true;
    }

    private void CompleteAnchorRestore(long generation)
    {
        if (_scheduledAnchorRestore?.Generation == generation)
        {
            _scheduledAnchorRestore = null;
        }
    }

    private void RequestRequiredBoundPublication()
    {
        if (Source == null || _scrollViewer == null)
        {
            RequiredDeviceLongEdgeChanged?.Invoke(this, 0);
            return;
        }

        var originalViewPixels = GetOriginalViewPixelSize();
        if (originalViewPixels.Width <= 0 || originalViewPixels.Height <= 0)
        {
            return;
        }

        int requiredLongEdge;
        if (EffectiveAutoFit)
        {
            var fitBox = GetColorAssessmentGeometry().FitBox;
            if (fitBox.Width <= 0 || fitBox.Height <= 0)
            {
                return;
            }
            requiredLongEdge = ZoomGeometryCalculator.FittedDeviceLongEdge(
                originalViewPixels,
                fitBox,
                RenderScaling);
        }
        else
        {
            requiredLongEdge = ZoomGeometryCalculator.RequiredDeviceLongEdge(
                originalViewPixels,
                EffectiveZoomLevel);
        }

        RequiredDeviceLongEdgeChanged?.Invoke(this, requiredLongEdge);
    }
}
