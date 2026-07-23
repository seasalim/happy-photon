using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HappyPhoton.Models;

namespace HappyPhoton.Views;

public partial class HistogramView : UserControl
{
    public static readonly StyledProperty<HistogramData?> HistogramProperty =
        AvaloniaProperty.Register<HistogramView, HistogramData?>(nameof(Histogram));

    public HistogramData? Histogram
    {
        get => GetValue(HistogramProperty);
        set => SetValue(HistogramProperty, value);
    }

    private Canvas? _canvas;

    public HistogramView()
    {
        InitializeComponent();
        _canvas = this.FindControl<Canvas>("HistogramCanvas");
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
        if (histogram == null || histogram.MaxValue == 0) return;

        var width = _canvas.Bounds.Width;
        var height = _canvas.Bounds.Height;

        if (width <= 0 || height <= 0) return;

        var barWidth = width / 256.0;

        DrawChannel(_canvas, histogram.Red, histogram.MaxValue, width, height, HappyPhotonColors.HistogramRed);
        DrawChannel(_canvas, histogram.Green, histogram.MaxValue, width, height, HappyPhotonColors.HistogramGreen);
        DrawChannel(_canvas, histogram.Blue, histogram.MaxValue, width, height, HappyPhotonColors.HistogramBlue);
        DrawChannelLine(_canvas, histogram.Luminance, histogram.MaxValue, width, height, HappyPhotonColors.HistogramLuminance);
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
