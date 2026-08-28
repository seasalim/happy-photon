using HappyPhoton.Services;

namespace HappyPhoton.Views;

internal sealed class SynchronizedPaneGroup : IDisposable
{
    private readonly SynchronizedViewService _view;
    private readonly HashSet<ZoomPanControl> _controls = [];
    private bool _publishing;
    private bool _broadcastingLoupe;
    public ZoomPanControl? Leader { get; init; }
    public Action<ZoomPanControl, double>? ZoomRequested { get; init; }
    public Action<ZoomPanControl, int>? RequiredDeviceLongEdgeChanged { get; init; }
    public SynchronizedPaneGroup(SynchronizedViewService view)
    {
        _view = view;
        _view.ViewportChanged += OnSynchronizedViewportChanged;
    }
    public void Attach(ZoomPanControl control)
    {
        if (!_controls.Add(control)) return;
        control.ZoomChanged += OnZoomChanged;
        control.AutoFitRequested += OnAutoFitRequested;
        control.NormalizedViewportChanged += OnViewportChanged;
        control.LoupePeekStarted += OnLoupeStarted;
        control.LoupePeekMoved += OnLoupeMoved;
        control.LoupePeekEnded += OnLoupeEnded;
        control.RequiredDeviceLongEdgeChanged += OnRequiredDeviceLongEdgeChanged;
        control.SizeChanged += OnSizeChanged;
        if (!ReferenceEquals(control, Leader)) control.ApplyNormalizedViewport(_view.Viewport);
    }
    public void Detach(ZoomPanControl control)
    {
        if (!_controls.Remove(control)) return;
        control.ZoomChanged -= OnZoomChanged;
        control.AutoFitRequested -= OnAutoFitRequested;
        control.NormalizedViewportChanged -= OnViewportChanged;
        control.LoupePeekStarted -= OnLoupeStarted;
        control.LoupePeekMoved -= OnLoupeMoved;
        control.LoupePeekEnded -= OnLoupeEnded;
        control.RequiredDeviceLongEdgeChanged -= OnRequiredDeviceLongEdgeChanged;
        control.SizeChanged -= OnSizeChanged;
    }
    public void RefreshFromLeader()
    {
        // A Before bitmap landing mid-peek must not republish the leader's
        // viewport and pull the panes out of alignment.
        if (Leader != null && _controls.All(control => !control.IsLoupePeekActive))
            Publish(Leader.CaptureNormalizedViewport(), Leader);
    }
    public bool CancelLoupePeek() => _controls.Aggregate(
        false, (canceled, control) => control.CancelLoupePeek() || canceled);
    private void OnZoomChanged(object? sender, double delta)
    {
        if (sender is not ZoomPanControl control) return;
        if (ZoomRequested != null) { ZoomRequested(control, delta); return; }
        var current = control.CaptureNormalizedViewport();
        var step = ViewModels.MainWindowViewModel.ZoomStepFactor;
        Publish(current with { ZoomRelativeToFit =
            current.ZoomRelativeToFit * (delta > 0 ? step : 1 / step) });
    }
    private void OnAutoFitRequested(object? sender, double fitZoom)
    {
        if (sender is not ZoomPanControl control || Leader != null) return;
        if (ZoomRequested != null) Publish(NormalizedViewport.Fit);
        else control.ApplyNormalizedViewport(_view.Viewport);
    }
    private void OnViewportChanged(object? sender, NormalizedViewport viewport)
    {
        if (!_broadcastingLoupe && sender is ZoomPanControl
            { IsLoupePeekActive: false } control) Publish(viewport, control);
    }
    private void OnSynchronizedViewportChanged(
        object? sender, NormalizedViewport viewport)
    {
        if (!_publishing) Apply(viewport, Leader);
    }
    private void OnSizeChanged(
        object? sender, Avalonia.Controls.SizeChangedEventArgs e)
    {
        if (sender is ZoomPanControl)
            Publish(Leader?.CaptureNormalizedViewport() ?? _view.Viewport, Leader);
    }
    private void Publish(
        NormalizedViewport viewport, ZoomPanControl? source = null)
    {
        _publishing = true;
        try
        {
            _view.SetViewport(viewport, _controls.Select(control =>
                control.GetNormalizedCenterBounds(viewport.ZoomRelativeToFit)));
            var keepSource = ReferenceEquals(source, Leader) ||
                _view.Viewport == viewport;
            Apply(_view.Viewport, keepSource ? source : null);
        }
        finally { _publishing = false; }
    }
    private void Apply(NormalizedViewport viewport, ZoomPanControl? source)
    {
        foreach (var control in _controls)
            if (!ReferenceEquals(control, source))
                control.ApplyNormalizedViewport(viewport);
    }
    private void OnLoupeStarted(object? sender, NormalizedPoint point) =>
        BroadcastLoupe(sender, control => control.BeginSynchronizedLoupePeek(
            point, (sender as ZoomPanControl)?.LoupeFocalFraction));
    private void OnLoupeMoved(object? sender, NormalizedPoint point) =>
        BroadcastLoupe(sender, control => control.MoveSynchronizedLoupePeek(point));
    private void OnLoupeEnded(object? sender, EventArgs e) =>
        BroadcastLoupe(sender, control => control.EndSynchronizedLoupePeek());
    private void BroadcastLoupe(object? sender, Action<ZoomPanControl> action)
    {
        if (_broadcastingLoupe) return;
        _broadcastingLoupe = true;
        try
        {
            foreach (var control in _controls)
                if (!ReferenceEquals(control, sender)) action(control);
        }
        finally { _broadcastingLoupe = false; }
    }
    private void OnRequiredDeviceLongEdgeChanged(object? sender, int longEdge)
    {
        if (sender is ZoomPanControl control)
            RequiredDeviceLongEdgeChanged?.Invoke(control, longEdge);
    }
    public void Dispose()
    {
        _view.ViewportChanged -= OnSynchronizedViewportChanged;
        foreach (var control in _controls.ToArray()) Detach(control);
    }
}
