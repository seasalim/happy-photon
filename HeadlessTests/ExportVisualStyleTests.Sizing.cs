using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class ExportVisualStyleTests
{
    private readonly ITestOutputHelper _output;

    public ExportVisualStyleTests(ITestOutputHelper output) => _output = output;

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public async Task PreviewResolutionChanges_KeepTheSamePaintedFit(double scaling)
    {
        using var catalog = _fixture.CreateCatalog();
        await using var vm = _fixture.CreateViewModel(
            catalog, loadMetadataAsync: _ => Task.CompletedTask);
        using var thumbnail = WhiteBitmap(600, 300);
        using var preview = WhiteBitmap(1600, 800);
        using var replacement = WhiteBitmap(3200, 1600);
        vm.OriginalViewPixelSize = new PixelSize(6000, 3000);
        var pane = new ExportPreviewPane { DataContext = vm };
        var placeholder = pane.FindControl<PlaceholderImage>("ExportPlaceholderImage")!;
        placeholder.Source = thumbnail;
        placeholder.OriginalPixelWidth = 6000;
        placeholder.OriginalPixelHeight = 3000;
        pane.FindControl<Border>("ExportPreviewEmptyState")!.IsVisible = false;
        var caption = pane.FindControl<TextBlock>("ExportProofCaption")!;
        caption.Text = "PREVIEW";
        caption.IsVisible = true;
        var frame = pane.FindControl<UniformImageOverlayPanel>("ExportPreviewImageFrame")!;
        var window = new Window { Width = 2432, Height = 1660, Content = pane };
        using var scope = new TestUiScope(window);
        window.SetRenderScaling(scaling);
        Dispatcher.UIThread.RunJobs();
        pane.UpdateLayout();
        Assert.Equal(new Size(2400, 1600), frame.Bounds.Size);
        var measurements = new List<(string Stage, Rect Paint, Point CaptionBottom)>();
        try
        {
            foreach (var (stage, bitmap) in new (string, Bitmap?)[]
                     { ("thumbnail", null), ("preview", preview),
                       ("replacement", replacement), ("navigation thumbnail", null),
                       ("next preview", preview) })
            {
                vm.PreviewImage = bitmap;
                Dispatcher.UIThread.RunJobs();
                pane.UpdateLayout();
                var paint = MeasureWhiteImage(window, frame);
                var origin = caption.TranslatePoint(default, frame)!.Value;
                measurements.Add((stage, paint, new Point(origin.X, origin.Y + caption.Bounds.Height)));
                _output.WriteLine($"scale={scaling} {stage}: painted={paint}");
            }
            foreach (var (_, paint, captionBottom) in measurements)
            {
                AssertRectNear(new Rect(0, 200, 2400, 1200), paint, scaling);
                Assert.InRange(Math.Abs(captionBottom.X - paint.Left - 4) * scaling, 0, 1);
                Assert.InRange(Math.Abs(captionBottom.Y - paint.Bottom + 4) * scaling, 0, 1);
            }
        }
        finally
        {
            vm.PreviewImage = null;
        }
    }

    [AvaloniaFact]
    public async Task SmallOriginal_RemainsDeviceNativeAcrossProxyAndMonitorChanges()
    {
        using var catalog = _fixture.CreateCatalog();
        await using var vm = _fixture.CreateViewModel(
            catalog, loadMetadataAsync: _ => Task.CompletedTask);
        using var proxy = WhiteBitmap(128, 128);
        using var replacement = WhiteBitmap(256, 256);
        vm.OriginalViewPixelSize = new PixelSize(256, 256);
        var pane = new ExportPreviewPane { DataContext = vm };
        pane.FindControl<Border>("ExportPreviewEmptyState")!.IsVisible = false;
        var frame = pane.FindControl<UniformImageOverlayPanel>("ExportPreviewImageFrame")!;
        var window = new Window { Width = 832, Height = 660, Content = pane };
        using var scope = new TestUiScope(window);
        try
        {
            foreach (var scaling in new[] { 1, 1.5, 2 })
            {
                window.SetRenderScaling(scaling);
                foreach (var bitmap in new[] { proxy, replacement })
                {
                    vm.PreviewImage = bitmap;
                    Dispatcher.UIThread.RunJobs();
                    pane.UpdateLayout();
                    var size = 256 / scaling;
                    AssertRectNear(new Rect((800 - size) / 2, (600 - size) / 2, size, size),
                        MeasureWhiteImage(window, frame), scaling);
                }
            }
            // No original dimensions: use the current bitmap, not stale native facts.
            vm.OriginalViewPixelSize = default;
            vm.PreviewImage = proxy;
            Dispatcher.UIThread.RunJobs();
            pane.UpdateLayout();
            AssertRectNear(new Rect(368, 268, 64, 64),
                MeasureWhiteImage(window, frame), 2);
        }
        finally
        {
            vm.PreviewImage = null;
        }
    }

    [AvaloniaFact]
    public async Task EditedPreview_UsesMetadataThenAcceptedGeometryAcrossSwaps()
    {
        using var catalog = _fixture.CreateCatalog();
        await using var vm = _fixture.CreateViewModel(catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(SourceAvailability.RequiresHydration));
        vm.SelectedImage = new ImageFile(_fixture.Path("rotated.jpg"))
        {
            PixelWidth = 600,
            PixelHeight = 400,
            EditSettings = new EditSettings
            {
                Rotation = 90,
                Crop = new CropRegion { Left = 0, Top = 0, Right = 1, Bottom = 0.5 }
            }
        };
        using var proxy = WhiteBitmap(80, 60);
        using var replacement = WhiteBitmap(160, 120);
        vm.PreviewImage = proxy;
        var pane = new ExportPreviewPane { DataContext = vm };
        pane.FindControl<Border>("ExportPreviewEmptyState")!.IsVisible = false;
        var frame = pane.FindControl<UniformImageOverlayPanel>("ExportPreviewImageFrame")!;
        var window = new Window { Width = 832, Height = 660, Content = pane };
        using var scope = new TestUiScope(window);
        window.SetRenderScaling(2);
        try
        {
            Dispatcher.UIThread.RunJobs();
            pane.UpdateLayout();
            AssertRectNear(new Rect(300, 225, 200, 150), MeasureWhiteImage(window, frame), 2);
            vm.OriginalViewPixelSize = new PixelSize(200, 150);
            foreach (var bitmap in new[] { proxy, replacement })
            {
                vm.PreviewImage = bitmap;
                Dispatcher.UIThread.RunJobs();
                pane.UpdateLayout();
                AssertRectNear(new Rect(350, 262.5, 100, 75), MeasureWhiteImage(window, frame), 2);
            }
        }
        finally
        {
            vm.PreviewImage = null;
        }
    }

    private static WriteableBitmap WhiteBitmap(int width, int height)
    {
        var bitmap = new WriteableBitmap(new PixelSize(width, height),
            new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var buffer = bitmap.Lock();
        var row = Enumerable.Repeat(-1, width).ToArray();
        for (var y = 0; y < height; y++)
            Marshal.Copy(row, 0, buffer.Address + y * buffer.RowBytes, width);
        return bitmap;
    }

    private static Rect MeasureWhiteImage(Window window, Control imageFrame)
    {
        using var capture = window.CaptureRenderedFrame() ??
            throw new InvalidOperationException("Export frame was empty.");
        using var buffer = capture.Lock();
        var scale = window.RenderScaling;
        var origin = imageFrame.TranslatePoint(default, window)!.Value;
        var left = (int)Math.Round(origin.X * scale);
        var top = (int)Math.Round(origin.Y * scale);
        var width = (int)Math.Round(imageFrame.Bounds.Width * scale);
        var height = (int)Math.Round(imageFrame.Bounds.Height * scale);
        bool White(int x, int y) =>
            (Marshal.ReadInt32(buffer.Address + y * buffer.RowBytes + x * 4) & 0xFFFFFF) == 0xFFFFFF;
        var xs = Enumerable.Range(left, width).Where(x => White(x, top + height / 2)).ToArray();
        var ys = Enumerable.Range(top, height).Where(y => White(left + width / 2, y)).ToArray();
        Assert.NotEmpty(xs);
        Assert.NotEmpty(ys);
        return new Rect((xs[0] - left) / scale, (ys[0] - top) / scale,
            (xs[^1] - xs[0] + 1) / scale, (ys[^1] - ys[0] + 1) / scale);
    }

    private static void AssertRectNear(Rect expected, Rect actual, double scaling)
    {
        Assert.InRange(Math.Abs(expected.X - actual.X) * scaling, 0, 1);
        Assert.InRange(Math.Abs(expected.Y - actual.Y) * scaling, 0, 1);
        Assert.InRange(Math.Abs(expected.Width - actual.Width) * scaling, 0, 1);
        Assert.InRange(Math.Abs(expected.Height - actual.Height) * scaling, 0, 1);
    }
}
