using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using HappyPhoton.Models;

namespace HappyPhoton.Views;

public sealed partial class LocalsOverlayControl
{
    private Point? _brushHover;
    private static readonly Cursor HiddenBrushCursor = new(StandardCursorType.None);
    private static readonly Pen CursorOutline = new(HappyPhotonColors.CropHandleStroke, 3);
    internal double BrushScreenRadius => _owner?.LocalsFrame is { } frame
        ? _owner.BrushRadius * ScreenLongEdge(frame) : 0;
    private double ScreenLongEdge(LocalsFrame frame) => Bounds.Width / frame.CropWidth * frame.LongEdge / frame.Width;
    private Point BrushPoint(LocalBrushPoint point, LocalsFrame frame) => new(
        (point.U / (double)LocalBrushPoint.Scale - frame.CropX) / frame.CropWidth * Bounds.Width,
        (point.V / (double)LocalBrushPoint.Scale - frame.CropY) / frame.CropHeight * Bounds.Height);
    internal void UpdateBrushHover(Point? point)
    {
        var hadHover = _brushHover != null;
        _brushHover = point is { } p && new Rect(Bounds.Size).Contains(p) ? point : null;
        UpdateBrushCursor();
        if (_owner is { IsBrushSectionVisible: true, IsLocalHuePicking: false } || hadHover != (_brushHover != null))
            InvalidateVisual();
    }
    private void UpdateBrushCursor() => Cursor = _owner?.IsLocalHuePicking == true
        ? new Cursor(StandardCursorType.Cross)
        : _owner is { CanEditLocals: true, IsBrushSectionVisible: true } && _brushHover != null
            ? HiddenBrushCursor : Cursor.Default;
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        UpdateBrushHover(null);
    }
    internal LocalAdjustment? HitPin(Point point, LocalsFrame frame) => _owner?.Locals.FirstOrDefault(local =>
        !(local.IsBrush && local.Id == _owner.SelectedLocal?.Id) &&
        (!local.IsBrush || local.Strokes is { Count: > 0 }) &&
        ((Vector)(point - ToCanvas(local, frame, Bounds.Size))).Length <= 10);

    private void DrawBrush(DrawingContext context, LocalsFrame frame)
    {
        if (_owner?.LiveBrushStroke is { } stroke)
        {
            var path = new StreamGeometry();
            using (var shape = path.Open())
            {
                shape.BeginFigure(BrushPoint(stroke.Points[0], frame), false);
                foreach (var point in stroke.Points.Skip(1)) shape.LineTo(BrushPoint(point, frame));
                // A zero-length round-capped segment is a dab.
                if (stroke.Points.Count == 1) shape.LineTo(BrushPoint(stroke.Points[0], frame));
                shape.EndFigure(false);
            }
            // Erase draws a translucent white band rather than an outline. Stroking a widened polyline
            // traces every segment, cap and join, and merging those contours costs ~150 ms per move
            // on long strokes; a thick pen fills the stroke's envelope once, like the paint ribbon.
            var erase = stroke.Mode == "erase";
            var pen = new Pen(erase ? HappyPhotonColors.CropBorder : new SolidColorBrush(HappyPhotonColors.LocalMaskColor),
                2 * stroke.Radius * ScreenLongEdge(frame), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            using (context.PushOpacity((erase ? .3 : .35) * stroke.Flow)) context.DrawGeometry(null, pen, path);
        }
        if (_owner is not { IsBrushSectionVisible: true, IsLocalHuePicking: false } vm || _brushHover is not { } center) return;
        var radius = BrushScreenRadius;
        foreach (var pen in new[] { CursorOutline, Guide })
        {
            if (radius >= 4)
            {
                context.DrawEllipse(null, pen, center, radius, radius);
                var inner = radius * (1 - vm.BrushFeather / 100);
                if (inner > 0) context.DrawEllipse(null, pen, center, inner, inner);
            }
            context.DrawLine(pen, center - new Vector(4, 0), center + new Vector(4, 0));
            if (vm.IsBrushPaint || radius < 4) context.DrawLine(pen, center - new Vector(0, 4), center + new Vector(0, 4));
        }
    }
}
