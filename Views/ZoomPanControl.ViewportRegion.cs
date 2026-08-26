using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Services;

namespace HappyPhoton.Views;

public partial class ZoomPanControl
{
    private bool _visibleRegionPublicationPending;
    private bool _forceVisibleRegionPublication;
    private bool _applyingNormalizedViewport;
    private long _normalizedViewportRestoreGeneration;

    public Rect? VisibleRegion { get; private set; }

    public event EventHandler<Rect?>? VisibleRegionChanged;

    public event EventHandler<NormalizedViewport>? NormalizedViewportChanged;

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
        AbandonNormalizedViewportApplication();
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
        CompleteNormalizedViewportApplication();
    }

    private void SetVisibleRegion(Rect? region, bool force = false)
    {
        if (!force && VisibleRegion == region)
        {
            return;
        }

        VisibleRegion = region;
        VisibleRegionChanged?.Invoke(this, region);
        if (!_applyingNormalizedViewport && Source != null)
        {
            NormalizedViewportChanged?.Invoke(this, CaptureNormalizedViewport());
        }
    }

    public NormalizedViewport CaptureNormalizedViewport()
    {
        var fit = GetFitZoomLevel();
        var relativeZoom = fit > 0 ? ZoomLevel / fit : 1;
        var center = VisibleRegion is { } region
            ? new NormalizedPoint(
                region.X + region.Width / 2,
                region.Y + region.Height / 2)
            : new NormalizedPoint(0.5, 0.5);
        return new NormalizedViewport(center, relativeZoom).Clamp();
    }

    public void ApplyNormalizedViewport(NormalizedViewport viewport)
    {
        if (Source == null || _scrollViewer == null) return;

        viewport = viewport.Clamp();
        _applyingNormalizedViewport = true;
        AutoFit = viewport.ZoomRelativeToFit == 1;
        var fit = GetFitZoomLevel();
        ZoomLevel = fit * viewport.ZoomRelativeToFit;
        var focal = new Point(
            _scrollViewer.Viewport.Width / 2,
            _scrollViewer.Viewport.Height / 2);
        _normalizedViewportRestoreGeneration =
            ScheduleAnchorRestoreAfterLayout(new ViewportAnchor(
                new Point(viewport.Center.X, viewport.Center.Y),
                focal));
        RequestVisibleRegionPublication(force: true);
    }

    private void OnNormalizedViewportAnchorRestored(long generation)
    {
        if (_applyingNormalizedViewport &&
            generation == _normalizedViewportRestoreGeneration)
        {
            RequestVisibleRegionPublication(force: true);
        }
    }

    private void CompleteNormalizedViewportApplication()
    {
        if (_applyingNormalizedViewport &&
            _appliedAnchorRestoreGeneration ==
                _normalizedViewportRestoreGeneration)
        {
            _applyingNormalizedViewport = false;
        }
    }

    // The guard waits on a restore that can never arrive once its anchor is
    // abandoned or the control leaves the tree; without this the pane would keep
    // following the shared viewport but stop publishing its own forever.
    private void AbandonNormalizedViewportApplication() =>
        _applyingNormalizedViewport = false;
}
