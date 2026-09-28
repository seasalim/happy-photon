using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Services;

namespace HappyPhoton.Views;

public partial class ZoomPanControl
{
    public static readonly StyledProperty<bool> VisualizeSpotsProperty =
        AvaloniaProperty.Register<ZoomPanControl, bool>(nameof(VisualizeSpots));

    public static readonly StyledProperty<double> SpotVisualizeThresholdProperty =
        AvaloniaProperty.Register<ZoomPanControl, double>(nameof(SpotVisualizeThreshold), 50);

    public bool VisualizeSpots
    {
        get => GetValue(VisualizeSpotsProperty);
        set => SetValue(VisualizeSpotsProperty, value);
    }

    public double SpotVisualizeThreshold
    {
        get => GetValue(SpotVisualizeThresholdProperty);
        set => SetValue(SpotVisualizeThresholdProperty, value);
    }

    private Image? _visualizeOverlay;

    private long _visualizeGeneration;

    private bool _visualizePending;

    internal Task VisualizeWork { get; private set; } = Task.CompletedTask;

    internal Func<byte[], int, int, double, Bitmap> VisualizeRenderer { get; set; } = SpotVisualizationRenderer.Render;

    internal Bitmap? VisualizeBitmap => _visualizeOverlay?.Source as Bitmap;

    private void InitializeSpotVisualization()
    {
        _visualizeOverlay = this.FindControl<Image>("SpotVisualization");
        _imageControl!.PropertyChanged += (_, e) =>
        {
            if (e.Property == Image.SourceProperty) InvalidateSpotVisualization();
        };
        VisibleRegionChanged += (_, _) => InvalidateSpotVisualization();
        DetachedFromVisualTree += (_, _) => InvalidateSpotVisualization();
    }

    private void InvalidateSpotVisualization()
    {
        _visualizeGeneration++;
        var previous = VisualizeBitmap;
        if (_visualizeOverlay != null) _visualizeOverlay.Source = null;
        previous?.Dispose();
        if (_visualizePending || !VisualizeSpots || !IsSpotsMode || !this.IsAttachedToVisualTree()) return;

        _visualizePending = true;
        Dispatcher.UIThread.Post(() => VisualizeWork = RefreshSpotVisualizationAsync(), DispatcherPriority.Background);
    }

    private async Task RefreshSpotVisualizationAsync()
    {
        var generation = _visualizeGeneration;

        try
        {
            if (!VisualizeSpots || !IsSpotsMode || !this.IsAttachedToVisualTree() ||
                _imageControl?.DisplayedBitmap is not { } displayed || _imageControl.Bounds.Width <= 0 ||
                _imageControl.Bounds.Height <= 0)
                return;

            var region = VisibleRegion ?? new Rect(0, 0, 1, 1);

            // Copy on the UI thread before DisplayImage or the preview owner can retire the bitmap.
            var pixels = CopySpotViewport(displayed, region, out var rect);
            var threshold = SpotVisualizeThreshold;
            var scaleX = _imageControl.Bounds.Width / displayed.PixelSize.Width;
            var scaleY = _imageControl.Bounds.Height / displayed.PixelSize.Height;
            var render = VisualizeRenderer;
            var bitmap = await Task.Run(() => render(pixels, rect.Width, rect.Height, threshold));

            if (generation != _visualizeGeneration)
            {
                bitmap.Dispose();

                return;
            }

            _visualizeOverlay!.Width = rect.Width * scaleX;
            _visualizeOverlay.Height = rect.Height * scaleY;
            Canvas.SetLeft(_visualizeOverlay, rect.X * scaleX);
            Canvas.SetTop(_visualizeOverlay, rect.Y * scaleY);
            _visualizeOverlay.Source = bitmap;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Spot visualization failed: {exception}");
        }
        finally
        {
            _visualizePending = false;
            if (generation != _visualizeGeneration) InvalidateSpotVisualization();
        }
    }

    internal static unsafe byte[] CopySpotViewport(Bitmap displayed, Rect region, out PixelRect rect)
    {
        var size = displayed.PixelSize;
        var x = Math.Clamp((int)Math.Floor(region.X * size.Width), 0, size.Width - 1);
        var y = Math.Clamp((int)Math.Floor(region.Y * size.Height), 0, size.Height - 1);
        var right = Math.Clamp((int)Math.Ceiling(region.Right * size.Width), x + 1, size.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(region.Bottom * size.Height), y + 1, size.Height);
        rect = new PixelRect(x, y, right - x, bottom - y);
        var pixels = new byte[checked(rect.Width * rect.Height * 4)];

        fixed (byte* address = pixels)
            displayed.CopyPixels(rect, (IntPtr)address, pixels.Length, rect.Width * 4);

        return pixels;
    }
}
