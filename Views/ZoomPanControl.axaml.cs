using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;

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

    public static readonly StyledProperty<bool> IsWhiteBalancePickingProperty =
        AvaloniaProperty.Register<ZoomPanControl, bool>(
            nameof(IsWhiteBalancePicking),
            false);

    public static readonly StyledProperty<CropRegion?> CropProperty =
        AvaloniaProperty.Register<ZoomPanControl, CropRegion?>(nameof(Crop));

    public static readonly StyledProperty<bool> IsCropAspectLockedProperty =
        AvaloniaProperty.Register<ZoomPanControl, bool>(nameof(IsCropAspectLocked));

    public static readonly StyledProperty<ScrollBarVisibility> ScrollBarVisibilityProperty =
        AvaloniaProperty.Register<ZoomPanControl, ScrollBarVisibility>(
            nameof(ScrollBarVisibility),
            ScrollBarVisibility.Auto);

    public static readonly StyledProperty<bool> IsColorAssessmentProperty =
        AvaloniaProperty.Register<ZoomPanControl, bool>(nameof(IsColorAssessment));

    public static readonly StyledProperty<Thickness> ContentInsetProperty =
        AvaloniaProperty.Register<ZoomPanControl, Thickness>(nameof(ContentInset));

    public static readonly StyledProperty<bool> IsDisplayTraceActiveProperty =
        AvaloniaProperty.Register<ZoomPanControl, bool>(
            nameof(IsDisplayTraceActive));

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

    public bool IsWhiteBalancePicking
    {
        get => GetValue(IsWhiteBalancePickingProperty);
        set => SetValue(IsWhiteBalancePickingProperty, value);
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

    public bool IsColorAssessment
    {
        get => GetValue(IsColorAssessmentProperty);
        set => SetValue(IsColorAssessmentProperty, value);
    }

    public Thickness ContentInset
    {
        get => GetValue(ContentInsetProperty);
        set => SetValue(ContentInsetProperty, value);
    }

    public bool IsDisplayTraceActive
    {
        get => GetValue(IsDisplayTraceActiveProperty);
        set => SetValue(IsDisplayTraceActiveProperty, value);
    }

    public event EventHandler<double>? ZoomChanged;
    public event EventHandler<double>? AutoFitRequested;
    public event EventHandler<(double X, double Y)>? WhiteBalancePickRequested;

    private Image? _imageControl;
    private ScrollViewer? _scrollViewer;
    private CropOverlayControl? _cropOverlay;
    private Panel? _surroundLayer;
    private Border? _assessmentMat;
    private Point _lastPanPoint;
    private Point _pressPoint;
    private bool _isPanning;
    private bool _isWhiteBalanceGesture;
    private bool _pointerMoved;
    private readonly DisplayChainTrace? _displayChainTrace;

    public ZoomPanControl()
    {
        InitializeComponent();

        _imageControl = this.FindControl<Image>("ImageControl");
        _scrollViewer = this.FindControl<ScrollViewer>("ScrollViewer");
        _cropOverlay = this.FindControl<CropOverlayControl>("CropOverlay");
        _surroundLayer = this.FindControl<Panel>("SurroundLayer");
        _assessmentMat = this.FindControl<Border>("AssessmentMat");
        InitializeVisibleRegionTracking();

        AddHandler(
            PointerWheelChangedEvent,
            OnPreviewPointerWheelChanged,
            RoutingStrategies.Tunnel);

        UpdateScrollBarVisibility();
        ApplyColorAssessment();
        if (ImageServiceHelpers.DisplayTraceLoggingEnabled)
        {
            _displayChainTrace = new DisplayChainTrace(
                this,
                _imageControl!,
                _scrollViewer!);
        }
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
            ApplyColorAssessment();
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
        else if (change.Property == IsWhiteBalancePickingProperty)
        {
            Cursor = IsWhiteBalancePicking
                ? new Cursor(StandardCursorType.Cross)
                : Cursor.Default;
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
        else if (change.Property == IsColorAssessmentProperty)
        {
            ApplyColorAssessment();
            RequestAutoFit();
        }

        if (change.Property == SourceProperty ||
            change.Property == ZoomLevelProperty ||
            change.Property == IsDisplayTraceActiveProperty)
        {
            _displayChainTrace?.OnInputChanged();
        }
        OnViewportGeometryInputChanged(change.Property);
    }

    private void ApplyColorAssessment()
    {
        if (_surroundLayer == null || _assessmentMat == null)
        {
            return;
        }

        var geometry = GetColorAssessmentGeometry();
        var showField = Source != null && geometry.IsFieldVisible;
        _surroundLayer.Classes.Set("assessment-on", showField);
        _assessmentMat.Classes.Set("assessment-on", showField);
        _assessmentMat.Padding = new Thickness(showField ? geometry.BandWidth : 0);
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

        ApplyColorAssessment();
        RequestAutoFit();
        RequestVisibleRegionPublication();
        _displayChainTrace?.OnInputChanged();
    }

    private void RequestAutoFit()
    {
        var fitBox = GetColorAssessmentGeometry().FitBox;
        if (AutoFit && Source != null &&
            fitBox.Width > 0 && fitBox.Height > 0)
        {
            AutoFitRequested?.Invoke(this, GetFitZoomLevel());
        }
    }

    public double GetFitZoomLevel()
    {
        if (Source == null || _scrollViewer == null) return 1.0;

        var fitBox = GetColorAssessmentGeometry().FitBox;
        var viewportWidth = fitBox.Width;
        var viewportHeight = fitBox.Height;

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

    private ColorAssessmentGeometry GetColorAssessmentGeometry() =>
        _scrollViewer == null
            ? default
            : ColorAssessmentGeometry.Calculate(
                _scrollViewer.Viewport,
                IsColorAssessment);

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

        if ((point.Properties.IsMiddleButtonPressed ||
             point.Properties.IsLeftButtonPressed) &&
            CanPanContent())
        {
            _isPanning = true;
            _lastPanPoint = e.GetPosition(this);
            e.Pointer.Capture(this);
            Cursor = new Cursor(StandardCursorType.Hand);
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

        if (_isWhiteBalanceGesture)
        {
            var currentPoint = e.GetPosition(this);
            var deltaX = _pressPoint.X - currentPoint.X;
            var deltaY = _pressPoint.Y - currentPoint.Y;
            _pointerMoved |= deltaX * deltaX + deltaY * deltaY > 16;
        }

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

        if (_isWhiteBalanceGesture)
        {
            var shouldPick = !_pointerMoved;
            _isWhiteBalanceGesture = false;
            _isPanning = false;
            e.Pointer.Capture(null);
            Cursor = IsWhiteBalancePicking
                ? new Cursor(StandardCursorType.Cross)
                : Cursor.Default;
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
            Cursor = Cursor.Default;
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
