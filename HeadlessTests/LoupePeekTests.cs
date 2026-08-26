using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;
namespace HappyPhoton.Tests;
public sealed partial class LoupePeekTests
{
    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void HoldAndMoves_PeekAtOriginalOneToOneThenRestoreExactViewport(double renderScaling)
    {
        var clock = new TestTimeProvider();
        using var bitmap = CreateBitmap(600, 450);
        var identity = new object();
        var viewer = CreateViewer(clock, bitmap, identity);
        viewer.OriginalViewPixelSize = new PixelSize(2400, 1800);
        var requiredBounds = new List<int>();
        viewer.RequiredDeviceLongEdgeChanged +=
            (_, bound) => requiredBounds.Add(bound);
        var window = Show(viewer, 500, 400, renderScaling);
        try
        {
            var scroll = Scroll(viewer);
            var image = Image(viewer);
            var fitSize = image.Bounds.Size;
            var fitOffset = scroll.Offset;
            var fitBounds = ImageBounds(image, scroll);
            var pointer = ViewportPoint(window, scroll, 0.72, 0.36);
            var expected = Normalize(fitBounds, ToScroll(window, scroll, pointer));
            var sourceBefore = viewer.Source;

            window.MouseDown(pointer, MouseButton.Left, RawInputModifiers.None);
            clock.Advance(TimeSpan.FromMilliseconds(149));
            Drain();
            Assert.False(viewer.IsLoupePeekActive);

            clock.Advance(TimeSpan.FromMilliseconds(1));
            Drain();
            Assert.True(viewer.IsLoupePeekActive);
            Assert.Same(sourceBefore, viewer.Source);
            Assert.Equal(2400 / renderScaling, image.Bounds.Width, precision: 8);
            Assert.Equal(1800 / renderScaling, image.Bounds.Height, precision: 8);
            AssertPoint(expected, NormalizeAt(window, image, pointer), image);
            Assert.Equal(2400, requiredBounds[^1]);

            var imageOrigin = image.TranslatePoint(default, window)!.Value;
            var moved = pointer + new Vector(30, 20);
            window.MouseMove(moved, RawInputModifiers.LeftMouseButton);
            Drain();
            var imageTravel = image.TranslatePoint(default, window)!.Value - imageOrigin;
            Assert.Equal(30, imageTravel.X, precision: 8);
            Assert.Equal(20, imageTravel.Y, precision: 8);
            pointer = moved;
            window.MouseUp(pointer, MouseButton.Left, RawInputModifiers.None);
            Drain();
            Assert.False(viewer.IsLoupePeekActive);
            Assert.Equal(fitSize, image.Bounds.Size);
            Assert.Equal(fitOffset, scroll.Offset);
            Assert.NotEqual(2400, requiredBounds[^1]);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ClickReleasedBelowThreshold_RemainsInert()
    {
        var clock = new TestTimeProvider();
        using var bitmap = CreateBitmap(1200, 900);
        var viewer = CreateViewer(clock, bitmap, new object());
        var window = Show(viewer, 500, 400);
        try
        {
            var image = Image(viewer);
            var size = image.Bounds.Size;
            var pointer = Center(viewer, window);
            window.MouseDown(pointer, MouseButton.Left, RawInputModifiers.None);
            clock.Advance(TimeSpan.FromMilliseconds(149));
            window.MouseUp(pointer, MouseButton.Left, RawInputModifiers.None);
            clock.Advance(TimeSpan.FromSeconds(1));
            Drain();

            Assert.False(viewer.IsLoupePeekActive);
            Assert.Equal(size, image.Bounds.Size);
            Assert.Equal(default, Scroll(viewer).Offset);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("crop")]
    [InlineData("white-balance")]
    [InlineData("fit-at-one-to-one")]
    [InlineData("at-one-to-one")]
    [InlineData("zoomed")]
    public void IneligibleViewer_DoesNotBeginPeek(string state)
    {
        var clock = new TestTimeProvider();
        using var bitmap = state == "fit-at-one-to-one"
            ? CreateBitmap(100, 100)
            : CreateBitmap(1200, 900);
        var viewer = CreateViewer(clock, bitmap, new object());
        if (state == "crop") viewer.IsCropMode = true;
        if (state == "white-balance") viewer.IsWhiteBalancePicking = true;
        if (state == "at-one-to-one" || state == "zoomed") viewer.AutoFit = false;
        if (state == "at-one-to-one") viewer.ZoomLevel = 1;
        if (state == "zoomed") viewer.ZoomLevel = 1.25;
        var window = Show(viewer, 500, 400);
        try
        {
            var pointer = Center(viewer, window);
            window.MouseDown(pointer, MouseButton.Left, RawInputModifiers.None);
            clock.Advance(TimeSpan.FromSeconds(1));
            Drain();
            Assert.False(viewer.IsLoupePeekActive);
            window.MouseUp(pointer, MouseButton.Left, RawInputModifiers.None);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CaptureLossAndPhotoChange_RestoreWhileRefinementKeepsPeek()
    {
        var clock = new TestTimeProvider();
        using var first = CreateBitmap(1200, 900);
        using var refinement = CreateBitmap(600, 450);
        var identity = new object();
        var viewer = CreateViewer(clock, first, identity);
        viewer.OriginalViewPixelSize = first.PixelSize;
        IPointer? pointerDevice = null;
        viewer.AddHandler(
            InputElement.PointerPressedEvent,
            (_, e) => pointerDevice = e.Pointer,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        var window = Show(viewer, 500, 400);
        try
        {
            var image = Image(viewer);
            var fitSize = image.Bounds.Size;
            var pointer = Center(viewer, window);
            Engage(window, clock, pointer);

            viewer.Source = refinement;
            Drain();
            Assert.True(viewer.IsLoupePeekActive);

            viewer.SourceIdentity = new object();
            Drain();
            Assert.False(viewer.IsLoupePeekActive);
            Assert.Equal(fitSize, image.Bounds.Size);

            viewer.SourceIdentity = identity;
            pointer = Center(viewer, window);
            Engage(window, clock, pointer);
            Assert.NotNull(pointerDevice);
            pointerDevice!.Capture(null);
            Drain();
            Assert.False(viewer.IsLoupePeekActive);
            Assert.Equal(fitSize, image.Bounds.Size);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DetachingViewer_CancelsPendingOrActivePeek(bool engage)
    {
        var clock = new TestTimeProvider();
        using var bitmap = CreateBitmap(1200, 900);
        var viewer = CreateViewer(clock, bitmap, new object());
        var window = Show(viewer, 500, 400);
        try
        {
            var pointer = Center(viewer, window);
            window.MouseDown(pointer, MouseButton.Left, RawInputModifiers.None);
            if (engage)
            {
                clock.Advance(TimeSpan.FromMilliseconds(150));
                Drain();
                Assert.True(viewer.IsLoupePeekActive);
            }

            window.Content = null;
            Drain();
            clock.Advance(TimeSpan.FromSeconds(1));
            Drain();

            Assert.False(viewer.IsLoupePeekActive);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task EscapeAndFullScreen_UseActiveViewerWithoutChangingDocumentState()
    {
        var root = NewRoot();
        var clock = new TestTimeProvider();
        using var catalog = new CatalogService(Path.Combine(root, "catalog"));
        await using var vm = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);
        using var bitmap = CreateBitmap(2400, 1800);
        var selected = new ImageFile(Path.Combine(root, "photo.jpg"));
        vm.SelectedImage = selected;
        vm.IsDevelopMode = true;
        vm.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var window = new MainWindow { DataContext = vm };
        try
        {
            window.Show();
            Drain();
            vm.PreviewImage = bitmap;
            vm.OriginalViewPixelSize = bitmap.PixelSize;
            Drain();
            var develop = window.FindControl<DevelopViewerPane>("DevelopViewerPane")!.Viewer;
            develop.SetLoupeTimeProvider(clock);
            var fitZoom = vm.ZoomLevel;
            var fitMode = vm.IsZoomFitMode;
            var manualZoom = vm.ManualZoomLevel;
            var settings = selected.EditSettings;
            var developPoint = Center(develop, window);
            Engage(window, clock, developPoint);
            Assert.True(
                develop.IsLoupePeekActive,
                $"source={develop.Source != null}, autoFit={develop.AutoFit}, " +
                $"fit={develop.GetFitZoomLevel()}, size={develop.Bounds.Size}");
            var focusedButton = window.FindControl<DevelopViewerPane>("DevelopViewerPane")!
                .FindControl<Button>("RotateLeftButton")!;
            Assert.True(focusedButton.Focus());
            // Real input pipeline: Escape is ranked by the window's KeyBinding, so
            // raising KeyDown directly would exercise a path the app never takes.
            window.KeyPress(
                Key.Escape,
                RawInputModifiers.None,
                PhysicalKey.Escape,
                null);
            Drain();
            Assert.False(develop.IsLoupePeekActive);
            Assert.True(vm.IsDevelopMode);
            Assert.False(vm.IsFullScreenMode);
            AssertDocumentState(fitZoom, fitMode, manualZoom, settings, vm, selected);
            window.MouseUp(
                developPoint,
                MouseButton.Left,
                RawInputModifiers.None);

            vm.IsFullScreenMode = true;
            Drain();
            using var fullScreenBitmap = CreateBitmap(2400, 1800);
            vm.PreviewImage = fullScreenBitmap;
            vm.OriginalViewPixelSize = fullScreenBitmap.PixelSize;
            Drain();
            var fullScreen = window.FindControl<ZoomPanControl>(
                "FullScreenZoomPanControl")!;
            fullScreen.SetLoupeTimeProvider(clock);
            var fullScreenFitZoom = vm.ZoomLevel;
            var fullScreenFitMode = vm.IsZoomFitMode;
            var fullScreenManualZoom = vm.ManualZoomLevel;
            var fullScreenPoint = Center(fullScreen, window);
            Engage(window, clock, fullScreenPoint);
            Assert.True(
                fullScreen.IsLoupePeekActive,
                $"source={fullScreen.Source != null}, autoFit={fullScreen.AutoFit}, " +
                $"fit={fullScreen.GetFitZoomLevel()}, size={fullScreen.Bounds.Size}, " +
                $"point={fullScreenPoint}");
            window.MouseUp(
                fullScreenPoint,
                MouseButton.Left,
                RawInputModifiers.None);
            Drain();
            Assert.False(fullScreen.IsLoupePeekActive);
            Assert.True(vm.IsFullScreenMode);
            AssertDocumentState(
                fullScreenFitZoom,
                fullScreenFitMode,
                fullScreenManualZoom,
                settings,
                vm,
                selected);
        }
        finally
        {
            vm.PreviewImage = null;
            window.DataContext = null;
            window.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ShortcutCatalog_ListsLoupeGesture()
    {
        var entry = ShortcutCatalog.Groups
            .SelectMany(group => group.Entries)
            .Single(item => item.Keys == "Hold left mouse");
        Assert.Contains("1:1", entry.Action);
        Assert.Contains("fullscreen", entry.Action);
    }

    [AvaloniaFact]
    public void LoupePeek_GeneratesMockupReviewScreenshot()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_LOUPE_LOOKGATE") != "1",
            "Set HAPPY_PHOTON_LOUPE_LOOKGATE=1 and " +
            "HAPPY_PHOTON_LOUPE_LOOKGATE_DIR to generate the screenshot.");
        var outputDirectory = Environment.GetEnvironmentVariable(
            "HAPPY_PHOTON_LOUPE_LOOKGATE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(outputDirectory));
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var clock = new TestTimeProvider();
        using var bitmap = CreateBitmap(800, 600, structured: true);
        var viewer = CreateViewer(clock, bitmap, new object());
        viewer.OriginalViewPixelSize = new PixelSize(1600, 1200);
        var window = Show(viewer, 700, 500);
        try
        {
            Engage(window, clock, Center(viewer, window));
            Assert.True(viewer.IsLoupePeekActive);
            Assert.True(viewer.FindControl<TextBlock>("LoupeStatus")!.IsVisible);
            Assert.Equal(1600, Image(viewer).Bounds.Width, precision: 8);
            Assert.Equal(1200, Image(viewer).Bounds.Height, precision: 8);
            using var frame = window.CaptureRenderedFrame() ??
                throw new InvalidOperationException("Loupe screenshot was empty.");
            frame.Save(Path.Combine(outputDirectory, "loupe-peek.png"));
        }
        finally
        {
            window.Close();
        }
    }

    private static ZoomPanControl CreateViewer(
        TimeProvider clock,
        Bitmap bitmap,
        object identity)
    {
        var viewer = new ZoomPanControl(clock)
        {
            Source = bitmap,
            SourceIdentity = identity,
            ScrollBarVisibility = ScrollBarVisibility.Hidden
        };
        viewer.AutoFitRequested += (_, zoom) => viewer.ZoomLevel = zoom;
        return viewer;
    }

    private static Window Show(
        Control content,
        double width,
        double height,
        double renderScaling = 1)
    {
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        if (renderScaling != 1)
        {
            window.SetRenderScaling(renderScaling);
        }
        Drain();
        return window;
    }

    private static void Engage(Window window, TestTimeProvider clock, Point point)
    {
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        clock.Advance(TimeSpan.FromMilliseconds(150));
        Drain();
    }

    private static void AssertDocumentState(
        double zoom,
        bool fitMode,
        double manualZoom,
        EditSettings settings,
        MainWindowViewModel vm,
        ImageFile selected)
    {
        Assert.Equal(zoom, vm.ZoomLevel);
        Assert.Equal(fitMode, vm.IsZoomFitMode);
        Assert.Equal(manualZoom, vm.ManualZoomLevel);
        Assert.Same(settings, selected.EditSettings);
        Assert.False(vm.CanUndo);
    }

    private static Rect ImageBounds(Image image, Visual relativeTo) =>
        new(image.TranslatePoint(default, relativeTo)!.Value, image.Bounds.Size);

    private static Point Normalize(Rect bounds, Point point) =>
        new(
            (point.X - bounds.X) / bounds.Width,
            (point.Y - bounds.Y) / bounds.Height);

    private static Point NormalizeAt(Visual relativeTo, Image image, Point point)
    {
        var origin = image.TranslatePoint(default, relativeTo)!.Value;
        return new Point(
            (point.X - origin.X) / image.Bounds.Width,
            (point.Y - origin.Y) / image.Bounds.Height);
    }

    private static void AssertPoint(Point expected, Point actual, Image image)
    {
        Assert.InRange(
            Math.Abs(expected.X - actual.X) * image.Bounds.Width,
            0,
            1.01);
        Assert.InRange(
            Math.Abs(expected.Y - actual.Y) * image.Bounds.Height,
            0,
            1.01);
    }

    private static Point ToScroll(
        Visual relativeTo,
        ScrollViewer scroll,
        Point point) =>
        point - scroll.TranslatePoint(default, relativeTo)!.Value;

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

    private static Point Center(Control control, Visual relativeTo)
    {
        var origin = control.TranslatePoint(default, relativeTo)!.Value;
        return origin + new Vector(control.Bounds.Width / 2, control.Bounds.Height / 2);
    }

    private static ScrollViewer Scroll(ZoomPanControl viewer) =>
        viewer.FindControl<ScrollViewer>("ScrollViewer")!;

    private static Image Image(ZoomPanControl viewer) =>
        viewer.FindControl<Image>("ImageControl")!;

    private static void Drain()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    private static Bitmap CreateBitmap(int width, int height, bool structured = false)
    {
        using var image = new MagickImage(
            MagickColors.DarkSlateGray,
            (uint)width,
            (uint)height);
        if (structured)
        {
            using var stripe = new MagickImage(MagickColors.Goldenrod, (uint)(width / 8), (uint)height);
            for (var x = width / 8; x < width; x += width / 4)
                image.Composite(stripe, x, 0, CompositeOperator.Over);
        }
        return BitmapConversionService.ConvertToBitmap(image)!;
    }

    private static string NewRoot() => Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-loupe-{Guid.NewGuid():N}")).FullName;
}
