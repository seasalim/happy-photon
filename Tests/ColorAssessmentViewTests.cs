using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ColorAssessmentViewTests
{
    [AvaloniaFact]
    public void ModeOffFitAndBounds_ReturnExactlyAfterAssessmentRoundTrip()
    {
        using var bitmap = LoadBitmap();
        var viewer = new ZoomPanControl { Source = bitmap };
        var window = Show(viewer, 800, 600);
        try
        {
            var scroll = viewer.FindControl<ScrollViewer>("ScrollViewer")!;
            var panel = viewer.FindControl<Panel>("ImagePanel")!;
            var baselineFit = Math.Min(
                scroll.Viewport.Width / bitmap.PixelSize.Width,
                scroll.Viewport.Height / bitmap.PixelSize.Height);
            var baselineBounds = panel.Bounds;

            Assert.Equal(baselineFit, viewer.GetFitZoomLevel());

            viewer.IsColorAssessment = true;
            Dispatcher.UIThread.RunJobs();
            viewer.IsColorAssessment = false;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(baselineFit, viewer.GetFitZoomLevel());
            Assert.Equal(baselineBounds, panel.Bounds);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AssessmentField_KeepsConstantBandAndComposition()
    {
        using var bitmap = LoadBitmap();
        var viewer = new ZoomPanControl
        {
            Source = bitmap,
            IsCropMode = true,
            IsWhiteBalancePicking = true,
            IsColorAssessment = true
        };
        var window = Show(viewer, 1000, 800);
        try
        {
            var scroll = viewer.FindControl<ScrollViewer>("ScrollViewer")!;
            var surround = viewer.FindControl<Panel>("SurroundLayer")!;
            var mat = viewer.FindControl<Border>("AssessmentMat")!;
            var imagePanel = viewer.FindControl<Panel>("ImagePanel")!;
            var image = viewer.FindControl<Image>("ImageControl")!;
            var crop = viewer.FindControl<CropOverlayControl>("CropOverlay")!;
            var expected = ColorAssessmentGeometry.Calculate(
                scroll.Viewport,
                isColorAssessment: true);

            Assert.True(expected.IsFieldVisible);
            Assert.Contains("assessment-on", surround.Classes);
            Assert.Contains("assessment-on", mat.Classes);
            Assert.Equal(new Thickness(expected.BandWidth), mat.Padding);
            Assert.Same(imagePanel, mat.Child);
            Assert.Contains(image, imagePanel.Children);
            Assert.Contains(crop, imagePanel.Children);
            Assert.True(crop.IsVisible);
            Assert.NotNull(viewer.Cursor);

            viewer.ZoomLevel = 0.5;
            var halfZoomPadding = mat.Padding;
            viewer.ZoomLevel = 2;

            Assert.Equal(halfZoomPadding, mat.Padding);
            Assert.Equal(new Thickness(expected.BandWidth), mat.Padding);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SourceNull_SuppressesFieldAndLeavesPlaceholderPathClear()
    {
        var viewer = new ZoomPanControl
        {
            IsColorAssessment = true
        };
        var window = Show(viewer, 800, 600);
        try
        {
            var surround = viewer.FindControl<Panel>("SurroundLayer")!;
            var mat = viewer.FindControl<Border>("AssessmentMat")!;

            Assert.DoesNotContain("assessment-on", surround.Classes);
            Assert.DoesNotContain("assessment-on", mat.Classes);
            Assert.Null(surround.Background);
            Assert.Null(mat.Background);
            Assert.Equal(default, mat.Padding);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void LiveThemeSwitch_PreservesReferenceBrushInstances()
    {
        var application = Application.Current!;
        application.RequestedThemeVariant = ThemeVariant.Dark;
        using var bitmap = LoadBitmap();
        var viewer = new ZoomPanControl
        {
            Source = bitmap,
            IsColorAssessment = true
        };
        var window = Show(viewer, 800, 600);
        try
        {
            var surround = viewer.FindControl<Panel>("SurroundLayer")!;
            var mat = viewer.FindControl<Border>("AssessmentMat")!;
            var gray = ThemeResourceTests.Brush(
                "AssessmentGray",
                ThemeVariant.Dark);
            var white = ThemeResourceTests.Brush(
                "AssessmentWhite",
                ThemeVariant.Dark);

            Assert.Same(gray, surround.Background);
            Assert.Same(white, mat.Background);

            application.RequestedThemeVariant = HappyPhotonThemes.MidGray;
            Dispatcher.UIThread.RunJobs();

            Assert.Same(gray, surround.Background);
            Assert.Same(white, mat.Background);
            Assert.Same(
                gray,
                ThemeResourceTests.Brush(
                    "AssessmentGray",
                    HappyPhotonThemes.MidGray));
            Assert.Same(
                white,
                ThemeResourceTests.Brush(
                    "AssessmentWhite",
                    HappyPhotonThemes.MidGray));
        }
        finally
        {
            application.RequestedThemeVariant = ThemeVariant.Dark;
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(ScrollBarVisibility.Auto)]
    [InlineData(ScrollBarVisibility.Hidden)]
    public void OverflowPansWithLeftAndMiddleDrag(
        ScrollBarVisibility scrollBarVisibility)
    {
        using var bitmap = LoadBitmap();
        var viewer = new ZoomPanControl
        {
            Source = bitmap,
            ZoomLevel = 2,
            ScrollBarVisibility = scrollBarVisibility
        };
        var window = Show(viewer, 400, 300);
        try
        {
            var scroll = viewer.FindControl<ScrollViewer>("ScrollViewer")!;
            Assert.True(scroll.Extent.Width > scroll.Viewport.Width ||
                        scroll.Extent.Height > scroll.Viewport.Height);
            Assert.True(viewer.CanPanContent());

            AssertDragMovesOffset(
                window,
                scroll,
                MouseButton.Left,
                RawInputModifiers.LeftMouseButton);
            scroll.Offset = default;
            Dispatcher.UIThread.RunJobs();
            AssertDragMovesOffset(
                window,
                scroll,
                MouseButton.Middle,
                RawInputModifiers.MiddleMouseButton);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task MainWindow_ToggleRefitsAndEntryPointsStayBound()
    {
        var root = NewRoot();
        using var catalog = new CatalogService(Path.Combine(root, "catalog"));
        await using var vm = CreateViewModel(catalog);
        using var bitmap = LoadBitmap();
        var image = new ImageFile(Path.Combine(root, "photo.jpg"));
        vm.SelectedImage = image;
        vm.IsDevelopMode = true;
        vm.PreviewImage = bitmap;
        vm.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var window = new MainWindow { DataContext = vm };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var pane = window.FindControl<DevelopViewerPane>(
                "DevelopViewerPane")!;
            var viewer = pane.Viewer;
            var button = pane.FindControl<ToggleButton>(
                "ColorAssessmentButton")!;
            var binding = Assert.Single(
                window.KeyBindings,
                keyBinding => keyBinding.Gesture.ToString() == "L");

            Assert.Same(vm.ToggleColorAssessmentModeCommand, button.Command);
            Assert.Same(vm.ToggleColorAssessmentModeCommand, binding.Command);
            Assert.False(button.IsChecked);
            var plainFit = viewer.GetFitZoomLevel();
            vm.ZoomLevel = 1;

            vm.ToggleColorAssessmentModeCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(button.IsChecked);
            Assert.Equal(viewer.GetFitZoomLevel(), vm.ZoomLevel);
            Assert.NotEqual(plainFit, vm.ZoomLevel);
            Assert.Same(bitmap, vm.PreviewImage);

            vm.ZoomLevel = 1;
            vm.ToggleColorAssessmentModeCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.False(button.IsChecked);
            Assert.Equal(plainFit, vm.ZoomLevel);
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
    public async Task FullScreenRoundTrip_FitsOnlyActiveViewer()
    {
        var root = NewRoot();
        using var catalog = new CatalogService(Path.Combine(root, "catalog"));
        await using var vm = CreateViewModel(catalog);
        using var bitmap = LoadBitmap();
        vm.SelectedImage = new ImageFile(Path.Combine(root, "photo.jpg"));
        vm.IsDevelopMode = true;
        vm.PreviewImage = bitmap;
        var window = new MainWindow { DataContext = vm };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var develop = window.FindControl<DevelopViewerPane>(
                "DevelopViewerPane")!.Viewer;
            var fullScreen = window.FindControl<ZoomPanControl>(
                "FullScreenZoomPanControl")!;
            Assert.Equal(ScrollBarVisibility.Hidden, fullScreen.ScrollBarVisibility);

            vm.IsFullScreenMode = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(fullScreen.GetFitZoomLevel(), vm.ZoomLevel);
            AssertInactiveEventsAreIgnored(window, develop, vm);

            vm.IsFullScreenMode = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(develop.GetFitZoomLevel(), vm.ZoomLevel);
            AssertInactiveEventsAreIgnored(window, fullScreen, vm);
        }
        finally
        {
            vm.PreviewImage = null;
            window.DataContext = null;
            window.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertDragMovesOffset(
        Window window,
        ScrollViewer scroll,
        MouseButton button,
        RawInputModifiers heldModifier)
    {
        var start = new Point(220, 170);
        var end = new Point(120, 90);
        window.MouseDown(start, button, RawInputModifiers.None);
        window.MouseMove(end, heldModifier);
        window.MouseUp(end, button, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.True(scroll.Offset.X > 0 || scroll.Offset.Y > 0);
    }

    private static void AssertInactiveEventsAreIgnored(
        MainWindow window,
        ZoomPanControl inactive,
        MainWindowViewModel vm)
    {
        vm.ZoomLevel = 1;
        Invoke(window, "OnZoomChanged", inactive, 1d);
        Assert.Equal(1, vm.ZoomLevel);
        Invoke(window, "OnAutoFitRequested", inactive, 0.25d);
        Assert.Equal(1, vm.ZoomLevel);
    }

    private static void Invoke(
        MainWindow window,
        string name,
        object sender,
        double value) =>
        typeof(MainWindow).GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(window, [sender, value]);

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

    private static Bitmap LoadBitmap() => new(Path.Combine(
        GoldenTestPaths.AssetDirectory,
        "srgb-reference.jpg"));

    private static string NewRoot() => Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-assessment-view-{Guid.NewGuid():N}")).FullName;

    private static MainWindowViewModel CreateViewModel(CatalogService catalog) =>
        new(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);
}
