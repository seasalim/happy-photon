using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using HappyPhoton.Models;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public sealed class LocalsOverlayControl : Control
{
    private MainWindowViewModel? _owner;
    private Point _press;
    private IPointer? _pointer;
    private static readonly Pen Guide = new(HappyPhotonColors.CropBorder, 1);
    private static readonly Pen Subdued = new(HappyPhotonColors.CropHandleStroke, 1);

    public LocalsOverlayControl()
    {
        ClipToBounds = true;
        Focusable = true;
        DataContextChanged += (_, _) => BindOwner();
        DetachedFromVisualTree += (_, _) =>
        {
            CancelCapture();
            if (_owner != null) _owner.PropertyChanged -= OnOwnerChanged;
            _owner = null;
        };
        AttachedToVisualTree += (_, _) => BindOwner();
    }

    private void BindOwner()
    {
        if (_owner != null) _owner.PropertyChanged -= OnOwnerChanged;
        _owner = DataContext as MainWindowViewModel;
        if (_owner != null) _owner.PropertyChanged += OnOwnerChanged;
        Refresh();
    }
    private void OnOwnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.Locals) or
            nameof(MainWindowViewModel.CanEditLocals) or nameof(MainWindowViewModel.IsLocalMaskVisible))
            Refresh();
    }
    private void Refresh()
    {
        IsVisible = _owner?.CanEditLocals == true;
        if (_owner?.IsLocalsGestureActive != true && _pointer != null) CancelCapture();
        InvalidateVisual();
    }
    private void CancelCapture()
    {
        var pointer = _pointer;
        _pointer = null;
        if (pointer != null)
        {
            _owner?.DiscardLocalsGesture();
            pointer.Capture(null);
        }
    }

    internal static Point ToCanvas(LocalAdjustment local, LocalsFrame frame, Size size) =>
        new((local.Cu - frame.CropX) / frame.CropWidth * size.Width,
            (local.Cv - frame.CropY) / frame.CropHeight * size.Height);
    private Point Normalize(Point point, LocalsFrame frame) => new(
        frame.CropX + point.X / Bounds.Width * frame.CropWidth,
        frame.CropY + point.Y / Bounds.Height * frame.CropHeight);
    private static Vector Direction(LocalAdjustment local, LocalsFrame frame, Size size)
    {
        var angle = local.Angle * Math.PI / 180;
        return new(Math.Cos(angle) * frame.LongEdge / frame.Width / frame.CropWidth * size.Width,
            Math.Sin(angle) * frame.LongEdge / frame.Height / frame.CropHeight * size.Height);
    }
    internal static GradientBrush BuildMaskBrush(LocalAdjustment local, LocalsFrame frame, Size size)
    {
        if (local.IsRadial) return BuildRadialBrush(local, frame, size);
        var center = ToCanvas(local, frame, size);
        var direction = Direction(local, frame, size);
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(center - direction * (local.Feather / 2), RelativeUnit.Absolute),
            EndPoint = new RelativePoint(center + direction * (local.Feather / 2), RelativeUnit.Absolute),
            Opacity = .35
        };
        for (var i = 0; i <= 256; i++)
        {
            var t = i / 256d;
            var color = HappyPhotonColors.LocalMaskColor;
            brush.GradientStops.Add(new GradientStop(new Color(
                (byte)Math.Round(255 * (1 - t * t * (3 - 2 * t))), color.R, color.G, color.B), t));
        }
        return brush;
    }

    private static Matrix RadialTransform(LocalAdjustment local, LocalsFrame frame, Size size)
    {
        var c = ToCanvas(local, frame, size);
        var x = Direction(local, frame, size) * local.Rx;
        var y = Direction(local with { Angle = local.Angle + 90 }, frame, size) * local.Ry;
        return new Matrix(x.X, x.Y, y.X, y.Y, c.X, c.Y);
    }

    private static RadialGradientBrush BuildRadialBrush(LocalAdjustment local, LocalsFrame frame, Size size)
    {
        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(0, 0, RelativeUnit.Absolute),
            GradientOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute),
            RadiusX = new RelativeScalar(1, RelativeUnit.Absolute),
            RadiusY = new RelativeScalar(1, RelativeUnit.Absolute),
            TransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute),
            Transform = new MatrixTransform(RadialTransform(local, frame, size)), Opacity = .35
        };
        var color = HappyPhotonColors.LocalMaskColor;
        for (var i = 0; i <= 256; i++)
        {
            var t = i / 256d;
            var weight = 1 - t * t * (3 - 2 * t);
            brush.GradientStops.Add(new GradientStop(new Color(
                (byte)Math.Round(255 * (local.Outside ? 1 - weight : weight)), color.R, color.G, color.B),
                1 - local.Feather + t * local.Feather));
        }
        return brush;
    }

    private static readonly (LocalHandle Handle, Point Point)[] RadialKnobs =
    [ (LocalHandle.AxisXPositive, new(1, 0)), (LocalHandle.AxisXNegative, new(-1, 0)),
      (LocalHandle.AxisYPositive, new(0, 1)), (LocalHandle.AxisYNegative, new(0, -1)) ];

    private void DrawRadial(DrawingContext context, LocalAdjustment local, LocalsFrame frame)
    {
        var matrix = RadialTransform(local, frame, Bounds.Size);
        foreach (var scale in new[] { 1d, 1 - local.Feather })
        {
            var ellipse = new StreamGeometry();
            using (var shape = ellipse.Open())
            {
                shape.BeginFigure(new Point(scale, 0).Transform(matrix), false);
                for (var i = 1; i < 128; i++)
                    shape.LineTo(new Point(scale * Math.Cos(i * Math.PI / 64), scale * Math.Sin(i * Math.PI / 64)).Transform(matrix));
                shape.EndFigure(true);
            }
            context.DrawGeometry(null, Guide, ellipse);
        }
        var rotation = new Point(1 + .08 / local.Rx, 0).Transform(matrix);
        context.DrawLine(Guide, new Point(1, 0).Transform(matrix), rotation);
        foreach (var knob in RadialKnobs)
            context.DrawEllipse(HappyPhotonColors.CropHandleFill, Guide, knob.Point.Transform(matrix), 4, 4);
        context.DrawEllipse(null, Guide, rotation, 5, 5);
        context.DrawEllipse(HappyPhotonColors.CropHandleFill, Subdued, ToCanvas(local, frame, Bounds.Size), 7, 7);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_owner is not { CanEditLocals: true } vm || vm.LocalsFrame is not { } frame) return;
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        var selected = vm.SelectedLocal;
        if (vm.IsLocalMaskVisible && selected != null)
            context.DrawRectangle(BuildMaskBrush(selected, frame, Bounds.Size), null, new Rect(Bounds.Size));
        foreach (var local in vm.Locals)
        {
            var center = ToCanvas(local, frame, Bounds.Size);
            using (context.PushOpacity(.5))
                context.DrawEllipse(HappyPhotonColors.CropHandleFill, Subdued, center, 3, 3);
        }
        if (selected == null) return;
        if (selected.IsRadial) { DrawRadial(context, selected, frame); return; }
        var c = ToCanvas(selected, frame, Bounds.Size);
        var d = Direction(selected, frame, Bounds.Size);
        var rail = new Vector(-d.Y, d.X).Normalize() * (Bounds.Width + Bounds.Height);
        var full = c - d * (selected.Feather / 2);
        var empty = c + d * (selected.Feather / 2);
        var directionPoint = c + d * (selected.Feather / 2 + .08);
        foreach (var p in new[] { c, full, empty }) context.DrawLine(Guide, p - rail, p + rail);
        context.DrawLine(Guide, full, directionPoint);
        context.DrawEllipse(HappyPhotonColors.CropHandleFill, Subdued, c, 7, 7);
        foreach (var p in new[] { full, empty })
            context.DrawEllipse(Brushes.Transparent, Guide, p, 4, 4);
        var unit = d.Normalize();
        var across = new Vector(-unit.Y, unit.X);
        var triangle = new StreamGeometry();
        using (var shape = triangle.Open())
        {
            shape.BeginFigure(directionPoint + unit * 7, true);
            shape.LineTo(directionPoint - unit * 5 + across * 5);
            shape.LineTo(directionPoint - unit * 5 - across * 5);
            shape.EndFigure(true);
        }
        context.DrawGeometry(HappyPhotonColors.CropHandleFill, Subdued, triangle);
        var marker = full + across * 16;
        context.DrawLine(new Pen(HappyPhotonColors.CropHandleStroke, 6), marker - across * 6, marker + across * 6);
        context.DrawLine(new Pen(HappyPhotonColors.CropHandleFill, 3), marker - across * 6, marker + across * 6);
    }

    internal LocalHandle? HitHandle(Point point, LocalAdjustment local, LocalsFrame frame)
    {
        if (local.IsRadial)
        {
            var matrix = RadialTransform(local, frame, Bounds.Size);
            foreach (var knob in RadialKnobs)
                if (((Vector)(point - knob.Point.Transform(matrix))).Length <= 10) return knob.Handle;
            if (((Vector)(point - new Point(1 + .08 / local.Rx, 0).Transform(matrix))).Length <= 10) return LocalHandle.Rotation;
            if (((Vector)(point - ToCanvas(local, frame, Bounds.Size))).Length <= 10) return LocalHandle.Center;
            var unitPoint = point.Transform(matrix.Invert());
            var rho = ((Vector)unitPoint).Length;
            if (rho > 0 && ((Vector)(point - new Point(unitPoint.X / rho * (1 - local.Feather),
                    unitPoint.Y / rho * (1 - local.Feather)).Transform(matrix))).Length <= 6)
                return LocalHandle.FeatherRing;
            return null;
        }
        var c = ToCanvas(local, frame, Bounds.Size);
        var d = Direction(local, frame, Bounds.Size);
        if (((Vector)(point - c)).Length <= 10) return LocalHandle.Center;
        if (((Vector)(point - (c + d * (local.Feather / 2 + .08)))).Length <= 10) return LocalHandle.Direction;
        var unit = d.Normalize();
        var distance = Math.Abs(Vector.Dot(point - c, unit));
        return Math.Abs(distance - d.Length * local.Feather / 2) <= 6 ? LocalHandle.Feather : null;
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_owner is not { CanEditLocals: true } vm || vm.LocalsFrame is not { } frame ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        var handle = vm.IsLocalCreationArmed ? LocalHandle.Create : vm.SelectedLocal is { } selected
            ? HitHandle(p, selected, frame) : null;
        if (handle == null)
        {
            var pin = vm.Locals.FirstOrDefault(local => ((Vector)(p - ToCanvas(local, frame, Bounds.Size))).Length <= 10);
            if (pin != null) vm.SelectedLocal = pin;
        }
        else if (vm.BeginLocalsGesture(handle.Value, Normalize(p, frame)))
        {
            _press = p;
            _pointer = e.Pointer;
            e.Pointer.Capture(this);
        }
        Focus();
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_pointer == null || _owner?.LocalsFrame is not { } frame) return;
        var p = e.GetPosition(this);
        _owner.MoveLocalsGesture(Normalize(p, frame), ((Vector)(p - _press)).Length);
        e.Handled = true;
    }
    protected override async void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pointer == null || _owner == null) return;
        _pointer = null;
        e.Pointer.Capture(null);
        e.Handled = true;
        await _owner.CompleteLocalsGestureAsync();
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        CancelCapture();
    }
}
