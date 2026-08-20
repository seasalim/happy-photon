using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HappyPhoton.Views;

public partial class ZoomPanControl
{
    private WheelAnchorCandidate? _wheelAnchorCandidate;
    private long _wheelAnchorGeneration;

    private readonly record struct WheelAnchorCandidate(
        ViewportAnchor? Anchor,
        double ZoomLevel,
        long Generation);

    private void InitializeWheelAnchoring() =>
        AddHandler(
            PointerWheelChangedEvent,
            OnPreviewPointerWheelChanged,
            RoutingStrategies.Tunnel);

    private void OnPreviewPointerWheelChanged(
        object? sender,
        PointerWheelEventArgs e)
    {
        var generation = ++_wheelAnchorGeneration;
        _wheelAnchorCandidate = new WheelAnchorCandidate(
            CaptureWheelAnchor(e),
            ZoomLevel,
            generation);

        ZoomChanged?.Invoke(this, e.Delta.Y);

        if (_wheelAnchorCandidate is { } candidate &&
            candidate.Generation == generation)
        {
            _wheelAnchorCandidate = null;
        }
        e.Handled = true;
    }

    private ViewportAnchor? CaptureWheelAnchor(PointerWheelEventArgs e)
    {
        if (_scrollViewer == null || _imageControl == null ||
            _imageControl.Bounds.Width <= 0 ||
            _imageControl.Bounds.Height <= 0)
        {
            return null;
        }

        return CreateViewportAnchor(
            e.GetPosition(_imageControl),
            e.GetPosition(_scrollViewer));
    }

    private ViewportAnchor? CaptureZoomChangeAnchor(double previousZoom)
    {
        if (AutoFit)
        {
            _wheelAnchorCandidate = null;
            return null;
        }

        if (TryConsumeWheelAnchor(previousZoom, out var wheelAnchor))
        {
            return wheelAnchor;
        }

        return CapturePendingOrViewportCenterAnchor();
    }

    private bool TryConsumeWheelAnchor(
        double previousZoom,
        out ViewportAnchor? anchor)
    {
        var candidate = _wheelAnchorCandidate;
        _wheelAnchorCandidate = null;
        if (candidate != null && candidate.Value.ZoomLevel == previousZoom)
        {
            anchor = candidate.Value.Anchor;
            return true;
        }

        anchor = null;
        return false;
    }
}
