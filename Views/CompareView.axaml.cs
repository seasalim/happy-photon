using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class CompareView : UserControl
{
    private readonly HashSet<ZoomPanControl> _hookedControls = [];
    private SynchronizedViewService? _synchronizedView;
    private bool _broadcastingLoupe;

    public CompareView()
    {
        InitializeComponent();
        AddHandler(
            PointerPressedEvent,
            OnPanePointerPressed,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_synchronizedView != null)
        {
            _synchronizedView.ViewportChanged -= OnSynchronizedViewportChanged;
        }
        _synchronizedView = (DataContext as MainWindowViewModel)?.SynchronizedView;
        if (_synchronizedView != null)
        {
            _synchronizedView.ViewportChanged += OnSynchronizedViewportChanged;
        }
    }

    private void OnPaneAttached(
        object? sender,
        VisualTreeAttachmentEventArgs e)
    {
        if (sender is not ZoomPanControl control ||
            !_hookedControls.Add(control)) return;
        control.ZoomChanged += OnPaneZoomChanged;
        control.AutoFitRequested += OnPaneAutoFitRequested;
        control.NormalizedViewportChanged += OnPaneViewportChanged;
        control.LoupePeekStarted += OnPaneLoupeStarted;
        control.LoupePeekMoved += OnPaneLoupeMoved;
        control.LoupePeekEnded += OnPaneLoupeEnded;
        control.RequiredDeviceLongEdgeChanged +=
            OnPaneRequiredDeviceLongEdgeChanged;
        control.ApplyNormalizedViewport(
            _synchronizedView?.Viewport ?? NormalizedViewport.Fit);
    }

    private void OnPaneDetached(
        object? sender,
        VisualTreeAttachmentEventArgs e)
    {
        if (sender is not ZoomPanControl control ||
            !_hookedControls.Remove(control))
            return;
        control.ZoomChanged -= OnPaneZoomChanged;
        control.AutoFitRequested -= OnPaneAutoFitRequested;
        control.NormalizedViewportChanged -= OnPaneViewportChanged;
        control.LoupePeekStarted -= OnPaneLoupeStarted;
        control.LoupePeekMoved -= OnPaneLoupeMoved;
        control.LoupePeekEnded -= OnPaneLoupeEnded;
        control.RequiredDeviceLongEdgeChanged -=
            OnPaneRequiredDeviceLongEdgeChanged;
    }

    private void OnPaneSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is ZoomPanControl control &&
            _hookedControls.Contains(control))
        {
            SetSynchronizedViewport(
                _synchronizedView?.Viewport ?? NormalizedViewport.Fit);
            control.ApplyNormalizedViewport(
                _synchronizedView?.Viewport ?? NormalizedViewport.Fit);
        }
    }

    private void OnPanePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var paneBorder = (e.Source as Visual)?.GetVisualAncestors()
            .Prepend(e.Source as Visual)
            .OfType<Border>()
            .FirstOrDefault(border => border.Classes.Contains("compare-pane"));
        if (paneBorder?.DataContext is ComparePaneViewModel pane &&
            DataContext is MainWindowViewModel vm)
        {
            vm.ActivateComparePaneCommand.Execute(pane);
        }
    }

    private void OnPaneZoomChanged(object? sender, double delta)
    {
        if (sender is not ZoomPanControl control || _synchronizedView == null)
            return;
        var current = control.CaptureNormalizedViewport();
        SetSynchronizedViewport(current with
        {
            ZoomRelativeToFit = current.ZoomRelativeToFit *
                (delta > 0
                    ? MainWindowViewModel.ZoomStepFactor
                    : 1 / MainWindowViewModel.ZoomStepFactor)
        });
    }

    private void OnPaneAutoFitRequested(object? sender, double fitZoom)
    {
        if (sender is ZoomPanControl control && _synchronizedView != null)
        {
            control.ApplyNormalizedViewport(_synchronizedView.Viewport);
        }
    }

    private void OnPaneViewportChanged(
        object? sender,
        NormalizedViewport viewport)
    {
        if (_broadcastingLoupe ||
            sender is ZoomPanControl { IsLoupePeekActive: true })
            return;
        SetSynchronizedViewport(viewport);
    }

    private void OnSynchronizedViewportChanged(
        object? sender,
        NormalizedViewport viewport)
    {
        SetSynchronizedViewport(viewport);
        if (_synchronizedView?.Viewport != viewport) return;

        foreach (var control in _hookedControls)
        {
            control.ApplyNormalizedViewport(viewport);
        }
    }

    private void SetSynchronizedViewport(NormalizedViewport viewport)
    {
        if (_synchronizedView == null) return;

        var previous = _synchronizedView.Viewport;
        _synchronizedView.SetViewport(
            viewport,
            _hookedControls.Select(control =>
                control.GetNormalizedCenterBounds(
                    viewport.ZoomRelativeToFit)));
        if (_synchronizedView.Viewport == previous)
        {
            foreach (var control in _hookedControls)
            {
                control.ApplyNormalizedViewport(_synchronizedView.Viewport);
            }
        }
    }

    private void OnPaneLoupeStarted(object? sender, NormalizedPoint point)
    {
        if (_broadcastingLoupe) return;
        _broadcastingLoupe = true;
        try
        {
            foreach (var control in _hookedControls)
            {
                if (!ReferenceEquals(control, sender))
                    control.BeginSynchronizedLoupePeek(point);
            }
        }
        finally
        {
            _broadcastingLoupe = false;
        }
    }

    private void OnPaneLoupeEnded(object? sender, EventArgs e)
    {
        if (_broadcastingLoupe) return;
        _broadcastingLoupe = true;
        try
        {
            foreach (var control in _hookedControls)
            {
                if (!ReferenceEquals(control, sender))
                    control.EndSynchronizedLoupePeek();
            }
        }
        finally
        {
            _broadcastingLoupe = false;
        }
    }

    private void OnPaneLoupeMoved(object? sender, NormalizedPoint point)
    {
        if (_broadcastingLoupe) return;
        _broadcastingLoupe = true;
        try
        {
            foreach (var control in _hookedControls)
            {
                if (!ReferenceEquals(control, sender))
                {
                    control.MoveSynchronizedLoupePeek(point);
                }
            }
        }
        finally
        {
            _broadcastingLoupe = false;
        }
    }

    private void OnPaneRequiredDeviceLongEdgeChanged(
        object? sender,
        int longEdge)
    {
        if (sender is ZoomPanControl control &&
            control.DataContext is ComparePaneViewModel pane &&
            DataContext is MainWindowViewModel vm)
        {
            vm.PublishCompareRequiredDeviceLongEdge(
                pane,
                longEdge,
                control.IsLoupePeekActive);
        }
    }

    public bool CancelLoupePeek()
    {
        var canceled = false;
        foreach (var control in _hookedControls)
        {
            canceled |= control.CancelLoupePeek();
        }
        return canceled;
    }
}
