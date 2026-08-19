using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using HappyPhoton.Models;

namespace HappyPhoton.Views;

public partial class HistogramView : UserControl
{
    public const long RawClippingDotMinPhotosites = 16;

    public static readonly StyledProperty<HistogramData?> HistogramProperty =
        AvaloniaProperty.Register<HistogramView, HistogramData?>(nameof(Histogram));

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<HistogramView, string>(
            nameof(Title), "HISTOGRAM");

    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<HistogramView, object?>(nameof(HeaderContent));

    public HistogramData? Histogram
    {
        get => GetValue(HistogramProperty);
        set => SetValue(HistogramProperty, value);
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    private Canvas? _canvas;
    private StackPanel? _clippingPanel;
    private Ellipse? _redDot;
    private Ellipse? _greenDot;
    private Ellipse? _blueDot;
    private TextBlock? _redText;
    private TextBlock? _greenText;
    private TextBlock? _blueText;

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
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == HistogramProperty)
        {
            DrawHistogram();
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
