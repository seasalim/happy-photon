using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using HappyPhoton.Models;

namespace HappyPhoton.Views;

public partial class CurveView : UserControl
{
    public static readonly StyledProperty<CurveData?> CurveProperty =
        AvaloniaProperty.Register<CurveView, CurveData?>(nameof(Curve));

    public CurveData? Curve
    {
        get => GetValue(CurveProperty);
        set => SetValue(CurveProperty, value);
    }

    public event EventHandler? CurveChanged;
    public event EventHandler? CurveEditStarted;

    private Canvas? _canvas;
    private int _dragPointIndex = -1;
    private int _hoverPointIndex = -1;
    private double _dragX, _dragY; // Normalized position during drag (0-1)
    private double _dragStartCanvasX, _dragStartCanvasY; // Initial canvas position at drag start
    private readonly List<Ellipse> _pointEllipses = new();
    private const double PointRadius = 6;      // Increase for easier targeting
    private const double SelectRadius = 10;    // Increase for easier grabbing

    // Cached brushes and cursors to avoid GC pressure
    private static readonly IBrush ActiveFillBrush = HappyPhotonColors.ControlActive;
    private static readonly IBrush NormalFillBrush = HappyPhotonColors.CurveControlPoint;
    private static readonly IBrush ActiveStrokeBrush = HappyPhotonColors.CurveControlPoint;
    private static readonly IBrush NormalStrokeBrush = HappyPhotonColors.CurveNormalStroke;
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private readonly TranslateTransform _dragTransform = new();

    public CurveView()
    {
        InitializeComponent();
        _canvas = this.FindControl<Canvas>("CurveCanvas");
        UpdateChannelSelectors();

        if (_canvas != null)
        {
            _canvas.PointerPressed += OnCanvasPointerPressed;
            _canvas.PointerMoved += OnCanvasPointerMoved;
            _canvas.PointerReleased += OnCanvasPointerReleased;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == CurveProperty)
        {
            CancelActiveDrag();
            DrawCurve();
        }
        else if (change.Property == ActiveChannelProperty ||
                 change.Property == CompositeCurveProperty ||
                 change.Property == HasRedCurveProperty ||
                 change.Property == HasGreenCurveProperty ||
                 change.Property == HasBlueCurveProperty ||
                 change.Property == AreColorChannelsEnabledProperty)
        {
            if (!AreColorChannelsEnabled &&
                ActiveChannel != ToneCurveChannel.Composite)
            {
                SetCurrentValue(
                    ActiveChannelProperty,
                    ToneCurveChannel.Composite);
            }
            UpdateChannelSelectors();
            DrawCurve();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        DrawCurve();
    }

    private void DrawCurve()
    {
        if (_canvas == null) return;
        _canvas.Children.Clear();

        var width = _canvas.Bounds.Width;
        var height = _canvas.Bounds.Height;

        if (width <= 0 || height <= 0) return;

        // Draw grid lines
        DrawGrid(width, height);

        // Draw diagonal reference line
        var diagLine = new Line
        {
            StartPoint = new Point(0, height),
            EndPoint = new Point(width, 0),
            Stroke = HappyPhotonColors.CurveReferenceLine,
            StrokeThickness = 1,
            StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 4 }
        };
        _canvas.Children.Add(diagLine);

        if (Curve == null) return;

        if (ActiveChannel != ToneCurveChannel.Composite && CompositeCurve != null)
        {
            DrawCurvePath(
                CompositeCurve,
                width,
                height,
                HappyPhotonColors.CurveNormalStroke,
                1.2,
                0.35);
        }
        DrawCurvePath(Curve, width, height, ActiveCurveBrush, 2, 1);

        // Draw control points with hover highlighting
        _pointEllipses.Clear();
        for (int i = 0; i < Curve.Points.Count; i++)
        {
            var point = Curve.Points[i];
            double x = point.X * width;
            double y = height - point.Y * height;

            bool isActive = (i == _hoverPointIndex || i == _dragPointIndex);
            double radius = isActive ? PointRadius * 1.4 : PointRadius;

            var ellipse = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = isActive ? ActiveFillBrush : NormalFillBrush,
                Stroke = isActive ? ActiveStrokeBrush : NormalStrokeBrush,
                StrokeThickness = isActive ? 2 : 1
            };

            Canvas.SetLeft(ellipse, x - radius);
            Canvas.SetTop(ellipse, y - radius);
            _canvas.Children.Add(ellipse);
            _pointEllipses.Add(ellipse);
        }
    }

    private void DrawGrid(double width, double height)
    {
        var gridBrush = HappyPhotonColors.CurveGridLine;

        // Vertical lines at 25%, 50%, 75%
        for (int i = 1; i < 4; i++)
        {
            var line = new Line
            {
                StartPoint = new Point(width * i / 4, 0),
                EndPoint = new Point(width * i / 4, height),
                Stroke = gridBrush,
                StrokeThickness = 1
            };
            _canvas!.Children.Add(line);
        }

        // Horizontal lines at 25%, 50%, 75%
        for (int i = 1; i < 4; i++)
        {
            var line = new Line
            {
                StartPoint = new Point(0, height * i / 4),
                EndPoint = new Point(width, height * i / 4),
                Stroke = gridBrush,
                StrokeThickness = 1
            };
            _canvas!.Children.Add(line);
        }
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_canvas == null || Curve == null) return;

        var pos = e.GetPosition(_canvas);
        var width = _canvas.Bounds.Width;
        var height = _canvas.Bounds.Height;

        if (width <= 0 || height <= 0) return;

        double normX = Math.Clamp(pos.X / width, 0, 1);

        var point = e.GetCurrentPoint(_canvas);

        // Find if there's an existing point within click radius
        int nearestIndex = FindNearestPointWithinRadius(pos, width, height, SelectRadius);

        if (point.Properties.IsRightButtonPressed)
        {
            // Right-click: remove point (if within radius and not an endpoint)
            if (nearestIndex > 0 && nearestIndex < Curve.Points.Count - 1)
            {
                CurveEditStarted?.Invoke(this, EventArgs.Empty);
                Curve.RemovePoint(nearestIndex);
                DrawCurve();
                CurveChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        else if (point.Properties.IsLeftButtonPressed)
        {
            CurveEditStarted?.Invoke(this, EventArgs.Empty);
            if (nearestIndex >= 0)
            {
                // Click is near an existing point - start dragging it
                StartDraggingPoint(nearestIndex, width, height, e);
            }
            else
            {
                // Click anywhere else - add a new point on the curve at this X and start dragging
                double curveY = Curve.GetValueAt(normX);
                int newIndex = Curve.AddPointAndReturnIndex(normX, curveY);
                DrawCurve();
                StartDraggingPoint(newIndex, width, height, e);
            }
        }
    }

    private void StartDraggingPoint(int index, double width, double height, PointerPressedEventArgs e)
    {
        if (Curve == null || _canvas == null) return;

        _dragPointIndex = index;
        _dragX = Curve.Points[index].X;
        _dragY = Curve.Points[index].Y;
        _hoverPointIndex = -1;
        e.Pointer.Capture(_canvas);

        // Store initial canvas position for transform-based dragging
        double radius = PointRadius * 1.4;
        _dragStartCanvasX = _dragX * width - radius;
        _dragStartCanvasY = height - _dragY * height - radius;

        // Update ellipse appearance and apply transform (no layout invalidation)
        if (index >= 0 && index < _pointEllipses.Count)
        {
            var ellipse = _pointEllipses[index];
            ellipse.Width = radius * 2;
            ellipse.Height = radius * 2;
            ellipse.Fill = ActiveFillBrush;
            ellipse.Stroke = ActiveStrokeBrush;
            ellipse.StrokeThickness = 2;
            _dragTransform.X = 0;
            _dragTransform.Y = 0;
            ellipse.RenderTransform = _dragTransform;
        }
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_canvas == null || Curve == null) return;

        var pos = e.GetPosition(_canvas);
        var width = _canvas.Bounds.Width;
        var height = _canvas.Bounds.Height;

        if (_dragPointIndex >= 0 && _dragPointIndex < _pointEllipses.Count)
        {
            // Dragging - use RenderTransform for smooth, layout-free updates
            double normX = Math.Clamp(pos.X / width, 0, 1);
            double normY = Math.Clamp(1 - pos.Y / height, 0, 1);

            // Constrain X within neighbors
            if (_dragPointIndex == 0)
                normX = 0;
            else if (_dragPointIndex == Curve.Points.Count - 1)
                normX = 1;
            else
            {
                double minX = Curve.Points[_dragPointIndex - 1].X + 0.001;
                double maxX = Curve.Points[_dragPointIndex + 1].X - 0.001;
                normX = Math.Clamp(normX, minX, maxX);
            }

            _dragX = normX;
            _dragY = normY;

            // Calculate new canvas position and update transform (bypasses layout)
            double radius = PointRadius * 1.4;
            double newCanvasX = normX * width - radius;
            double newCanvasY = height - normY * height - radius;
            _dragTransform.X = newCanvasX - _dragStartCanvasX;
            _dragTransform.Y = newCanvasY - _dragStartCanvasY;
        }
        else
        {
            // Hover detection - only redraw when hover state changes
            int newHover = FindNearestPointWithinRadius(pos, width, height, SelectRadius);
            if (newHover != _hoverPointIndex)
            {
                _hoverPointIndex = newHover;
                DrawCurve();
            }
            // Always show hand cursor over the curve canvas (can always add points)
            Cursor = HandCursor;
        }
    }

    private void CancelActiveDrag()
    {
        if (_dragPointIndex < 0)
        {
            return;
        }
        _dragPointIndex = -1;
        _dragTransform.X = 0;
        _dragTransform.Y = 0;
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragPointIndex >= 0 && Curve != null)
        {
            // Apply the final position to the curve model
            Curve.MovePoint(_dragPointIndex, _dragX, _dragY);
            _dragPointIndex = -1;
            e.Pointer.Capture(null);
            DrawCurve();
            CurveChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int FindNearestPointWithinRadius(Point pos, double width, double height, double maxRadius)
    {
        if (Curve == null) return -1;

        int nearest = -1;
        double minDist = maxRadius;

        // First, check control points (priority)
        for (int i = 0; i < Curve.Points.Count; i++)
        {
            var p = Curve.Points[i];
            double px = p.X * width;
            double py = (1 - p.Y) * height;
            double dist = Math.Sqrt(Math.Pow(pos.X - px, 2) + Math.Pow(pos.Y - py, 2));

            if (dist < minDist)
            {
                minDist = dist;
                nearest = i;
            }
        }

        return nearest;
    }

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        ResetCurve();
    }

    internal void ResetCurve()
    {
        CurveEditStarted?.Invoke(this, EventArgs.Empty);
        Curve?.Reset();
        _dragPointIndex = -1;
        _hoverPointIndex = -1;
        DrawCurve();
        CurveChanged?.Invoke(this, EventArgs.Empty);
    }
}
