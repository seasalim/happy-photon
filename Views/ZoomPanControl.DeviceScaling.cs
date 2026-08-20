using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

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
    private Point? _pendingNormalizedAnchor;

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
        _pendingNormalizedAnchor = CaptureNormalizedAnchor();
        UpdateImageSize();
        if (AutoFit)
        {
            RequestAutoFit();
        }
        RestorePendingAnchorAfterLayout();
        RequestRequiredBoundPublication();
        RequestVisibleRegionPublication(force: true);
        _displayChainTrace?.OnInputChanged();
    }

    private void OnPreviewSourceChanging(Bitmap? oldSource, Bitmap? newSource)
    {
        if (oldSource == null || newSource == null || AutoFit)
        {
            return;
        }

        _pendingNormalizedAnchor = CaptureNormalizedAnchor();
    }

    private PixelSize GetOriginalViewPixelSize() =>
        OriginalViewPixelSize.Width > 0 && OriginalViewPixelSize.Height > 0
            ? OriginalViewPixelSize
            : Source?.PixelSize ?? default;

    private void UpdateImageSize()
    {
        if (_imageControl == null || Source == null) return;

        var bitmapZoom = ZoomGeometryCalculator.BitmapRelativeZoom(
            Source.PixelSize,
            GetOriginalViewPixelSize(),
            ZoomLevel);
        var logicalSize = ZoomGeometryCalculator.ImageLogicalSize(
            Source.PixelSize,
            bitmapZoom,
            RenderScaling);
        _imageControl.Width = logicalSize.Width;
        _imageControl.Height = logicalSize.Height;
        UpdateCropOverlaySize();
        UpdateClippingOverlaySize();
    }

    private Point? CaptureNormalizedAnchor()
    {
        if (_scrollViewer == null || _imageControl == null ||
            _imageControl.Bounds.Width <= 0 ||
            _imageControl.Bounds.Height <= 0)
        {
            return null;
        }

        return new Point(
            Math.Clamp(
                (_scrollViewer.Offset.X + _scrollViewer.Viewport.Width / 2) /
                    _imageControl.Bounds.Width,
                0,
                1),
            Math.Clamp(
                (_scrollViewer.Offset.Y + _scrollViewer.Viewport.Height / 2) /
                    _imageControl.Bounds.Height,
                0,
                1));
    }

    private void RestorePendingAnchorAfterLayout()
    {
        if (_pendingNormalizedAnchor == null) return;
        Dispatcher.UIThread.Post(
            RestorePendingAnchor,
            DispatcherPriority.Render);
    }

    private void RestorePendingAnchor()
    {
        var anchor = _pendingNormalizedAnchor;
        _pendingNormalizedAnchor = null;
        if (anchor == null || _scrollViewer == null || _imageControl == null)
        {
            return;
        }

        _scrollViewer.Offset = new Vector(
            Math.Max(
                0,
                _imageControl.Bounds.Width * anchor.Value.X -
                    _scrollViewer.Viewport.Width / 2),
            Math.Max(
                0,
                _imageControl.Bounds.Height * anchor.Value.Y -
                    _scrollViewer.Viewport.Height / 2));
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
        if (AutoFit)
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
                ZoomLevel);
        }

        RequiredDeviceLongEdgeChanged?.Invoke(this, requiredLongEdge);
    }
}
