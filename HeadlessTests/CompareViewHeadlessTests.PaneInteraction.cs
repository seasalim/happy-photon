using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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

public sealed partial class CompareViewHeadlessTests
{
    [AvaloniaFact]
    public async Task RealPaneZoomPanAndResizePropagateOnceAndKeepOneActiveRing()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var images = new[]
        {
            new ImageFile(Path.Combine(root.Path, "landscape.jpg")),
            new ImageFile(Path.Combine(root.Path, "portrait.jpg"))
        };
        images[0].ApplyMetadata(new ImageMetadata
        {
            PixelWidth = 2400,
            PixelHeight = 1600
        });
        images[1].ApplyMetadata(new ImageMetadata
        {
            PixelWidth = 600,
            PixelHeight = 2400
        });
        vm.Browse.SetImages(images);
        vm.SelectedImage = images[0];
        foreach (var image in images) vm.ToggleImageSelection(image);
        vm.EnterCompareCommand.Execute(null);
        await vm.CompareLoadingTask;
        using var landscape = CreateBitmap(1200, 800);
        using var portrait = CreateBitmap(300, 1200);
        vm.ComparePanes[0].Preview = landscape;
        vm.ComparePanes[1].Preview = portrait;
        vm.ComparePanes[0].RenderedLongEdge = 1200;
        vm.ComparePanes[1].RenderedLongEdge = 1200;
        vm.ComparePanes[0].PreviewResolutionBitmap = landscape;
        vm.ComparePanes[1].PreviewResolutionBitmap = portrait;
        vm.ComparePanes[0].PreviewResolutionLongEdge = 1200;
        vm.ComparePanes[1].PreviewResolutionLongEdge = 1200;

        var window = new MainWindow
        {
            Width = 1100,
            Height = 720,
            DataContext = vm
        };
        window.Show();
        Drain();

        try
        {
            var compare = Descendant<CompareView>(window, "CompareView");
            var panes = compare.GetVisualDescendants()
                .OfType<ZoomPanControl>()
                .ToArray();
            Assert.Equal(2, panes.Length);
            Assert.All(panes, pane => Assert.True(pane.AutoFit));
            Assert.Equal(new PixelSize(2400, 1600),
                panes[0].OriginalViewPixelSize);
            Assert.Equal(new PixelSize(600, 2400),
                panes[1].OriginalViewPixelSize);
            AssertOneActiveRing(compare, images[0]);

            var notifications = 0;
            vm.SynchronizedView.ViewportChanged += (_, _) => notifications++;
            var pointer = CenterOf(panes[0], window);
            window.MouseWheel(pointer, new Vector(0, 1), RawInputModifiers.None);
            Drain();

            Assert.Equal(MainWindowViewModel.ZoomStepFactor,
                vm.SynchronizedView.Viewport.ZoomRelativeToFit, 8);
            Assert.Equal(1, notifications);
            AssertSynchronized(panes, vm.SynchronizedView.Viewport);
            Assert.All(panes, pane => Assert.False(pane.AutoFit));
            AssertSettled(notifications, () => notifications);

            var landscapeBounds = panes[0].GetNormalizedCenterBounds(
                MainWindowViewModel.ZoomStepFactor);
            var portraitBounds = panes[1].GetNormalizedCenterBounds(
                MainWindowViewModel.ZoomStepFactor);
            Assert.True(landscapeBounds.MinimumX < 0.5);
            Assert.Equal(0.5, portraitBounds.MinimumX, 8);
            var modestPan = pointer - new Vector(24, 0);
            window.MouseDown(pointer, MouseButton.Middle, RawInputModifiers.None);
            window.MouseMove(modestPan, RawInputModifiers.MiddleMouseButton);
            window.MouseUp(modestPan, MouseButton.Middle, RawInputModifiers.None);
            Drain();
            Assert.Equal(0.5, vm.SynchronizedView.Viewport.Center.X, 8);
            AssertSynchronized(panes, vm.SynchronizedView.Viewport);
            Assert.Equal(1, notifications);

            vm.SynchronizedView.SetViewport(new NormalizedViewport(
                new NormalizedPoint(0.5, 0.5),
                4));
            Drain();
            Assert.Equal(2, notifications);
            AssertSynchronized(panes, vm.SynchronizedView.Viewport);
            var beforePan = vm.SynchronizedView.Viewport.Center;
            var end = pointer - new Vector(24, 16);
            window.MouseDown(pointer, MouseButton.Middle, RawInputModifiers.None);
            window.MouseMove(end, RawInputModifiers.MiddleMouseButton);
            window.MouseUp(end, MouseButton.Middle, RawInputModifiers.None);
            Drain();

            Assert.NotEqual(beforePan, vm.SynchronizedView.Viewport.Center);
            Assert.Equal(3, notifications);
            AssertSynchronized(panes, vm.SynchronizedView.Viewport);
            AssertSettled(notifications, () => notifications);

            window.Width += 180;
            window.Height += 80;
            Drain();
            AssertSynchronized(panes, vm.SynchronizedView.Viewport);

            vm.SynchronizedView.Reset();
            Drain();
            Assert.All(panes, pane => Assert.True(pane.AutoFit));
            window.Width -= 120;
            Drain();
            AssertSynchronized(panes, NormalizedViewport.Fit);

            var clock = new TestTimeProvider();
            foreach (var pane in panes) pane.SetLoupeTimeProvider(clock);
            pointer = CenterOf(panes[0], window);
            var beforeLoupe = panes.Select(pane =>
                pane.CaptureNormalizedViewport().Center).ToArray();
            window.MouseDown(pointer, MouseButton.Left, RawInputModifiers.None);
            clock.Advance(TimeSpan.FromMilliseconds(150));
            Drain();
            Assert.All(panes, pane => Assert.True(pane.IsLoupePeekActive));
            vm.ComparePanes[0].Preview = CreateBitmap(2400, 1600);
            vm.ComparePanes[1].Preview = CreateBitmap(600, 2400);
            foreach (var pane in vm.ComparePanes) pane.RenderedLongEdge = 2400;
            var loupeMove = pointer + new Vector(36, 20);
            window.MouseMove(loupeMove, RawInputModifiers.LeftMouseButton);
            Drain();
            var movedLoupe = panes.Select(pane =>
                pane.CaptureNormalizedViewport().Center).ToArray();
            Assert.NotEqual(beforeLoupe[0], movedLoupe[0]);
            Assert.InRange(Math.Abs(movedLoupe[0].X - movedLoupe[1].X), 0, 0.01);
            Assert.InRange(Math.Abs(movedLoupe[0].Y - movedLoupe[1].Y), 0, 0.01);
            window.MouseUp(loupeMove, MouseButton.Left, RawInputModifiers.None);
            Drain();
            Assert.All(panes, pane => Assert.False(pane.IsLoupePeekActive));
            Assert.Same(landscape, vm.ComparePanes[0].Preview);
            Assert.Same(portrait, vm.ComparePanes[1].Preview);
            Assert.All(vm.ComparePanes, pane =>
                Assert.Equal(1200, pane.RenderedLongEdge));
            AssertSynchronized(panes, NormalizedViewport.Fit);

            var secondPanePoint = CenterOf(panes[1], window);
            window.MouseDown(
                secondPanePoint,
                MouseButton.Left,
                RawInputModifiers.None);
            window.MouseUp(
                secondPanePoint,
                MouseButton.Left,
                RawInputModifiers.None);
            Drain();
            AssertOneActiveRing(compare, images[1]);
        }
        finally
        {
            foreach (var pane in vm.ComparePanes)
            {
                pane.PreviewResolutionBitmap = null;
                pane.Preview = null;
            }
            window.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task QuickReentry_ResumesSerializedPaneUpdatesOnUiThread()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            _ => Task.CompletedTask);
        var images = new[]
        {
            new ImageFile(Path.Combine(root.Path, "first.jpg")),
            new ImageFile(Path.Combine(root.Path, "second.jpg"))
        };
        vm.Browse.SetImages(images);
        vm.SelectedImage = images[0];
        foreach (var image in images) vm.ToggleImageSelection(image);
        var gateEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ImageService.Previews.CachedPreviewGateAsync = () =>
        {
            gateEntered.TrySetResult();
            return releaseGate.Task;
        };

        vm.EnterCompareCommand.Execute(null);
        await gateEntered.Task.WaitAsync(TestWaits.Condition);
        vm.ExitCompareCommand.Execute(null);
        vm.EnterCompareCommand.Execute(null);
        var updatedOffThread = 0;
        foreach (var pane in vm.ComparePanes)
        {
            pane.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(ComparePaneViewModel.Preview) or
                    nameof(ComparePaneViewModel.IsLoading) &&
                    !Dispatcher.UIThread.CheckAccess())
                {
                    Interlocked.Exchange(ref updatedOffThread, 1);
                }
            };
        }

        releaseGate.TrySetResult();
        await vm.CompareLoadingTask.WaitAsync(TestWaits.Condition);

        Assert.Equal(0, Volatile.Read(ref updatedOffThread));
        Assert.All(vm.ComparePanes, pane => Assert.False(pane.IsLoading));
    }

    private static void AssertSynchronized(
        IEnumerable<ZoomPanControl> panes,
        NormalizedViewport expected)
    {
        foreach (var pane in panes)
        {
            var actual = pane.CaptureNormalizedViewport();
            Assert.True(
                Math.Abs(actual.ZoomRelativeToFit - expected.ZoomRelativeToFit) <= 0.01 &&
                Math.Abs(actual.Center.X - expected.Center.X) <= 0.01 &&
                Math.Abs(actual.Center.Y - expected.Center.Y) <= 0.01,
                $"Expected {expected}; captured {actual}.");
        }
    }

    private static void AssertOneActiveRing(CompareView compare, ImageFile active)
    {
        var paneBorders = compare.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("compare-pane"))
            .ToArray();
        var ring = Assert.Single(
            paneBorders,
            border => border.Classes.Contains("active"));
        Assert.Same(active, ((ComparePaneViewModel)ring.DataContext!).Image);
        Assert.All(paneBorders, border =>
        {
            Assert.Equal(1, border.BorderThickness.Left);
            Assert.Equal(new CornerRadius(14), border.CornerRadius);
            Assert.Equal(
                new CornerRadius(13),
                Assert.IsType<Border>(border.Child).CornerRadius);
        });
    }

    private static void AssertSettled(
        int settled,
        Func<int> getNotifications)
    {
        Drain();
        Drain();
        Assert.Equal(settled, getNotifications());
    }

    private static Point CenterOf(Control control, Control relativeTo) =>
        control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            relativeTo)!.Value;

    private static Bitmap CreateBitmap(int width, int height)
    {
        using var image = new MagickImage(MagickColors.Gray, (uint)width, (uint)height);
        return BitmapConversionService.ConvertToBitmap(image)!;
    }

    [AvaloniaTheory]
    [InlineData(Key.Escape, PhysicalKey.Escape, null)]
    [InlineData(Key.G, PhysicalKey.G, "g")]
    public async Task CompareExitsOnKeyboard(
        Key key,
        PhysicalKey physicalKey,
        string? keyText)
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var images = new[]
        {
            new ImageFile(Path.Combine(root.Path, "first.jpg")),
            new ImageFile(Path.Combine(root.Path, "second.jpg"))
        };
        var states = await catalog.LoadOrCreateImageStatesAsync(
            images.Select(image => image.FilePath).ToArray());
        foreach (var image in images)
            image.CatalogId = states[image.FilePath].Single().CatalogId;
        vm.Browse.SetImages(images);
        vm.SelectedImage = images[0];
        foreach (var image in images) vm.ToggleImageSelection(image);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Drain();

        try
        {
            vm.ToggleCompareCommand.Execute(null);
            Drain();
            Drain();
            Assert.True(vm.IsCompareMode);
            var compare = Descendant<CompareView>(window, "CompareView");

            // Drive the real input pipeline rather than raising KeyDown on the
            // focused element: a synthetic raise never evaluates the window's
            // KeyBindings, so a G binding could be dead and the test still pass.
            // It still proves focus is alive, since the pipeline routes through
            // the focused element.
            Assert.True(compare.IsKeyboardFocusWithin);
            window.KeyPress(key, RawInputModifiers.None, physicalKey, keyText);
            Drain();
            Drain();

            Assert.False(vm.IsCompareMode, $"{key} did not leave compare.");
            Assert.True(vm.IsBrowseGridVisible);
            Assert.False(
                Descendant<ToggleButton>(window, "CompareViewButton").IsChecked);
            Assert.All(images, image => Assert.True(image.IsSelected));
        }
        finally
        {
            window.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ArrowsMoveTheActivePaneAcrossRowsAndColumns()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var images = Enumerable.Range(0, 4)
            .Select(index => new ImageFile(
                Path.Combine(root.Path, $"image{index}.jpg")))
            .ToArray();
        var states = await catalog.LoadOrCreateImageStatesAsync(
            images.Select(image => image.FilePath).ToArray());
        foreach (var image in images)
            image.CatalogId = states[image.FilePath].Single().CatalogId;
        vm.Browse.SetImages(images);
        vm.SelectedImage = images[0];
        foreach (var image in images) vm.ToggleImageSelection(image);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Drain();

        try
        {
            vm.ToggleCompareCommand.Execute(null);
            Drain();
            Drain();
            Assert.True(vm.IsCompareMode);
            Assert.Equal(4, vm.ComparePanes.Count);
            Assert.Same(images[0], vm.SelectedImage);

            // The 2x2 reads 0 1 / 2 3, so a row step is two panes.
            Press(window, Key.Down, PhysicalKey.ArrowDown);
            Assert.Same(images[2], vm.SelectedImage);
            Press(window, Key.Right, PhysicalKey.ArrowRight);
            Assert.Same(images[3], vm.SelectedImage);
            Press(window, Key.Up, PhysicalKey.ArrowUp);
            Assert.Same(images[1], vm.SelectedImage);

            // Nothing above the top row or below the bottom one: the ring holds
            // still rather than sliding sideways.
            Press(window, Key.Up, PhysicalKey.ArrowUp);
            Assert.Same(images[1], vm.SelectedImage);
            Press(window, Key.Down, PhysicalKey.ArrowDown);
            Press(window, Key.Down, PhysicalKey.ArrowDown);
            Assert.Same(images[3], vm.SelectedImage);

            // Never leaves the compare set, and never collapses the selection.
            Assert.True(vm.IsCompareMode);
            Assert.All(images, image => Assert.True(image.IsSelected));
        }
        finally
        {
            window.DataContext = null;
            window.Close();
        }
    }

    private static void Press(MainWindow window, Key key, PhysicalKey physicalKey)
    {
        window.KeyPress(key, RawInputModifiers.None, physicalKey, null);
        Drain();
        Drain();
    }
}
