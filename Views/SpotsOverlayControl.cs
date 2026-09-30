using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public sealed class SpotsOverlayControl : Control
{
    private MainWindowViewModel? _owner;

    private IPointer? _pointer;

    private Point? _hover;

    private Point? _topLevelPosition;

    private TopLevel? _topLevel;

    private Point _press;

    private (SpotHandle Handle, Repair? Spot) _target;

    private static readonly Cursor CreateCursor = new(StandardCursorType.None);

    private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);

    private static readonly Cursor DrawCursor = new(StandardCursorType.Cross);

    private static readonly Pen Highlight = new(HappyPhotonColors.CropBorder, 2);

    private static readonly Pen Guide = new(HappyPhotonColors.CropBorder, 1);

    private static readonly Pen Outline = new(HappyPhotonColors.CropHandleStroke, 3);

    private static readonly Pen Source = new(HappyPhotonColors.CropBorder, 1, DashStyle.Dash);

    public SpotsOverlayControl()
    {
        ClipToBounds = true;
        Focusable = true;
        DataContextChanged += (_, _) => BindOwner();
        LayoutUpdated += ViewportChanged;
        AttachedToVisualTree += (_, _) =>
        {
            _topLevel = TopLevel.GetTopLevel(this);
            _topLevel?.AddHandler(PointerMovedEvent, TrackPointer, RoutingStrategies.Tunnel, handledEventsToo: true);
            _topLevel?.AddHandler(PointerExitedEvent, LeaveTopLevel);

            BindOwner();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _topLevel?.RemoveHandler(PointerMovedEvent, TrackPointer);
            _topLevel?.RemoveHandler(PointerExitedEvent, LeaveTopLevel);
            _topLevel = null;
            _topLevelPosition = null;

            CancelCapture();
            if (_owner != null) _owner.PropertyChanged -= OwnerChanged;
            _owner = null;
        };
    }

    private void BindOwner()
    {
        CancelCapture();
        if (_owner != null) _owner.PropertyChanged -= OwnerChanged;
        _owner = DataContext as MainWindowViewModel;
        if (_owner != null) _owner.PropertyChanged += OwnerChanged;
        Refresh();
    }

    private void OwnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.Spots) or nameof(MainWindowViewModel.CanEditSpots) or
            nameof(MainWindowViewModel.SpotDisplayMap) or nameof(MainWindowViewModel.SpotSize) or
            nameof(MainWindowViewModel.HideSpotCircles) or nameof(MainWindowViewModel.SelectedSpot) or
            nameof(MainWindowViewModel.IsSpotsGestureActive)) Refresh();
    }

    private void Refresh()
    {
        IsVisible = _owner?.CanEditSpots == true;
        if (_pointer != null && (!IsVisible || _owner?.IsSpotsGestureActive != true)) CancelCapture();
        UpdateFeedback();
        InvalidateVisual();
    }

    private void CancelCapture()
    {
        var pointer = _pointer;
        _pointer = null;
        if (pointer == null) return;

        _owner?.DiscardSpotsGesture();
        pointer.Capture(null);
        UpdateFeedback();
        InvalidateVisual();
    }

    internal Point ToCanvas(Point point)
    {
        var mapped = _owner!.SpotDisplayMap!.ToDisplay(point);
        return new(mapped.X * Bounds.Width, mapped.Y * Bounds.Height);
    }

    private Point ToBase(Point point) => _owner!.SpotDisplayMap!.ToBase(
        new(point.X / Bounds.Width, point.Y / Bounds.Height));

    private StreamGeometry Circle(Point center, double radius)
    {
        var map = _owner!.SpotDisplayMap!;
        var r = RepairGeometry.EffectiveRadius(radius, map.BaseWidth, map.BaseHeight);
        var geometry = new StreamGeometry();
        using var path = geometry.Open();
        for (var i = 0; i < 64; i++)
        {
            var angle = i * Math.Tau / 64;
            var point = ToCanvas(center + new Vector(Math.Cos(angle) * r / map.BaseWidth,
                Math.Sin(angle) * r / map.BaseHeight));
            if (i == 0) path.BeginFigure(point, false);
            else path.LineTo(point);
        }
        path.EndFigure(true);
        return geometry;
    }

    public override void Render(DrawingContext context)
    {
        if (_owner is not { CanEditSpots: true } vm || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        if (vm.HideSpotCircles) return;

        foreach (var spot in vm.Spots)
        {
            if (spot != vm.SelectedSpot && _hover == null && _pointer == null) continue;

            var circle = Circle(new(spot.U, spot.V), spot.Radius);
            context.DrawGeometry(null, Outline, circle);
            context.DrawGeometry(null,
                _target.Spot == spot && _target.Handle != SpotHandle.Source ? Highlight : Guide, circle);
        }

        if (vm.SelectedSpot is { } selected)
        {
            var map = vm.SpotDisplayMap!;
            var (su, sv) = RepairGeometry.ClampSource(selected, map.BaseWidth, map.BaseHeight);
            var circle = Circle(new(su, sv), selected.Radius);
            context.DrawGeometry(null, Outline, circle);
            context.DrawGeometry(null,
                _target.Spot == selected && _target.Handle == SpotHandle.Source ? Highlight : Source, circle);
            var source = ToCanvas(new(su, sv));
            var destination = ToCanvas(new(selected.U, selected.V));
            Line(source, destination);
            var delta = (Vector)(destination - source);
            if (delta.Length > 0)
            {
                var unit = delta.Normalize();
                var wing = new Vector(-unit.Y, unit.X) * 3;
                Line(destination, destination - unit * 7 + wing);
                Line(destination, destination - unit * 7 - wing);
            }
        }

        if (_hover is { } hover && _pointer == null && _target.Handle == SpotHandle.Create)
        {
            var cursor = Circle(ToBase(hover), vm.NewSpotRadius);
            context.DrawGeometry(null, Outline, cursor);
            context.DrawGeometry(null, Guide, cursor);
        }

        void Line(Point from, Point to)
        {
            context.DrawLine(Outline, from, to);
            context.DrawLine(Guide, from, to);
        }
    }

    internal SpotHandle? HitHandle(Point point, Repair spot)
    {
        var map = _owner!.SpotDisplayMap!;
        var p = ToBase(point);
        var radius = RepairGeometry.EffectiveRadius(spot.Radius, map.BaseWidth, map.BaseHeight);
        double Distance(double u, double v) => Math.Sqrt(Math.Pow((p.X - u) * map.BaseWidth, 2) +
            Math.Pow((p.Y - v) * map.BaseHeight, 2)) / radius;
        // Relative zones keep the center and edge distinct even for sub-four-pixel discs.
        if (Distance(spot.U, spot.V) < .65) return SpotHandle.Destination;
        var (su, sv) = RepairGeometry.ClampSource(spot, map.BaseWidth, map.BaseHeight);
        if (Distance(su, sv) < .8) return SpotHandle.Source;
        if (Math.Abs(Distance(spot.U, spot.V) - 1) <= .35) return SpotHandle.Edge;
        return null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_owner is not { CanEditSpots: true } vm || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var point = e.GetPosition(this);
        var target = ResolveTarget(point);
        _topLevelPosition = e.GetPosition(_topLevel);
        vm.SelectedSpot = target.Spot;
        _hover = point;

        if (vm.BeginSpotsGesture(target.Handle, ToBase(point)))
        {
            _press = point;
            _pointer = e.Pointer;
            SetFeedback(target, point);
            e.Pointer.Capture(this);
        }

        Focus();
        InvalidateVisual();
        e.Handled = true;
    }

    private (SpotHandle Handle, Repair? Spot) ResolveTarget(Point point)
    {
        var vm = _owner!;
        if (vm.SelectedSpot is { } selected && HitHandle(point, selected) is { } handle) return (handle, selected);

        var hit = vm.Spots.Reverse().FirstOrDefault(spot => HitHandle(point, spot) is SpotHandle.Destination or SpotHandle.Edge);

        return (hit == null ? SpotHandle.Create : SpotHandle.Destination, hit);
    }

    private void TrackPointer(object? sender, PointerEventArgs e)
    {
        _topLevelPosition = e.GetPosition(_topLevel);
        ViewportChanged(sender, e);
    }

    private void LeaveTopLevel(object? sender, PointerEventArgs e)
    {
        _topLevelPosition = null;
        ViewportChanged(sender, e);
    }

    private void ViewportChanged(object? sender, EventArgs e)
    {
        var previous = (_hover, _target, Cursor);
        UpdateFeedback();
        if (previous != (_hover, _target, Cursor)) InvalidateVisual();
    }

    private void UpdateFeedback()
    {
        if (_pointer != null) return;

        _hover = null;

        if (IsVisible && _owner?.CanEditSpots == true && _topLevelPosition is { } position &&
            _topLevel?.InputHitTest(position) == this && _topLevel.TranslatePoint(position, this) is { } point &&
            Bounds.Width > 0 && Bounds.Height > 0)
        {
            _hover = point;
            SetFeedback(ResolveTarget(point), point);
            return;
        }

        _target = default;
        Cursor = null;
    }

    private void SetFeedback((SpotHandle Handle, Repair? Spot) target, Point point)
    {
        _target = target;
        var center = target.Spot is { } spot ? ToCanvas(new(spot.U, spot.V)) : point;

        Cursor = target.Handle switch
        {
            SpotHandle.Create => _pointer == null ? CreateCursor : DrawCursor,
            SpotHandle.Edge => ResizeCursor.ForAngle(Math.Atan2(point.Y - center.Y, point.X - center.X)),
            _ => MoveCursor
        };
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
        _topLevelPosition = e.GetPosition(_topLevel);

        if (_pointer != null && _owner?.CanEditSpots == true)
            _owner.MoveSpotsGesture(ToBase(point), ((Vector)(point - _press)).Length);

        UpdateFeedback();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override async void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pointer == null || _owner == null) return;

        var point = e.GetPosition(this);
        _topLevelPosition = e.GetPosition(_topLevel);
        _owner.MoveSpotsGesture(ToBase(point), ((Vector)(point - _press)).Length);
        _pointer = null;
        e.Pointer.Capture(null);
        UpdateFeedback();
        InvalidateVisual();
        e.Handled = true;
        await _owner.CompleteSpotsGestureAsync();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        UpdateFeedback();
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        CancelCapture();
    }
}
