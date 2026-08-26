using Avalonia;
using Avalonia.Input;

namespace HappyPhoton.Views;

public partial class ZoomPanControl
{
    private const double PointerMovementThresholdSquared = 16;

    private Point _lastPanPoint;
    private Point _pressPoint;
    private bool _isPanning;
    private bool _isWhiteBalanceGesture;
    private bool _pointerMoved;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed &&
            IsWhiteBalancePicking &&
            !IsCropMode)
        {
            _isWhiteBalanceGesture = true;
            _pointerMoved = false;
            _pressPoint = e.GetPosition(this);
            _lastPanPoint = _pressPoint;
            _isPanning = CanPanContent();
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (point.Properties.IsLeftButtonPressed && TryBeginLoupePeek(e))
        {
            e.Handled = true;
            return;
        }

        if ((point.Properties.IsMiddleButtonPressed ||
             point.Properties.IsLeftButtonPressed) &&
            CanPanContent())
        {
            _isPanning = true;
            _lastPanPoint = e.GetPosition(this);
            e.Pointer.Capture(this);
            UpdatePointerCursor();
            e.Handled = true;
        }
    }

    internal bool CanPanContent() =>
        _scrollViewer != null &&
        (_scrollViewer.Extent.Width > _scrollViewer.Viewport.Width ||
         _scrollViewer.Extent.Height > _scrollViewer.Viewport.Height);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (UpdatePendingLoupeHold(e))
        {
            e.Handled = true;
            return;
        }

        if (_isWhiteBalanceGesture)
        {
            var currentPoint = e.GetPosition(this);
            var deltaX = _pressPoint.X - currentPoint.X;
            var deltaY = _pressPoint.Y - currentPoint.Y;
            _pointerMoved |= deltaX * deltaX + deltaY * deltaY >
                PointerMovementThresholdSquared;
        }

        if (_isPanning && _scrollViewer != null)
        {
            var currentPoint = e.GetPosition(this);
            var delta = _lastPanPoint - currentPoint;
            var previousOffset = _scrollViewer.Offset;

            _scrollViewer.Offset = new Vector(
                _scrollViewer.Offset.X + delta.X,
                _scrollViewer.Offset.Y + delta.Y);

            if (IsLoupePeekActive)
            {
                PublishLoupePan(_scrollViewer.Offset - previousOffset);
            }

            _lastPanPoint = currentPoint;
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (EndLoupePeek(releaseCapture: true))
        {
            e.Handled = true;
            return;
        }

        if (_isWhiteBalanceGesture)
        {
            var shouldPick = !_pointerMoved;
            _isWhiteBalanceGesture = false;
            _isPanning = false;
            e.Pointer.Capture(null);
            UpdatePointerCursor();
            if (shouldPick)
            {
                RequestWhiteBalancePick(e);
            }
            e.Handled = true;
            return;
        }

        if (_isPanning)
        {
            _isPanning = false;
            e.Pointer.Capture(null);
            UpdatePointerCursor();
            e.Handled = true;
        }
    }

    private void RequestWhiteBalancePick(PointerReleasedEventArgs e)
    {
        if (_imageControl == null ||
            _imageControl.Bounds.Width <= 0 ||
            _imageControl.Bounds.Height <= 0)
        {
            return;
        }

        var position = e.GetPosition(_imageControl);
        if (position.X < 0 || position.Y < 0 ||
            position.X > _imageControl.Bounds.Width ||
            position.Y > _imageControl.Bounds.Height)
        {
            return;
        }

        WhiteBalancePickRequested?.Invoke(
            this,
            (
                position.X / _imageControl.Bounds.Width,
                position.Y / _imageControl.Bounds.Height));
    }
}
