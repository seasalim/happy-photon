using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class NavigatorViewportOverlayTests
{
    [Fact]
    public void RegionCalculator_UsesRealizedOriginsAndClampsToImageEdges()
    {
        AssertRect(
            new Rect(0.25, 0.25, 0.5, 0.5),
            ViewportRegion.Calculate(
                new Rect(100, 50, 400, 200),
                new Rect(200, 100, 200, 100)));

        AssertRect(
            new Rect(0, 0.125, 1, 0.5),
            ViewportRegion.Calculate(
                new Rect(50, -50, 200, 400),
                new Rect(0, 0, 300, 200)));

        AssertRect(
            new Rect(0.75, 0, 0.25, 1),
            ViewportRegion.Calculate(
                new Rect(-300, 20, 400, 200),
                new Rect(0, 0, 200, 300)));
    }

    [Fact]
    public void RegionCalculator_HidesByAreaAtThePinnedThreshold()
    {
        var image = new Rect(0, 0, 1000, 1000);

        Assert.Null(ViewportRegion.Calculate(
            image,
            new Rect(0, 0, 995, 1000)));
        Assert.NotNull(ViewportRegion.Calculate(
            image,
            new Rect(0, 0, 994.999, 1000)));
        Assert.NotNull(ViewportRegion.Calculate(
            image,
            new Rect(0, 0, 997, 997)));
        Assert.Null(ViewportRegion.Calculate(
            image,
            new Rect(-50, -50, 1100, 1100)));
    }

    [Fact]
    public void LetterboxMapping_UsesOnlyTheUniformImageArea()
    {
        var landscapeBounds = ViewportRegion.UniformImageBounds(
            new Size(200, 132),
            new Size(300, 200));
        AssertRect(new Rect(1, 0, 198, 132), landscapeBounds);
        AssertRect(
            new Rect(50.5, 33, 99, 66),
            ViewportRegion.MapToImage(
                new Rect(0.25, 0.25, 0.5, 0.5),
                landscapeBounds));

        var portraitBounds = ViewportRegion.UniformImageBounds(
            new Size(200, 132),
            new Size(200, 300));
        AssertRect(new Rect(56, 0, 88, 132), portraitBounds);
        AssertRect(
            new Rect(78, 33, 44, 66),
            ViewportRegion.MapToImage(
                new Rect(0.25, 0.25, 0.5, 0.5),
                portraitBounds));

        AssertRect(
            portraitBounds,
            ViewportRegion.MapToImage(
                new Rect(-0.5, -0.5, 2, 2),
                portraitBounds));
    }

    [AvaloniaTheory]
    [MemberData(
        nameof(ThemeResourceTests.Variants),
        MemberType = typeof(ThemeResourceTests))]
    public void HairlineTokens_MatchVariantAUnderEveryTheme(ThemeVariant variant)
    {
        var stroke = ThemeResourceTests.Brush(
            "NavigatorViewportStroke",
            variant);
        var halo = ThemeResourceTests.Brush(
            "NavigatorViewportHalo",
            variant);

        Assert.Equal(
            ThemeResourceTests.Brush("TextPrimary", variant).Color,
            stroke.Color);
        Assert.Equal(0.92, stroke.Opacity);
        Assert.Equal(Color.Parse("#73000000"), halo.Color);
        Assert.Equal(1, NavigatorViewportOverlay.StrokeThickness);
        Assert.Equal(1, NavigatorViewportOverlay.HaloThickness);
        Assert.Equal(1, NavigatorViewportOverlay.CornerRadius);
    }

    [AvaloniaFact]
    public void RealizedOverlay_MapsAspectSwapsAndKeepsTheHaloClipOnImageBounds()
    {
        using var landscape = CreateBitmap(300, 200);
        using var portrait = CreateBitmap(200, 300);
        var overlay = new NavigatorViewportOverlay
        {
            Source = landscape,
            VisibleRegion = new Rect(0.25, 0.25, 0.5, 0.5),
            Stroke = ThemeResourceTests.Brush(
                "NavigatorViewportStroke",
                ThemeVariant.Dark),
            Halo = ThemeResourceTests.Brush(
                "NavigatorViewportHalo",
                ThemeVariant.Dark)
        };
        var window = Show(overlay, 200, 132);
        try
        {
            AssertRect(new Rect(1, 0, 198, 132), overlay.ImageBounds);
            AssertRect(
                new Rect(50.5, 33, 99, 66),
                overlay.MappedVisibleRegion);

            overlay.Source = portrait;
            Dispatcher.UIThread.RunJobs();

            AssertRect(new Rect(56, 0, 88, 132), overlay.ImageBounds);
            var mapped = AssertRect(
                new Rect(78, 33, 44, 66),
                overlay.MappedVisibleRegion);
            Assert.True(overlay.ImageBounds.Contains(mapped));

            overlay.VisibleRegion = new Rect(0, 0, 0.25, 0.25);
            Dispatcher.UIThread.RunJobs();
            mapped = Assert.IsType<Rect>(overlay.MappedVisibleRegion);
            Assert.Equal(overlay.ImageBounds.Left, mapped.Left);
            Assert.Equal(overlay.ImageBounds.Top, mapped.Top);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ViewerPublishesOnZoomPanResizeAndPreviewSwapThenHidesAtFit()
    {
        using var landscape = CreateBitmap(800, 400);
        using var portrait = CreateBitmap(400, 800);
        var viewer = new ZoomPanControl { Source = landscape };
        var publications = new List<Rect?>();
        viewer.VisibleRegionChanged += (_, region) => publications.Add(region);
        var window = Show(viewer, 400, 300);
        try
        {
            viewer.ZoomLevel = viewer.GetFitZoomLevel();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(viewer.VisibleRegion);

            viewer.ZoomLevel *= 2;
            Dispatcher.UIThread.RunJobs();
            var zoomed = Assert.IsType<Rect>(viewer.VisibleRegion);

            var scroll = viewer.FindControl<ScrollViewer>("ScrollViewer")!;
            scroll.Offset = new Vector(200, 100);
            Dispatcher.UIThread.RunJobs();
            var panned = Assert.IsType<Rect>(viewer.VisibleRegion);
            Assert.NotEqual(zoomed, panned);

            window.Width = 500;
            window.Height = 250;
            Dispatcher.UIThread.RunJobs();
            var resized = Assert.IsType<Rect>(viewer.VisibleRegion);
            Assert.NotEqual(panned, resized);

            viewer.Source = portrait;
            Dispatcher.UIThread.RunJobs();
            var swapped = Assert.IsType<Rect>(viewer.VisibleRegion);
            Assert.NotEqual(resized, swapped);

            viewer.ZoomLevel = viewer.GetFitZoomLevel();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(viewer.VisibleRegion);
            Assert.Contains(publications, region => region != null);
            Assert.Null(publications[^1]);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task DevelopWheelAndDrag_UpdateNavigatorWithHiddenScrollBars()
    {
        var root = NewRoot();
        using var catalog = new CatalogService(Path.Combine(root, "catalog"));
        await using var vm = CreateViewModel(catalog);
        using var bitmap = CreateBitmap(800, 400);
        vm.HasSelectedImage = true;
        vm.PreviewImage = bitmap;
        vm.IsDevelopMode = true;
        vm.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var window = new MainWindow { DataContext = vm };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var viewer = window.FindControl<ZoomPanControl>("ZoomPanControl")!;
            var overlay = window.FindControl<NavigatorViewportOverlay>(
                "NavigatorViewportOverlay")!;
            var scroll = viewer.FindControl<ScrollViewer>("ScrollViewer")!;

            Assert.Equal(ScrollBarVisibility.Hidden, viewer.ScrollBarVisibility);
            Assert.Equal(
                ScrollBarVisibility.Hidden,
                scroll.HorizontalScrollBarVisibility);
            Assert.Equal(
                ScrollBarVisibility.Hidden,
                scroll.VerticalScrollBarVisibility);

            vm.ZoomLevel = viewer.GetFitZoomLevel();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(vm.NavigatorVisibleRegion);
            Assert.False(overlay.IsVisible);

            var point = CenterOf(viewer, window);
            window.MouseWheel(
                point,
                new Vector(0, 1),
                RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(vm.NavigatorVisibleRegion);
            Assert.True(overlay.IsVisible);
            Assert.NotNull(overlay.MappedVisibleRegion);
            Assert.True(viewer.CanPanContent());

            var beforeDrag = vm.NavigatorVisibleRegion;
            var end = point - new Vector(80, 50);
            window.MouseDown(
                point,
                MouseButton.Left,
                RawInputModifiers.None);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(
                end,
                MouseButton.Left,
                RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.True(scroll.Offset.X > 0 || scroll.Offset.Y > 0);
            Assert.NotEqual(beforeDrag, vm.NavigatorVisibleRegion);
        }
        finally
        {
            vm.PreviewImage = null;
            window.DataContext = null;
            window.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task OnlyActiveDevelopPublishesIncludingModeAndFullscreenRoundTrips()
    {
        var root = NewRoot();
        using var catalog = new CatalogService(Path.Combine(root, "catalog"));
        await using var vm = CreateViewModel(catalog);
        using var bitmap = CreateBitmap(800, 400);
        vm.HasSelectedImage = true;
        vm.PreviewImage = bitmap;
        vm.IsDevelopMode = true;
        var window = new MainWindow { DataContext = vm };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var develop = window.FindControl<ZoomPanControl>("ZoomPanControl")!;
            var fullScreen = window.FindControl<ZoomPanControl>(
                "FullScreenZoomPanControl")!;

            vm.ZoomLevel = develop.GetFitZoomLevel() * 2;
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(vm.NavigatorVisibleRegion);

            vm.IsDevelopMode = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(vm.NavigatorVisibleRegion);

            vm.IsDevelopMode = true;
            vm.PreviewImage = CreateBitmap(800, 400);
            vm.ZoomLevel = develop.GetFitZoomLevel() * 2;
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(vm.NavigatorVisibleRegion);

            vm.IsDevelopMode = false;
            vm.ZoomLevel *= 1.1;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(vm.NavigatorVisibleRegion);
            vm.IsDevelopMode = true;
            vm.PreviewImage = CreateBitmap(800, 400);
            vm.ZoomLevel = develop.GetFitZoomLevel() * 2;
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(vm.NavigatorVisibleRegion);

            vm.IsFullScreenMode = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(vm.NavigatorVisibleRegion);
            var fullScreenPoint = CenterOf(fullScreen, window);
            window.MouseWheel(
                fullScreenPoint,
                new Vector(0, 1),
                RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(vm.NavigatorVisibleRegion);

            var exitPublications = new List<Rect?>();
            void OnPropertyChanged(
                object? sender,
                PropertyChangedEventArgs args)
            {
                if (args.PropertyName ==
                    nameof(MainWindowViewModel.NavigatorVisibleRegion))
                {
                    exitPublications.Add(vm.NavigatorVisibleRegion);
                }
            }

            vm.PropertyChanged += OnPropertyChanged;
            vm.IsFullScreenMode = false;
            Dispatcher.UIThread.RunJobs();
            vm.PropertyChanged -= OnPropertyChanged;

            Assert.Null(vm.NavigatorVisibleRegion);
            Assert.DoesNotContain(exitPublications, region => region != null);

            vm.IsDevelopMode = false;
            vm.IsFullScreenMode = true;
            Dispatcher.UIThread.RunJobs();
            vm.IsFullScreenMode = false;
            Dispatcher.UIThread.RunJobs();
            vm.ZoomLevel = develop.GetFitZoomLevel() * 2;
            vm.IsDevelopMode = true;
            vm.PreviewImage = CreateBitmap(800, 400);
            vm.ZoomLevel = develop.GetFitZoomLevel() * 2;
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(vm.NavigatorVisibleRegion);
        }
        finally
        {
            vm.PreviewImage = null;
            window.DataContext = null;
            window.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    private static Rect AssertRect(Rect expected, Rect? actual)
    {
        var value = Assert.IsType<Rect>(actual);
        Assert.Equal(expected.X, value.X, precision: 6);
        Assert.Equal(expected.Y, value.Y, precision: 6);
        Assert.Equal(expected.Width, value.Width, precision: 6);
        Assert.Equal(expected.Height, value.Height, precision: 6);
        return value;
    }

    private static Rect AssertRect(Rect expected, Rect actual) =>
        AssertRect(expected, (Rect?)actual);

    private static Point CenterOf(Control control, Visual relativeTo)
    {
        var origin = control.TranslatePoint(default, relativeTo)!.Value;
        return origin + new Vector(
            control.Bounds.Width / 2,
            control.Bounds.Height / 2);
    }

    private static Window Show(Control content, double width, double height)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = content
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
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
        $"happy-photon-navigator-{Guid.NewGuid():N}")).FullName;

    private static MainWindowViewModel CreateViewModel(CatalogService catalog) =>
        new(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);
}
