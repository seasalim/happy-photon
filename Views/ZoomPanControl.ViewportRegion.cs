using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HappyPhoton.Views;

public partial class ZoomPanControl
{
    private bool _visibleRegionPublicationPending;
    private bool _forceVisibleRegionPublication;

    public Rect? VisibleRegion { get; private set; }

    public event EventHandler<Rect?>? VisibleRegionChanged;

    private void InitializeVisibleRegionTracking()
    {
        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged += OnViewportScrollChanged;
        }
        AttachedToVisualTree += OnViewportAttachedToVisualTree;
        DetachedFromVisualTree += OnViewportDetachedFromVisualTree;
    }

    private void OnViewportGeometryInputChanged(AvaloniaProperty property)
    {
        if (property == SourceProperty ||
            property == ZoomLevelProperty ||
            property == OriginalViewPixelSizeProperty ||
            property == IsColorAssessmentProperty)
        {
            RequestVisibleRegionPublication();
        }
    }

    private void OnViewportScrollChanged(
        object? sender,
        ScrollChangedEventArgs e) =>
        RequestVisibleRegionPublication();

    private void OnViewportAttachedToVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs e) =>
        RequestVisibleRegionPublication();

    private void OnViewportDetachedFromVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs e)
    {
        _visibleRegionPublicationPending = false;
        _forceVisibleRegionPublication = false;
        SetVisibleRegion(null);
    }

    public void RequestVisibleRegionPublication(bool force = false)
    {
        _forceVisibleRegionPublication |= force;
        if (_visibleRegionPublicationPending || !this.IsAttachedToVisualTree())
        {
            return;
        }

        _visibleRegionPublicationPending = true;
        Dispatcher.UIThread.Post(
            PublishVisibleRegion,
            DispatcherPriority.Render);
    }

    private void PublishVisibleRegion()
    {
        _visibleRegionPublicationPending = false;
        var force = _forceVisibleRegionPublication;
        _forceVisibleRegionPublication = false;
        if (!this.IsAttachedToVisualTree() ||
            Source == null ||
            _imageControl == null ||
            _scrollViewer == null ||
            _imageControl.Bounds.Width <= 0 ||
            _imageControl.Bounds.Height <= 0 ||
            _scrollViewer.Viewport.Width <= 0 ||
            _scrollViewer.Viewport.Height <= 0)
        {
            SetVisibleRegion(null, force);
            return;
        }

        var imageOrigin = _imageControl.TranslatePoint(default, _scrollViewer);
        if (imageOrigin == null)
        {
            SetVisibleRegion(null, force);
            return;
        }

        SetVisibleRegion(ViewportRegion.Calculate(
            new Rect(imageOrigin.Value, _imageControl.Bounds.Size),
            new Rect(_scrollViewer.Viewport)), force);
    }

    private void SetVisibleRegion(Rect? region, bool force = false)
    {
        if (!force && VisibleRegion == region)
        {
            return;
        }

        VisibleRegion = region;
        VisibleRegionChanged?.Invoke(this, region);
    }
}
