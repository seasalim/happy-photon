using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DetailAndClippingControlTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-detail-clipping-ui-{Guid.NewGuid():N}")).FullName;

    [AvaloniaFact]
    public async Task DetailGroupUsesAllSourceSlidersAndResolvesSourceDefault()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        await using var vm = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask);
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window { Width = 250, Height = 660, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var detail = panel.FindControl<DetailEditGroup>("DetailEditGroup")!;
        var luminanceNr = detail.FindControl<CompactSlider>(
            "LuminanceNrSlider")!;

        vm.SelectedImage = new ImageFile(Path.Combine(_root, "raw.dng"));
        Dispatcher.UIThread.RunJobs();
        Assert.True(luminanceNr.IsVisible);
        Assert.True(luminanceNr.IsEnabled);
        Assert.Equal(25, vm.CaptureSharpen);

        vm.SelectedImage = new ImageFile(Path.Combine(_root, "standard.jpg"));
        Dispatcher.UIThread.RunJobs();
        Assert.True(luminanceNr.IsVisible);
        Assert.True(luminanceNr.IsEnabled);
        Assert.Equal(0, vm.CaptureSharpen);
        Assert.Null(detail.FindControl<ListBox>("NoiseReductionControl"));

        window.Close();
        panel.DataContext = null;
    }

    [AvaloniaFact]
    public void DisplayTrianglesUseSourceSaturationAndFloorSemantics()
    {
        var histogram = new HistogramView
        {
            Histogram = new HistogramData(),
            ShowDisplayClippingIndicators = true,
            Clipping = new ClippingStats(
                ChannelClip.Empty,
                ChannelClip.Empty,
                HighAny: 0.2,
                LowAll: 0.1,
                IsHighAvailable: true)
        };
        var window = new Window { Width = 250, Height = 100, Content = histogram };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var high = histogram.FindControl<Border>(
            "SceneHighlightTriangleTarget")!;
        var floor = histogram.FindControl<Border>(
            "DisplayFloorTriangleTarget")!;

        Assert.True(high.IsHitTestVisible);
        Assert.True(floor.IsHitTestVisible);
        Assert.Contains("Source saturation", ToolTip.GetTip(high)!.ToString());
        Assert.Contains("Display-floor shadows", ToolTip.GetTip(floor)!.ToString());
        Assert.Contains("sensor saturation", ToolTip.GetTip(high)!.ToString()!,
            StringComparison.OrdinalIgnoreCase);

        histogram.Clipping = null;
        Dispatcher.UIThread.RunJobs();
        Assert.True(high.IsHitTestVisible);
        Assert.True(floor.IsHitTestVisible);
        Assert.Contains("unavailable", ToolTip.GetTip(high)!.ToString());
        Assert.Contains("unavailable", ToolTip.GetTip(floor)!.ToString());

        histogram.Clipping = new ClippingStats(
            ChannelClip.Empty,
            ChannelClip.Empty,
            HighAny: 0,
            LowAll: 0.1,
            IsHighAvailable: false);
        Dispatcher.UIThread.RunJobs();
        Assert.True(high.IsHitTestVisible);
        Assert.True(floor.IsHitTestVisible);
        Assert.Contains("unavailable", ToolTip.GetTip(high)!.ToString());
        Assert.Contains("Display-floor shadows", ToolTip.GetTip(floor)!.ToString());

        histogram.Clipping = new ClippingStats(
            ChannelClip.Empty,
            ChannelClip.Empty,
            HighAny: 0.2,
            LowAll: 0.1,
            IsHighAvailable: true);
        histogram.Histogram = new HistogramData
        {
            Domain = HistogramDomain.RawSensor
        };
        Dispatcher.UIThread.RunJobs();
        Assert.False(histogram.FindControl<Grid>(
            "DisplayClippingIndicators")!.IsVisible);

        histogram.Histogram = new HistogramData();
        Dispatcher.UIThread.RunJobs();
        Assert.True(histogram.FindControl<Grid>(
            "DisplayClippingIndicators")!.IsVisible);

        window.Close();
    }

    [AvaloniaFact]
    public void OverlayMapsSemanticFlagsAndTracksZoomedImageGeometry()
    {
        using var source = new WriteableBitmap(
            new PixelSize(2, 1),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);
        using var mask = new ClippingMask(
            2,
            1,
            ClippingOverlaySide.Both,
            [
                (byte)ClippingOverlaySide.Both,
                (byte)ClippingOverlaySide.DisplayFloor
            ]);
        var viewer = new ZoomPanControl
        {
            Source = source,
            ZoomLevel = 2,
            ClippingMask = mask,
            VisibleClippingSides = ClippingOverlaySide.Both,
            IsClippingOverlayLatched = true
        };
        var window = new Window { Width = 200, Height = 120, Content = viewer };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var overlay = viewer.FindControl<ClippingOverlayControl>(
            "ClippingOverlay")!;
        var pixels = BitmapConversionService.CopyBgraPixels(
            overlay.BitmapForTesting!);

        Assert.Equal(4, overlay.Width);
        Assert.Equal(2, overlay.Height);
        Assert.Equal(
            Premultiply(HappyPhotonColors.SceneHighlightClipColor.B),
            pixels[0]);
        Assert.Equal(
            Premultiply(HappyPhotonColors.SceneHighlightClipColor.R),
            pixels[2]);
        Assert.Equal(
            Premultiply(HappyPhotonColors.DisplayFloorClipColor.B),
            pixels[4]);
        Assert.Equal(
            Premultiply(HappyPhotonColors.DisplayFloorClipColor.R),
            pixels[6]);

        viewer.VisibleClippingSides = ClippingOverlaySide.DisplayFloor;
        Dispatcher.UIThread.RunJobs();
        pixels = BitmapConversionService.CopyBgraPixels(
            overlay.BitmapForTesting!);
        Assert.Equal(
            Premultiply(HappyPhotonColors.DisplayFloorClipColor.B),
            pixels[0]);
        Assert.Equal(
            Premultiply(HappyPhotonColors.DisplayFloorClipColor.R),
            pixels[2]);

        var status = viewer.FindControl<TextBlock>("ClippingStatus")!;
        var viewportLayer = viewer.FindControl<Panel>("ViewportOverlayLayer")!;
        var imagePanel = viewer.FindControl<Panel>("ImagePanel")!;
        Assert.True(status.IsVisible);
        Assert.Equal("CLIPPING · HIGHLIGHTS / FLOOR", status.Text);
        Assert.Contains(
            ShortcutCatalog.Groups.SelectMany(group => group.Entries),
            entry => entry.Keys == "J" &&
                entry.Action.Contains("highlight/floor"));
        Assert.Same(viewportLayer, status.Parent);
        Assert.DoesNotContain(status, imagePanel.GetLogicalDescendants());

        window.Close();
    }

    private static byte Premultiply(byte channel) => (byte)Math.Round(
        channel * HappyPhotonColors.SceneHighlightClipColor.A / 255.0,
        MidpointRounding.ToEven);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }
}
