using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
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
    private readonly DisplayChainTrace? _displayChainTrace;

    public ZoomPanControl() : this(TimeProvider.System)
    {
    }

    internal ZoomPanControl(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        InitializeComponent();

        _imageControl = this.FindControl<Image>("ImageControl");
        _scrollViewer = this.FindControl<ScrollViewer>("ScrollViewer");
        _cropOverlay = this.FindControl<CropOverlayControl>("CropOverlay");
        _surroundLayer = this.FindControl<Panel>("SurroundLayer");
        _assessmentMat = this.FindControl<Border>("AssessmentMat");
        InitializeVisibleRegionTracking();
        InitializeClippingOverlay();
        InitializeAlignmentGrid();
        InitializeDeviceScaling();
        InitializeWheelAnchoring();
        InitializeLoupePeek();

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
            var anchor = CaptureSourceChangeAnchor(
                change.OldValue as Bitmap,
                change.NewValue as Bitmap);
            ScheduleAnchorRestoreAfterLayout(anchor);
            if (_imageControl != null)
            {
                _imageControl.Source = Source;
            }
            ApplyColorAssessment();
            UpdateImageSize();
            UpdateAlignmentGridVisibility();
            if (EffectiveAutoFit)
            {
                RequestAutoFit();
            }
            RequestRequiredBoundPublication();
        }
        else if (change.Property == ZoomLevelProperty)
        {
            var anchor = CaptureZoomChangeAnchor(
                change.OldValue is double oldZoom ? oldZoom : ZoomLevel);
            ScheduleAnchorRestoreAfterLayout(anchor);
            UpdateImageSize();
            RequestRequiredBoundPublication();
        }
        else if (change.Property == OriginalViewPixelSizeProperty)
        {
            var anchor = EffectiveAutoFit
                ? null
                : CapturePendingOrViewportCenterAnchor();
            ScheduleAnchorRestoreAfterLayout(anchor);
            UpdateImageSize();
            if (EffectiveAutoFit)
            {
                RequestAutoFit();
            }
            RequestRequiredBoundPublication();
        }
        else if (change.Property == IsCropModeProperty)
        {
            UpdateCropOverlayVisibility();
            UpdateAlignmentGridVisibility();
        }
        else if (change.Property == IsWhiteBalancePickingProperty)
        {
            UpdatePointerCursor();
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
            RequestRequiredBoundPublication();
        }
        else if (change.Property == AutoFitProperty)
        {
            if (AutoFit)
            {
                ScheduleAnchorRestoreAfterLayout(null);
                RequestAutoFit();
            }
            RequestRequiredBoundPublication();
        }
        else if (change.Property == SourceIdentityProperty)
        {
            OnSourceIdentityChanged(change.NewValue);
        }
        else if (IsClippingProperty(change.Property))
        {
            UpdateClippingOverlaySize();
        }
        else if (change.Property == ShowAlignmentGridProperty)
        {
            UpdateAlignmentGridVisibility();
        }

        if (change.Property == SourceProperty ||
            change.Property == ZoomLevelProperty ||
            change.Property == OriginalViewPixelSizeProperty ||
            change.Property == IsDisplayTraceActiveProperty)
        {
            _displayChainTrace?.OnInputChanged();
        }
        OnViewportGeometryInputChanged(change.Property);
        if (change.Property == SourceProperty ||
            change.Property == ZoomLevelProperty ||
            change.Property == IsCropModeProperty)
        {
            UpdatePointerCursor();
        }
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
        RequestRequiredBoundPublication();
        _displayChainTrace?.OnInputChanged();
    }

    private void RequestAutoFit()
    {
        var fitBox = GetColorAssessmentGeometry().FitBox;
        if (EffectiveAutoFit && Source != null &&
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

        return ZoomGeometryCalculator.FitZoomLevel(
            GetOriginalViewPixelSize(),
            new Size(viewportWidth, viewportHeight),
            RenderScaling);
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

}
