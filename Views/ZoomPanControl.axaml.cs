using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;

namespace HappyPhoton.Views;

public partial class ZoomPanControl : UserControl
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<ZoomPanControl, Bitmap?>(nameof(Source));

    public static readonly StyledProperty<double> ZoomLevelProperty =
        AvaloniaProperty.Register<ZoomPanControl, double>(nameof(ZoomLevel), 1.0);

    public static readonly StyledProperty<bool> AutoFitProperty =
        AvaloniaProperty.Register<ZoomPanControl, bool>(nameof(AutoFit), true);

    public static readonly StyledProperty<bool> IsCropModeProperty =
        AvaloniaProperty.Register<ZoomPanControl, bool>(nameof(IsCropMode), false);

    public static readonly StyledProperty<CropRegion?> CropProperty =
        AvaloniaProperty.Register<ZoomPanControl, CropRegion?>(nameof(Crop));

    public static readonly StyledProperty<bool> IsCropAspectLockedProperty =
        AvaloniaProperty.Register<ZoomPanControl, bool>(nameof(IsCropAspectLocked));

    public static readonly StyledProperty<ScrollBarVisibility> ScrollBarVisibilityProperty =
        AvaloniaProperty.Register<ZoomPanControl, ScrollBarVisibility>(
            nameof(ScrollBarVisibility),
            ScrollBarVisibility.Auto);

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public double ZoomLevel
    {
        get => GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    public bool AutoFit
    {
        get => GetValue(AutoFitProperty);
        set => SetValue(AutoFitProperty, value);
    }

    public bool IsCropMode
    {
        get => GetValue(IsCropModeProperty);
        set => SetValue(IsCropModeProperty, value);
    }

    public CropRegion? Crop
    {
        get => GetValue(CropProperty);
        set => SetValue(CropProperty, value);
    }

    public bool IsCropAspectLocked
    {
        get => GetValue(IsCropAspectLockedProperty);
        set => SetValue(IsCropAspectLockedProperty, value);
    }

    public ScrollBarVisibility ScrollBarVisibility
    {
        get => GetValue(ScrollBarVisibilityProperty);
        set => SetValue(ScrollBarVisibilityProperty, value);
    }

    public event EventHandler<double>? ZoomChanged;
    public event EventHandler<double>? AutoFitRequested;

    private Image? _imageControl;
    private ScrollViewer? _scrollViewer;
    private CropOverlayControl? _cropOverlay;
    private Point _lastPanPoint;
    private bool _isPanning;

    public ZoomPanControl()
    {
        InitializeComponent();

        _imageControl = this.FindControl<Image>("ImageControl");
        _scrollViewer = this.FindControl<ScrollViewer>("ScrollViewer");
        _cropOverlay = this.FindControl<CropOverlayControl>("CropOverlay");

        AddHandler(
            PointerWheelChangedEvent,
            OnPreviewPointerWheelChanged,
            RoutingStrategies.Tunnel);

        UpdateScrollBarVisibility();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SourceProperty)
        {
            if (_imageControl != null)
            {
                _imageControl.Source = Source;
            }
            UpdateImageSize();
        }
        else if (change.Property == ZoomLevelProperty)
        {
            UpdateImageSize();
        }
        else if (change.Property == IsCropModeProperty)
        {
            UpdateCropOverlayVisibility();
        }
        else if (change.Property == CropProperty)
        {
            UpdateCropOverlay();
        }
        else if (change.Property == IsCropAspectLockedProperty)
        {
            UpdateCropOverlay();
        }
        else if (change.Property == ScrollBarVisibilityProperty)
        {
            UpdateScrollBarVisibility();
        }
    }

    private void UpdateScrollBarVisibility()
    {
        if (_scrollViewer == null) return;

        _scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility;
        _scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility;
    }

    private void UpdateImageSize()
    {
        if (_imageControl == null || Source == null) return;

        // Set explicit size based on source image and zoom level
        _imageControl.Width = Source.PixelSize.Width * ZoomLevel;
        _imageControl.Height = Source.PixelSize.Height * ZoomLevel;

        // Update crop overlay size to match
        UpdateCropOverlaySize();
    }

    private void UpdateCropOverlayVisibility()
    {
        if (_cropOverlay == null) return;
        _cropOverlay.IsVisible = IsCropMode;
        if (IsCropMode)
        {
            UpdateCropOverlay();
        }
    }

    private void UpdateCropOverlay()
    {
        if (_cropOverlay == null) return;
        _cropOverlay.Crop = Crop;
        _cropOverlay.IsAspectRatioLocked = IsCropAspectLocked;
        UpdateCropOverlaySize();
        _cropOverlay.InvalidateOverlay();
    }

    private void UpdateCropOverlaySize()
    {
        if (_cropOverlay == null || _imageControl == null || Source == null) return;

        // Match overlay size to image display size
        _cropOverlay.Width = _imageControl.Width;
        _cropOverlay.Height = _imageControl.Height;
        _cropOverlay.ImageSize = new Size(Source.PixelSize.Width, Source.PixelSize.Height);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        if (AutoFit && Source != null && _scrollViewer != null &&
            _scrollViewer.Viewport.Width > 0 && _scrollViewer.Viewport.Height > 0)
        {
            var fitZoom = GetFitZoomLevel();
            AutoFitRequested?.Invoke(this, fitZoom);
        }
    }

    public double GetFitZoomLevel()
    {
        if (Source == null || _scrollViewer == null) return 1.0;

        var viewportWidth = _scrollViewer.Viewport.Width;
        var viewportHeight = _scrollViewer.Viewport.Height;

        if (viewportWidth <= 0 || viewportHeight <= 0) return 1.0;

        var imageWidth = Source.PixelSize.Width;
        var imageHeight = Source.PixelSize.Height;

        if (imageWidth <= 0 || imageHeight <= 0) return 1.0;

        // Calculate scale to fit both dimensions
        var scaleX = viewportWidth / imageWidth;
        var scaleY = viewportHeight / imageHeight;

        // Use the smaller scale to ensure the image fits entirely
        return Math.Min(scaleX, scaleY);
    }

    /// <summary>
    /// Requests a fit-to-view zoom. If the viewport isn't ready yet,
    /// waits for layout to complete before calculating the fit zoom.
    /// </summary>
    public void RequestFitToView(Action<double> applyZoom)
    {
        var fitZoom = GetFitZoomLevel();
        
        // If viewport is valid (not returning default 1.0 due to zero dimensions), apply immediately
        if (_scrollViewer != null && _scrollViewer.Viewport.Width > 0 && _scrollViewer.Viewport.Height > 0)
        {
            applyZoom(fitZoom);
            return;
        }

        // Viewport not ready - wait for layout
        void OnLayoutUpdated(object? sender, EventArgs e)
        {
            if (_scrollViewer != null && _scrollViewer.Viewport.Width > 0 && _scrollViewer.Viewport.Height > 0)
            {
                LayoutUpdated -= OnLayoutUpdated;
                applyZoom(GetFitZoomLevel());
            }
        }

        LayoutUpdated += OnLayoutUpdated;
    }

    private void OnPreviewPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var delta = e.Delta.Y;
        ZoomChanged?.Invoke(this, delta);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsMiddleButtonPressed ||
            (point.Properties.IsLeftButtonPressed && ZoomLevel > 1.0))
        {
            _isPanning = true;
            _lastPanPoint = e.GetPosition(this);
            e.Pointer.Capture(this);
            Cursor = new Cursor(StandardCursorType.Hand);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_isPanning && _scrollViewer != null)
        {
            var currentPoint = e.GetPosition(this);
            var delta = _lastPanPoint - currentPoint;

            _scrollViewer.Offset = new Vector(
                _scrollViewer.Offset.X + delta.X,
                _scrollViewer.Offset.Y + delta.Y);

            _lastPanPoint = currentPoint;
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isPanning)
        {
            _isPanning = false;
            e.Pointer.Capture(null);
            Cursor = Cursor.Default;
            e.Handled = true;
        }
    }
}
