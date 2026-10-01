using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Rendering.Composition;

namespace HappyPhoton.Views;

internal sealed class OverlayPointerTracker
{
    private readonly Control _overlay;

    private readonly Action _changed;

    private TopLevel? _topLevel;

    private Point? _position;

    private Task? _pendingRefresh;

    public OverlayPointerTracker(Control overlay, Action changed)
    {
        _overlay = overlay;
        _changed = changed;
        overlay.LayoutUpdated += RefreshAfterRender;
        // Re-entry after a repaint can happen without a PointerMoved event.
        overlay.PointerEntered += Track;
        overlay.AttachedToVisualTree += (_, _) =>
        {
            _topLevel = TopLevel.GetTopLevel(overlay);
            _topLevel?.AddHandler(InputElement.PointerMovedEvent, Track, RoutingStrategies.Tunnel, handledEventsToo: true);
            _topLevel?.AddHandler(InputElement.PointerExitedEvent, Leave);
        };
        overlay.DetachedFromVisualTree += (_, _) =>
        {
            _topLevel?.RemoveHandler(InputElement.PointerMovedEvent, Track);
            _topLevel?.RemoveHandler(InputElement.PointerExitedEvent, Leave);
            _topLevel = null;
            _pendingRefresh = null;
            _position = null;
        };
    }

    public Point? Position => _overlay.IsVisible && _position is { } position &&
        _topLevel?.InputHitTest(position) == _overlay && _overlay.Bounds.Width > 0 && _overlay.Bounds.Height > 0
            ? _topLevel.TranslatePoint(position, _overlay) : null;

    public void Record(PointerEventArgs e) => _position = e.GetPosition(_topLevel);

    private void Track(object? sender, PointerEventArgs e)
    {
        Record(e);
        _changed();
    }

    private async void RefreshAfterRender(object? sender, EventArgs e)
    {
        _changed();

        if (!_overlay.IsVisible || _position == null ||
            ElementComposition.GetElementVisual(_overlay) is not { } visual)
        {
            return;
        }

        // Hit testing catches up after rendering, even if visibility changes skip pointer re-entry.
        var rendered = visual.Compositor.RequestCompositionBatchCommitAsync().Rendered;
        if (ReferenceEquals(_pendingRefresh, rendered)) return;

        _pendingRefresh = rendered;
        await rendered;
        if (!ReferenceEquals(_pendingRefresh, rendered)) return;

        _pendingRefresh = null;
        _changed();
    }

    private void Leave(object? sender, PointerEventArgs e)
    {
        _position = null;
        _changed();
    }
}
