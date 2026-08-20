using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.Views;

public partial class HistogramView : UserControl
{
    public const long RawClippingDotMinPhotosites = 16;

    public static readonly StyledProperty<HistogramData?> HistogramProperty =
        AvaloniaProperty.Register<HistogramView, HistogramData?>(nameof(Histogram));

    public static readonly StyledProperty<ClippingStats?> ClippingProperty =
        AvaloniaProperty.Register<HistogramView, ClippingStats?>(nameof(Clipping));

    public static readonly StyledProperty<bool> ClippingIsRawSourceProperty =
        AvaloniaProperty.Register<HistogramView, bool>(nameof(ClippingIsRawSource));

    public static readonly StyledProperty<bool> ShowDisplayClippingIndicatorsProperty =
        AvaloniaProperty.Register<HistogramView, bool>(
            nameof(ShowDisplayClippingIndicators));

    public HistogramData? Histogram
    {
        get => GetValue(HistogramProperty);
        set => SetValue(HistogramProperty, value);
    }

    public ClippingStats? Clipping
    {
        get => GetValue(ClippingProperty);
        set => SetValue(ClippingProperty, value);
    }

    public bool ClippingIsRawSource
    {
        get => GetValue(ClippingIsRawSourceProperty);
        set => SetValue(ClippingIsRawSourceProperty, value);
    }

    public bool ShowDisplayClippingIndicators
    {
        get => GetValue(ShowDisplayClippingIndicatorsProperty);
        set => SetValue(ShowDisplayClippingIndicatorsProperty, value);
    }

    public event EventHandler<ClippingOverlaySide>? ClippingPeekStarted;
    public event EventHandler? ClippingPeekEnded;

    private Canvas? _canvas;
    private StackPanel? _clippingPanel;
    private Ellipse? _redDot;
    private Ellipse? _greenDot;
    private Ellipse? _blueDot;
    private TextBlock? _redText;
    private TextBlock? _greenText;
    private TextBlock? _blueText;
    private Grid? _displayClippingIndicators;
    private Border? _displayFloorTriangleTarget;
    private Border? _sceneHighlightTriangleTarget;
    private Avalonia.Controls.Shapes.Path? _displayFloorTriangle;
    private Avalonia.Controls.Shapes.Path? _sceneHighlightTriangle;

    public HistogramView()
    {
        InitializeComponent();
        _canvas = this.FindControl<Canvas>("HistogramCanvas");
        _clippingPanel = this.FindControl<StackPanel>("RawClippingPanel");
        _redDot = this.FindControl<Ellipse>("RawRedClippingDot");
        _greenDot = this.FindControl<Ellipse>("RawGreenClippingDot");
        _blueDot = this.FindControl<Ellipse>("RawBlueClippingDot");
        _redText = this.FindControl<TextBlock>("RawRedClippingText");
        _greenText = this.FindControl<TextBlock>("RawGreenClippingText");
        _blueText = this.FindControl<TextBlock>("RawBlueClippingText");
        _displayClippingIndicators =
            this.FindControl<Grid>("DisplayClippingIndicators");
        _displayFloorTriangleTarget =
            this.FindControl<Border>("DisplayFloorTriangleTarget");
        _sceneHighlightTriangleTarget =
            this.FindControl<Border>("SceneHighlightTriangleTarget");
        _displayFloorTriangle =
            this.FindControl<Avalonia.Controls.Shapes.Path>("DisplayFloorTriangle");
        _sceneHighlightTriangle =
            this.FindControl<Avalonia.Controls.Shapes.Path>("SceneHighlightTriangle");
        _displayFloorTriangleTarget!.PointerEntered += OnDisplayFloorEntered;
        _displayFloorTriangleTarget.PointerExited += OnTriangleExited;
        _sceneHighlightTriangleTarget!.PointerEntered += OnSceneHighlightEntered;
        _sceneHighlightTriangleTarget.PointerExited += OnTriangleExited;
        UpdateDisplayClippingIndicators();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == HistogramProperty)
        {
            DrawHistogram();
            UpdateDisplayClippingIndicators();
        }
        else if (change.Property == ClippingProperty ||
                 change.Property == ClippingIsRawSourceProperty ||
                 change.Property == ShowDisplayClippingIndicatorsProperty)
        {
            UpdateDisplayClippingIndicators();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        DrawHistogram();
    }

    private void DrawHistogram()
    {
        if (_canvas == null) return;

        _canvas.Children.Clear();

        var histogram = Histogram;
        UpdateClipping(histogram);
        if (histogram == null || histogram.MaxValue == 0) return;

        var width = _canvas.Bounds.Width;
        var height = _canvas.Bounds.Height;

        if (width <= 0 || height <= 0) return;

        DrawChannel(_canvas, histogram.Red, histogram.MaxValue, width, height, HappyPhotonColors.HistogramRed);
        DrawChannel(_canvas, histogram.Green, histogram.MaxValue, width, height, HappyPhotonColors.HistogramGreen);
        DrawChannel(_canvas, histogram.Blue, histogram.MaxValue, width, height, HappyPhotonColors.HistogramBlue);
        if (histogram.Domain != HistogramDomain.RawSensor)
        {
            DrawChannelLine(_canvas, histogram.Luminance, histogram.MaxValue, width, height, HappyPhotonColors.HistogramLuminance);
        }
    }

    private void UpdateClipping(HistogramData? histogram)
    {
        if (_clippingPanel == null) return;
        var clipping = histogram?.Domain == HistogramDomain.RawSensor
            ? histogram.Clipping
            : null;
        _clippingPanel.IsVisible = clipping != null;
        if (clipping == null) return;

        SetClippingChannel(_redDot!, _redText!, clipping.Red,
            clipping.TotalVisibleSamples, HappyPhotonColors.HistogramRed,
            clipping.WhiteLevel);
        SetClippingChannel(_greenDot!, _greenText!, clipping.Green,
            clipping.TotalVisibleSamples, HappyPhotonColors.HistogramGreen,
            clipping.WhiteLevel);
        SetClippingChannel(_blueDot!, _blueText!, clipping.Blue,
            clipping.TotalVisibleSamples, HappyPhotonColors.HistogramBlue,
            clipping.WhiteLevel);
    }

    private void UpdateDisplayClippingIndicators()
    {
        if (_displayClippingIndicators == null ||
            _displayFloorTriangle == null ||
            _sceneHighlightTriangle == null ||
            _displayFloorTriangleTarget == null ||
            _sceneHighlightTriangleTarget == null)
        {
            return;
        }

        var isDisplayHistogram = Histogram?.Domain != HistogramDomain.RawSensor;
        _displayClippingIndicators.IsVisible =
            ShowDisplayClippingIndicators && isDisplayHistogram;
        _displayFloorTriangle.Fill = HappyPhotonColors.DisplayFloorClip;
        _sceneHighlightTriangle.Fill = HappyPhotonColors.SceneHighlightClip;

        var clipping = Clipping;
        var hasStats = clipping != null;
        _displayFloorTriangle.Opacity = clipping?.LowAll > 0 ? 0.9 : 0.25;
        _sceneHighlightTriangle.Opacity = !ClippingIsRawSource
            ? 0.16
            : clipping?.HighAny > 0
                ? 1
                : 0.25;
        _displayFloorTriangleTarget.IsHitTestVisible = true;
        _sceneHighlightTriangleTarget.IsHitTestVisible = true;

        ToolTip.SetTip(
            _displayFloorTriangleTarget,
            hasStats
                ? $"Display-floor shadows: {clipping!.LowAll:P4}."
                : "Display-floor clipping data is unavailable.");
        ToolTip.SetTip(
            _sceneHighlightTriangleTarget,
            !ClippingIsRawSource
                ? "Scene highlight clipping is available for RAW sources."
                : hasStats
                    ? $"Scene highlights above scene white: {clipping!.HighAny:P4}."
                    : "Scene highlight clipping data is unavailable.");
    }

    private void OnDisplayFloorEntered(object? sender, PointerEventArgs e) =>
        ClippingPeekStarted?.Invoke(
            this,
            ClippingOverlaySide.DisplayFloor);

    private void OnSceneHighlightEntered(object? sender, PointerEventArgs e) =>
        ClippingPeekStarted?.Invoke(
            this,
            ClippingOverlaySide.SceneHighlights);

    private void OnTriangleExited(object? sender, PointerEventArgs e) =>
        ClippingPeekEnded?.Invoke(this, EventArgs.Empty);

    private static void SetClippingChannel(
        Ellipse dot,
        TextBlock text,
        long count,
        long total,
        IBrush brush,
        uint whiteLevel)
    {
        var percentage = total > 0 ? count * 100.0 / total : 0;
        dot.Fill = brush;
        dot.Opacity = count >= RawClippingDotMinPhotosites ? 1 : 0.25;
        // Show the percentage only; exact counts (which run to millions) live in the
        // tooltip. Never let a lit channel round to 0.00% — floor it to <0.01%.
        text.Text = count > 0 && percentage < 0.005
            ? "<0.01%"
            : $"{percentage:F2}%";
        ToolTip.SetTip(text,
            $"{count:N0} photosites at or above sensor white level {whiteLevel:N0} ({percentage:F4}%).");
    }

    private static void DrawChannel(Canvas canvas, int[] data, int maxValue, double width, double height, IBrush brush)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(0, height), true);

            for (int i = 0; i < 256; i++)
            {
                var x = i * width / 256.0;
                var barHeight = (data[i] / (double)maxValue) * height;
                context.LineTo(new Point(x, height - barHeight));
            }

            context.LineTo(new Point(width, height));
            context.EndFigure(true);
        }

        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = geometry,
            Fill = brush
        };
        canvas.Children.Add(path);
    }

    private static void DrawChannelLine(Canvas canvas, int[] data, int maxValue, double width, double height, IBrush brush)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var startX = 0.0;
            var startY = height - (data[0] / (double)maxValue) * height;
            context.BeginFigure(new Point(startX, startY), false);

            for (int i = 1; i < 256; i++)
            {
                var x = i * width / 256.0;
                var y = height - (data[i] / (double)maxValue) * height;
                context.LineTo(new Point(x, y));
            }

            context.EndFigure(false);
        }

        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = geometry,
            Stroke = brush,
            StrokeThickness = 1
        };
        canvas.Children.Add(path);
    }
}
