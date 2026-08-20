using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class CursorAnchoredZoomTests
{
    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void FirstWheelStepFromFit_PreservesOffCenterCursorPoint(
        double renderScaling)
    {
        using var bitmap = CreateBitmap(1200, 950);
        var viewer = CreateViewer(bitmap);
        var window = Show(viewer, 500, 400, renderScaling);
        try
        {
            var scroll = Scroll(viewer);
            var image = Image(viewer);
            var pointer = ViewportPoint(window, scroll, 0.71, 0.37);
            var before = NormalizedImagePoint(window, image, pointer);
            window.MouseWheel(pointer, new Vector(0, 1), RawInputModifiers.None);
            Drain();
            var after = NormalizedImagePoint(window, image, pointer);
            Assert.False(viewer.AutoFit);
            AssertSameImagePoint(before, after, image);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AssessmentPaddingAndLetterbox_UseRealizedImageGeometry()
    {
        using var bitmap = CreateBitmap(1600, 600);
        var viewer = CreateViewer(bitmap, assessment: true);
        var window = Show(viewer, 600, 450);
        try
        {
            viewer.AutoFit = false;
            viewer.ZoomLevel = viewer.GetFitZoomLevel() * 2;
            Drain();

            var scroll = Scroll(viewer);
            var image = Image(viewer);
            var imageOrigin = image.TranslatePoint(default, window)!.Value;
            var pointer = new Point(
                imageOrigin.X + image.Bounds.Width * 0.73,
                imageOrigin.Y - 12);
            var before = NormalizedImagePoint(window, image, pointer);
            Assert.Equal(0, Math.Clamp(before.Y, 0, 1));
            window.MouseWheel(pointer, new Vector(0, 1), RawInputModifiers.None);
            Drain();
            var after = NormalizedImagePoint(window, image, pointer);
            AssertSameImageCoordinate(before.X, after.X, image.Bounds.Width);
            Assert.Equal(0, scroll.Offset.Y);
            Assert.Equal(0, Math.Clamp(after.Y, 0, 1));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SliderZoom_RemainsViewportCenterAnchored()
    {
        using var bitmap = CreateBitmap(1200, 900);
        var viewer = CreateViewer(bitmap, autoFit: false, zoomLevel: 0.75);
        var window = Show(viewer, 500, 400);
        try
        {
            var scroll = Scroll(viewer);
            scroll.Offset = new Vector(230, 145);
            Drain();
            var before = NormalizedViewportCenter(scroll, Image(viewer));
            viewer.ZoomLevel = 1.2;
            Drain();
            AssertSameImagePoint(
                before,
                NormalizedViewportCenter(scroll, Image(viewer)),
                Image(viewer));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void WheelZoomOut_CentersImageAndClearsOffsetsBelowViewport()
    {
        using var bitmap = CreateBitmap(800, 600);
        var viewer = CreateViewer(bitmap, autoFit: false, zoomLevel: 1);
        var window = Show(viewer, 400, 300);
        try
        {
            var scroll = Scroll(viewer);
            scroll.Offset = new Vector(180, 120);
            Drain();
            var pointer = ViewportPoint(window, scroll, 0.72, 0.41);
            for (var step = 0; step < 17; step++)
            {
                window.MouseWheel(
                    pointer, new Vector(0, -1), RawInputModifiers.None);
                Drain();
            }
            var image = Image(viewer);
            Assert.True(image.Bounds.Width < scroll.Viewport.Width);
            Assert.True(image.Bounds.Height < scroll.Viewport.Height);
            Assert.Equal(default, scroll.Offset);
            AssertCenteredOnConstrainedAxis(scroll, image, horizontal: true);
            AssertCenteredOnConstrainedAxis(scroll, image, horizontal: false);
            var centeredAnchor = NormalizedViewportCenter(scroll, image);
            viewer.ZoomLevel = 1;
            Drain();
            AssertSameImagePoint(
                centeredAnchor,
                NormalizedViewportCenter(scroll, image),
                image);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SuccessfulThenNoOpWheelWithoutLayout_KeepsScheduledRestore()
    {
        using var bitmap = CreateBitmap(1000, 800);
        var viewer = CreateViewer(
            bitmap,
            autoFit: false,
            zoomLevel: 5.0 / 1.1);
        var window = Show(viewer, 500, 400);
        try
        {
            var scroll = Scroll(viewer);
            scroll.Offset = new Vector(1700, 1300);
            Drain();
            var image = Image(viewer);
            var pointer = ViewportPoint(window, scroll, 0.76, 0.32);
            var before = NormalizedImagePoint(window, image, pointer);

            window.MouseWheel(pointer, new Vector(0, 1), RawInputModifiers.None);
            window.MouseWheel(pointer, new Vector(0, 1), RawInputModifiers.None);
            Drain();

            Assert.Equal(5, viewer.ZoomLevel, precision: 10);
            AssertSameImagePoint(
                before,
                NormalizedImagePoint(window, image, pointer),
                image);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(0.1, -1.0, 0.2)]
    [InlineData(5.0, 1.0, 4.0)]
    public void NoOpWheelAtLimit_DoesNotAnchorFollowingSliderChange(
        double limit,
        double wheelDelta,
        double sliderZoom)
    {
        using var bitmap = CreateBitmap(1000, 800);
        var viewer = CreateViewer(bitmap, autoFit: false, zoomLevel: limit);
        viewer.OriginalViewPixelSize = new PixelSize(6000, 4800);
        var window = Show(viewer, 500, 400);
        try
        {
            var scroll = Scroll(viewer);
            scroll.Offset = limit < 1
                ? new Vector(65, 50)
                : new Vector(9000, 7000);
            Drain();
            var before = NormalizedViewportCenter(scroll, Image(viewer));
            var pointer = ViewportPoint(window, scroll, 0.81, 0.24);

            window.MouseWheel(
                pointer, new Vector(0, wheelDelta), RawInputModifiers.None);
            viewer.ZoomLevel = sliderZoom;
            Drain();

            AssertSameImagePoint(
                before,
                NormalizedViewportCenter(scroll, Image(viewer)),
                Image(viewer));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SourceSwapAndScalingChange_PreserveNormalizedViewportCenter()
    {
        using var first = CreateBitmap(1000, 800);
        using var second = CreateBitmap(500, 400);
        var viewer = CreateViewer(first, autoFit: false, zoomLevel: 0.5);
        viewer.OriginalViewPixelSize = new PixelSize(2000, 1600);
        var window = Show(viewer, 500, 400);
        try
        {
            var scroll = Scroll(viewer);
            scroll.Offset = new Vector(260, 175);
            Drain();
            var before = NormalizedViewportCenter(scroll, Image(viewer));

            viewer.Source = second;
            Drain();
            AssertSameImagePoint(
                before,
                NormalizedViewportCenter(scroll, Image(viewer)),
                Image(viewer));

            window.SetRenderScaling(1.5);
            Drain();
            AssertSameImagePoint(
                before,
                NormalizedViewportCenter(scroll, Image(viewer)),
                Image(viewer));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void NullSourceEntryAfterNoOpWheel_StartsWithCleanFit()
    {
        using var bitmap = CreateBitmap(1000, 800);
        var viewer = CreateViewer(source: null, autoFit: false, zoomLevel: 0.1);
        var window = Show(viewer, 500, 400);
        try
        {
            window.MouseWheel(
                new Point(390, 105), new Vector(0, -1),
                RawInputModifiers.None);
            viewer.AutoFit = true;
            viewer.Source = bitmap;
            Drain();

            Assert.True(viewer.AutoFit);
            Assert.Equal(viewer.GetFitZoomLevel(), viewer.ZoomLevel, 10);
            Assert.Equal(default, Scroll(viewer).Offset);
            AssertCenteredOnConstrainedAxis(
                Scroll(viewer),
                Image(viewer),
                horizontal: true);
            AssertCenteredOnConstrainedAxis(
                Scroll(viewer),
                Image(viewer),
                horizontal: false);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task FullScreenWheel_RoutesThroughActiveControlAndAnchorsCursor()
    {
        var root = NewRoot();
        using var catalog = new CatalogService(Path.Combine(root, "catalog"));
        await using var viewModel = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);
        using var bitmap = CreateBitmap(1200, 800);
        viewModel.HasSelectedImage = true;
        viewModel.PreviewImage = bitmap;
        viewModel.IsDevelopMode = true;
        viewModel.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var window = new MainWindow
        {
            Width = 900,
            Height = 650,
            DataContext = viewModel
        };

        try
        {
            window.Show();
            viewModel.IsFullScreenMode = true;
            Drain();
            var viewer = window.FindControl<ZoomPanControl>(
                "FullScreenZoomPanControl")!;
            var scroll = Scroll(viewer);
            var image = Image(viewer);
            var pointer = ViewportPoint(window, scroll, 0.69, 0.41);
            var before = NormalizedImagePoint(window, image, pointer);
            var zoomBefore = viewModel.ZoomLevel;

            window.MouseWheel(pointer, new Vector(0, 1), RawInputModifiers.None);
            Drain();

            Assert.True(viewModel.ZoomLevel > zoomBefore);
            AssertCursorAnchorOrCenter(
                before,
                NormalizedImagePoint(window, image, pointer),
                scroll,
                image);
        }
        finally
        {
            viewModel.PreviewImage = null;
            window.DataContext = null;
            window.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    private static ZoomPanControl CreateViewer(
        Bitmap? source,
        bool autoFit = true,
        double zoomLevel = 1,
        bool assessment = false)
    {
        var viewer = new ZoomPanControl
        {
            Source = source,
            AutoFit = autoFit,
            ZoomLevel = zoomLevel,
            IsColorAssessment = assessment,
            ScrollBarVisibility = ScrollBarVisibility.Hidden
        };
        viewer.AutoFitRequested += (_, zoom) => viewer.ZoomLevel = zoom;
        viewer.ZoomChanged += (_, delta) =>
        {
            viewer.AutoFit = false;
            viewer.ZoomLevel = Math.Clamp(
                delta > 0
                    ? viewer.ZoomLevel * 1.1
                    : viewer.ZoomLevel / 1.1,
                0.1,
                5);
        };
        return viewer;
    }

    private static Window Show(
        Control content,
        double width,
        double height,
        double renderScaling = 1)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = content
        };
        window.Show();
        if (renderScaling != 1)
        {
            window.SetRenderScaling(renderScaling);
        }
        Drain();
        return window;
    }

    private static Point ViewportPoint(
        Visual relativeTo,
        ScrollViewer scroll,
        double x,
        double y)
    {
        var origin = scroll.TranslatePoint(default, relativeTo)!.Value;
        return origin + new Vector(
            scroll.Viewport.Width * x,
            scroll.Viewport.Height * y);
    }

    private static Point NormalizedViewportCenter(
        ScrollViewer scroll,
        Image image)
    {
        var imageOrigin = image.TranslatePoint(default, scroll)!.Value;
        var center = new Point(
            scroll.Viewport.Width / 2,
            scroll.Viewport.Height / 2);
        return new Point(
            (center.X - imageOrigin.X) / image.Bounds.Width,
            (center.Y - imageOrigin.Y) / image.Bounds.Height);
    }

    private static Point NormalizedImagePoint(
        Visual relativeTo,
        Image image,
        Point point)
    {
        var origin = image.TranslatePoint(default, relativeTo)!.Value;
        return new Point(
            (point.X - origin.X) / image.Bounds.Width,
            (point.Y - origin.Y) / image.Bounds.Height);
    }

    private static void AssertCursorAnchorOrCenter(
        Point before,
        Point after,
        ScrollViewer scroll,
        Image image)
    {
        if (image.Bounds.Width > scroll.Viewport.Width)
        {
            AssertSameImageCoordinate(before.X, after.X, image.Bounds.Width);
        }
        else
        {
            AssertCenteredOnConstrainedAxis(scroll, image, horizontal: true);
        }

        if (image.Bounds.Height > scroll.Viewport.Height)
        {
            AssertSameImageCoordinate(before.Y, after.Y, image.Bounds.Height);
        }
        else
        {
            AssertCenteredOnConstrainedAxis(scroll, image, horizontal: false);
        }
    }

    private static void AssertCenteredOnConstrainedAxis(
        ScrollViewer scroll,
        Image image,
        bool horizontal)
    {
        var origin = image.TranslatePoint(default, scroll)!.Value;
        var expected = horizontal
            ? (scroll.Viewport.Width - image.Bounds.Width) / 2
            : (scroll.Viewport.Height - image.Bounds.Height) / 2;
        var actual = horizontal ? origin.X : origin.Y;
        Assert.InRange(Math.Abs(expected - actual), 0, 0.51);
    }

    private static void AssertSameImagePoint(
        Point expected,
        Point actual,
        Image image)
    {
        AssertSameImageCoordinate(expected.X, actual.X, image.Bounds.Width);
        AssertSameImageCoordinate(expected.Y, actual.Y, image.Bounds.Height);
    }

    private static void AssertSameImageCoordinate(
        double expected,
        double actual,
        double realizedLength) =>
        Assert.InRange(Math.Abs(expected - actual) * realizedLength, 0, 1.01);

    private static ScrollViewer Scroll(ZoomPanControl viewer) =>
        viewer.FindControl<ScrollViewer>("ScrollViewer")!;

    private static Image Image(ZoomPanControl viewer) =>
        viewer.FindControl<Image>("ImageControl")!;

    private static void Drain()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    private static Bitmap CreateBitmap(int width, int height)
    {
        using var image = new MagickImage(
            MagickColors.Gray,
            (uint)width,
            (uint)height);
        return BitmapConversionService.ConvertToBitmap(image)!;
    }

    private static string NewRoot() => Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-cursor-zoom-{Guid.NewGuid():N}")).FullName;
}
