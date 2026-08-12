using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using HappyPhoton.Models;

namespace HappyPhoton.Views;

public partial class CropOverlayControl : UserControl
{
    public static readonly StyledProperty<CropRegion?> CropProperty =
        AvaloniaProperty.Register<CropOverlayControl, CropRegion?>(nameof(Crop));

    public static readonly StyledProperty<Size> ImageSizeProperty =
        AvaloniaProperty.Register<CropOverlayControl, Size>(nameof(ImageSize), new Size(1, 1));

    public static readonly StyledProperty<bool> IsAspectRatioLockedProperty =
        AvaloniaProperty.Register<CropOverlayControl, bool>(nameof(IsAspectRatioLocked));

    public CropRegion? Crop
    {
        get => GetValue(CropProperty);
        set => SetValue(CropProperty, value);
    }

    public Size ImageSize
    {
        get => GetValue(ImageSizeProperty);
        set => SetValue(ImageSizeProperty, value);
    }

    public bool IsAspectRatioLocked
    {
        get => GetValue(IsAspectRatioLockedProperty);
        set => SetValue(IsAspectRatioLockedProperty, value);
    }

    public event EventHandler? CropChanged;

    private Canvas? _canvas;
    private DragHandle _activeDragHandle = DragHandle.None;
    private Point _dragStartPoint;
    private CropRegion? _dragStartCrop;

    // Handle size and hit area
    private const double HandleSize = 10;
    private const double HandleHitArea = 16;
    private const double MinCropSize = 0.05; // 5% minimum

    // Cached brushes
    private static readonly IBrush MaskBrush = HappyPhotonColors.CropMask;
    private static readonly IBrush CropBorderBrush = HappyPhotonColors.CropBorder;
    private static readonly IBrush HandleFillBrush = HappyPhotonColors.CropHandleFill;
    private static readonly IBrush HandleStrokeBrush = HappyPhotonColors.CropHandleStroke;
    private static readonly IBrush GridBrush = HappyPhotonColors.CropGridLine;

    private enum DragHandle
    {
        None,
        TopLeft, TopCenter, TopRight,
        MiddleLeft, Center, MiddleRight,
        BottomLeft, BottomCenter, BottomRight
    }

    public CropOverlayControl()
    {
        InitializeComponent();
        _canvas = this.FindControl<Canvas>("OverlayCanvas");

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

        if (change.Property == CropProperty || change.Property == ImageSizeProperty ||
            change.Property == BoundsProperty)
        {
            DrawOverlay();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        DrawOverlay();
    }

    /// <summary>
    /// Forces the overlay to redraw. Call this after setting Crop programmatically.
    /// </summary>
    public void InvalidateOverlay()
    {
        DrawOverlay();
    }

    private void DrawOverlay()
    {
        if (_canvas == null) return;
        _canvas.Children.Clear();

        var width = _canvas.Bounds.Width;
        var height = _canvas.Bounds.Height;

        if (width <= 0 || height <= 0) return;

        var crop = Crop ?? new CropRegion();

        // Calculate crop rectangle in canvas coordinates
        var cropRect = GetCropRectInCanvas(crop, width, height);

        // Draw darkened mask outside crop region (4 rectangles)
        DrawMask(width, height, cropRect);

        // Draw crop border
        DrawCropBorder(cropRect);

        // Draw rule-of-thirds grid
        DrawGrid(cropRect);

        // Draw resize handles
        DrawHandles(cropRect);
    }

    private Rect GetCropRectInCanvas(CropRegion crop, double canvasWidth, double canvasHeight)
    {
        return new Rect(
            crop.Left * canvasWidth,
            crop.Top * canvasHeight,
            (crop.Right - crop.Left) * canvasWidth,
            (crop.Bottom - crop.Top) * canvasHeight
        );
    }

    private void DrawMask(double width, double height, Rect cropRect)
    {
        // Top mask
        if (cropRect.Top > 0)
        {
            var topMask = new Rectangle
            {
                Width = width,
                Height = cropRect.Top,
                Fill = MaskBrush
            };
            Canvas.SetLeft(topMask, 0);
            Canvas.SetTop(topMask, 0);
            _canvas!.Children.Add(topMask);
        }

        // Bottom mask
        if (cropRect.Bottom < height)
        {
            var bottomMask = new Rectangle
            {
                Width = width,
                Height = height - cropRect.Bottom,
                Fill = MaskBrush
            };
            Canvas.SetLeft(bottomMask, 0);
            Canvas.SetTop(bottomMask, cropRect.Bottom);
            _canvas!.Children.Add(bottomMask);
        }

        // Left mask
        if (cropRect.Left > 0)
        {
            var leftMask = new Rectangle
            {
                Width = cropRect.Left,
                Height = cropRect.Height,
                Fill = MaskBrush
            };
            Canvas.SetLeft(leftMask, 0);
            Canvas.SetTop(leftMask, cropRect.Top);
            _canvas!.Children.Add(leftMask);
        }

        // Right mask
        if (cropRect.Right < width)
        {
            var rightMask = new Rectangle
            {
                Width = width - cropRect.Right,
                Height = cropRect.Height,
                Fill = MaskBrush
            };
            Canvas.SetLeft(rightMask, cropRect.Right);
            Canvas.SetTop(rightMask, cropRect.Top);
            _canvas!.Children.Add(rightMask);
        }
    }

    private void DrawCropBorder(Rect cropRect)
    {
        var border = new Rectangle
        {
            Width = cropRect.Width,
            Height = cropRect.Height,
            Stroke = CropBorderBrush,
            StrokeThickness = 2,
            Fill = Brushes.Transparent
        };
        Canvas.SetLeft(border, cropRect.Left);
        Canvas.SetTop(border, cropRect.Top);
        _canvas!.Children.Add(border);
    }

    private void DrawGrid(Rect cropRect)
    {
        if (cropRect.Width < 30 || cropRect.Height < 30) return;

        // Vertical lines (rule of thirds)
        for (int i = 1; i <= 2; i++)
        {
            var x = cropRect.Left + cropRect.Width * i / 3.0;
            var line = new Line
            {
                StartPoint = new Point(x, cropRect.Top),
                EndPoint = new Point(x, cropRect.Bottom),
                Stroke = GridBrush,
                StrokeThickness = 1
            };
            _canvas!.Children.Add(line);
        }

        // Horizontal lines
        for (int i = 1; i <= 2; i++)
        {
            var y = cropRect.Top + cropRect.Height * i / 3.0;
            var line = new Line
            {
                StartPoint = new Point(cropRect.Left, y),
                EndPoint = new Point(cropRect.Right, y),
                Stroke = GridBrush,
                StrokeThickness = 1
            };
            _canvas!.Children.Add(line);
        }
    }

    private void DrawHandles(Rect cropRect)
    {
        // Corner handles
        DrawHandle(cropRect.Left, cropRect.Top);
        DrawHandle(cropRect.Right, cropRect.Top);
        DrawHandle(cropRect.Left, cropRect.Bottom);
        DrawHandle(cropRect.Right, cropRect.Bottom);

        // Edge center handles
        DrawHandle(cropRect.Left + cropRect.Width / 2, cropRect.Top);
        DrawHandle(cropRect.Left + cropRect.Width / 2, cropRect.Bottom);
        DrawHandle(cropRect.Left, cropRect.Top + cropRect.Height / 2);
        DrawHandle(cropRect.Right, cropRect.Top + cropRect.Height / 2);
    }

    private void DrawHandle(double x, double y)
    {
        var handle = new Rectangle
        {
            Width = HandleSize,
            Height = HandleSize,
            Fill = HandleFillBrush,
            Stroke = HandleStrokeBrush,
            StrokeThickness = 1
        };
        Canvas.SetLeft(handle, x - HandleSize / 2);
        Canvas.SetTop(handle, y - HandleSize / 2);
        _canvas!.Children.Add(handle);
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Crop == null || _canvas == null) return;

        var point = e.GetPosition(_canvas);
        var width = _canvas.Bounds.Width;
        var height = _canvas.Bounds.Height;

        if (width <= 0 || height <= 0) return;

        var cropRect = GetCropRectInCanvas(Crop, width, height);

        // Determine which handle (if any) was clicked
        _activeDragHandle = GetHandleAtPoint(point, cropRect);

        if (_activeDragHandle != DragHandle.None)
        {
            _dragStartPoint = point;
            _dragStartCrop = Crop.Clone();
            e.Handled = true;
        }
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_canvas == null) return;

        var point = e.GetPosition(_canvas);
        var width = _canvas.Bounds.Width;
        var height = _canvas.Bounds.Height;

        if (width <= 0 || height <= 0) return;

        // Update cursor based on handle hover
        if (_activeDragHandle == DragHandle.None && Crop != null)
        {
            var cropRect = GetCropRectInCanvas(Crop, width, height);
            var handle = GetHandleAtPoint(point, cropRect);
            Cursor = GetCursorForHandle(handle);
        }

        // Handle dragging
        if (_activeDragHandle != DragHandle.None && _dragStartCrop != null && Crop != null)
        {
            var deltaX = (point.X - _dragStartPoint.X) / width;
            var deltaY = (point.Y - _dragStartPoint.Y) / height;

            ApplyDrag(_activeDragHandle, deltaX, deltaY);
            DrawOverlay();
            e.Handled = true;
        }
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_activeDragHandle != DragHandle.None)
        {
            _activeDragHandle = DragHandle.None;
            _dragStartCrop = null;
            CropChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private DragHandle GetHandleAtPoint(Point point, Rect cropRect)
    {
        bool nearLeft = Math.Abs(point.X - cropRect.Left) < HandleHitArea;
        bool nearRight = Math.Abs(point.X - cropRect.Right) < HandleHitArea;
        bool nearTop = Math.Abs(point.Y - cropRect.Top) < HandleHitArea;
        bool nearBottom = Math.Abs(point.Y - cropRect.Bottom) < HandleHitArea;
        bool nearCenterX = Math.Abs(point.X - (cropRect.Left + cropRect.Width / 2)) < HandleHitArea;
        bool nearCenterY = Math.Abs(point.Y - (cropRect.Top + cropRect.Height / 2)) < HandleHitArea;

        // Corners
        if (nearLeft && nearTop) return DragHandle.TopLeft;
        if (nearRight && nearTop) return DragHandle.TopRight;
        if (nearLeft && nearBottom) return DragHandle.BottomLeft;
        if (nearRight && nearBottom) return DragHandle.BottomRight;

        // Edge centers
        if (nearCenterX && nearTop) return DragHandle.TopCenter;
        if (nearCenterX && nearBottom) return DragHandle.BottomCenter;
        if (nearLeft && nearCenterY) return DragHandle.MiddleLeft;
        if (nearRight && nearCenterY) return DragHandle.MiddleRight;

        // Inside crop area = drag entire region
        if (point.X >= cropRect.Left && point.X <= cropRect.Right &&
            point.Y >= cropRect.Top && point.Y <= cropRect.Bottom)
        {
            return DragHandle.Center;
        }

        return DragHandle.None;
    }

    private Cursor GetCursorForHandle(DragHandle handle) => handle switch
    {
        DragHandle.TopLeft or DragHandle.BottomRight => new Cursor(StandardCursorType.TopLeftCorner),
        DragHandle.TopRight or DragHandle.BottomLeft => new Cursor(StandardCursorType.TopRightCorner),
        DragHandle.TopCenter or DragHandle.BottomCenter => new Cursor(StandardCursorType.SizeNorthSouth),
        DragHandle.MiddleLeft or DragHandle.MiddleRight => new Cursor(StandardCursorType.SizeWestEast),
        DragHandle.Center => new Cursor(StandardCursorType.SizeAll),
        _ => Cursor.Default
    };

    private void ApplyDrag(DragHandle handle, double deltaX, double deltaY)
    {
        if (Crop == null || _dragStartCrop == null) return;

        if (IsAspectRatioLocked && handle != DragHandle.Center)
        {
            ApplyLockedAspectDrag(handle, deltaX, deltaY);
            return;
        }

        switch (handle)
        {
            case DragHandle.TopLeft:
                Crop.Left = Clamp(_dragStartCrop.Left + deltaX, 0, Crop.Right - MinCropSize);
                Crop.Top = Clamp(_dragStartCrop.Top + deltaY, 0, Crop.Bottom - MinCropSize);
                break;
            case DragHandle.TopCenter:
                Crop.Top = Clamp(_dragStartCrop.Top + deltaY, 0, Crop.Bottom - MinCropSize);
                break;
            case DragHandle.TopRight:
                Crop.Right = Clamp(_dragStartCrop.Right + deltaX, Crop.Left + MinCropSize, 1);
                Crop.Top = Clamp(_dragStartCrop.Top + deltaY, 0, Crop.Bottom - MinCropSize);
                break;
            case DragHandle.MiddleLeft:
                Crop.Left = Clamp(_dragStartCrop.Left + deltaX, 0, Crop.Right - MinCropSize);
                break;
            case DragHandle.Center:
                // Move entire crop region
                var newLeft = _dragStartCrop.Left + deltaX;
                var newTop = _dragStartCrop.Top + deltaY;
                var cropWidth = _dragStartCrop.Right - _dragStartCrop.Left;
                var cropHeight = _dragStartCrop.Bottom - _dragStartCrop.Top;

                // Constrain to bounds
                newLeft = Clamp(newLeft, 0, 1 - cropWidth);
                newTop = Clamp(newTop, 0, 1 - cropHeight);

                Crop.Left = newLeft;
                Crop.Top = newTop;
                Crop.Right = newLeft + cropWidth;
                Crop.Bottom = newTop + cropHeight;
                break;
            case DragHandle.MiddleRight:
                Crop.Right = Clamp(_dragStartCrop.Right + deltaX, Crop.Left + MinCropSize, 1);
                break;
            case DragHandle.BottomLeft:
                Crop.Left = Clamp(_dragStartCrop.Left + deltaX, 0, Crop.Right - MinCropSize);
                Crop.Bottom = Clamp(_dragStartCrop.Bottom + deltaY, Crop.Top + MinCropSize, 1);
                break;
            case DragHandle.BottomCenter:
                Crop.Bottom = Clamp(_dragStartCrop.Bottom + deltaY, Crop.Top + MinCropSize, 1);
                break;
            case DragHandle.BottomRight:
                Crop.Right = Clamp(_dragStartCrop.Right + deltaX, Crop.Left + MinCropSize, 1);
                Crop.Bottom = Clamp(_dragStartCrop.Bottom + deltaY, Crop.Top + MinCropSize, 1);
                break;
        }
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }
}
