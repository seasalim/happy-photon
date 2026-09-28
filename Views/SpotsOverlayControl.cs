using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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

    private Point _press;

    private static readonly Pen Guide = new(HappyPhotonColors.CropBorder, 1);

    private static readonly Pen Outline = new(HappyPhotonColors.CropHandleStroke, 3);

    private static readonly Pen Source = new(HappyPhotonColors.CropBorder, 1, DashStyle.Dash);

    public SpotsOverlayControl()
    {
        ClipToBounds = true;
        Focusable = true;
        DataContextChanged += (_, _) => BindOwner();
        AttachedToVisualTree += (_, _) => BindOwner();
        DetachedFromVisualTree += (_, _) =>
        {
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
            nameof(MainWindowViewModel.SpotDisplayMap) or nameof(MainWindowViewModel.SpotSize)) Refresh();
    }

    private void Refresh()
    {
        IsVisible = _owner?.CanEditSpots == true;
        if (_pointer != null && (!IsVisible || _owner?.IsSpotsGestureActive != true)) CancelCapture();
        InvalidateVisual();
    }

    private void CancelCapture()
    {
        var pointer = _pointer;
        _pointer = null;
        if (pointer == null) return;
        _owner?.DiscardSpotsGesture();
        pointer.Capture(null);
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
        foreach (var spot in vm.Spots)
        {
            if (spot != vm.SelectedSpot && _hover == null) continue;
            var circle = Circle(new(spot.U, spot.V), spot.Radius);
            context.DrawGeometry(null, Outline, circle);
            context.DrawGeometry(null, Guide, circle);
        }
        if (vm.SelectedSpot is { } selected)
        {
            var map = vm.SpotDisplayMap!;
            var (su, sv) = RepairGeometry.ClampSource(selected, map.BaseWidth, map.BaseHeight);
            var circle = Circle(new(su, sv), selected.Radius);
            context.DrawGeometry(null, Outline, circle);
            context.DrawGeometry(null, Source, circle);
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
        if (_hover is { } hover && _pointer == null)
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
        var handle = vm.SelectedSpot is { } selected ? HitHandle(point, selected) : null;
        if (handle == null)
        {
            var hit = vm.Spots.Reverse().FirstOrDefault(spot => HitHandle(point, spot) is SpotHandle.Destination or SpotHandle.Edge);
            vm.SelectedSpot = hit;
            handle = hit == null ? SpotHandle.Create : SpotHandle.Destination;
        }
        if (vm.BeginSpotsGesture(handle.Value, ToBase(point)))
        {
            _press = point;
            _pointer = e.Pointer;
            e.Pointer.Capture(this);
        }
        Focus();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _hover = e.GetPosition(this);
        Cursor = new Cursor(StandardCursorType.None);
        if (_pointer != null && _owner?.CanEditSpots == true)
            _owner.MoveSpotsGesture(ToBase(_hover.Value), ((Vector)(_hover.Value - _press)).Length);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override async void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pointer == null || _owner == null) return;
        var point = e.GetPosition(this);
        _owner.MoveSpotsGesture(ToBase(point), ((Vector)(point - _press)).Length);
        _pointer = null;
        e.Pointer.Capture(null);
        e.Handled = true;
        await _owner.CompleteSpotsGestureAsync();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        Cursor = null;
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        CancelCapture();
    }
}
