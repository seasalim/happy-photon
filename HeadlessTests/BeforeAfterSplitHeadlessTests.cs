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

public sealed partial class BeforeAfterSplitHeadlessTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("before-after-split");

    [Trait("Category", "Quarantined")]
    [AvaloniaFact]
    public async Task ToggleRendersAndSynchronizesBeforeWhileAfterStaysLive()
    {
        using var catalog = await _fx.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        var loader = new GrayLoader();
        await using var vm = _fx.CreateViewModel(
            catalog,
            loader,
            _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            timeProvider: clock);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.IsDevelopMode = true;
        var image = new ImageFile(_fx.Path("first.jpg"))
        {
            EditSettings = new EditSettings { Exposure = 0.5 }
        };
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null);

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
            var pane = Descendant<DevelopViewerPane>(window, "DevelopViewerPane");
            var after = pane.Viewer;
            var split = Descendant<ToggleButton>(window, "BeforeAfterSplitButton");
            var eye = Descendant<ToggleButton>(window, "BeforeAfterButton");
            Assert.Equal("Y|Y", Assert.IsType<TextBlock>(split.Content).Text);
            Assert.Equal("Before | After (Y)", ToolTip.GetTip(split));
            Assert.Equal("Toggle original preview (B or \\)", ToolTip.GetTip(eye));
            Assert.Contains(ShortcutCatalog.Groups.SelectMany(group => group.Entries),
                entry => entry.Keys == "Y" &&
                    entry.Reachability.Any(claim =>
                        claim.ControlName == "BeforeAfterSplitButton"));

            vm.SelectedScope = ScopeView.Waveform;
            await TestWaits.UntilAsync(() => vm.EffectiveWaveform != null);
            vm.ToggleClippingOverlayCommand.Execute(null);
            await TestWaits.UntilAsync(() => vm.PreviewClippingMask != null);
            var histogram = vm.Histogram;
            var waveform = vm.EffectiveWaveform;
            var rawHistogram = vm.RawHistogram;
            var clipping = vm.DisplayClippingStats;
            var clippingMask = vm.PreviewClippingMask;
            vm.ApplyManualZoom(2);
            Drain();
            var decodeCount = loader.DecodeCount;
            var renderGeneration = RenderGeneration(vm);
            window.KeyPress(Key.Y, RawInputModifiers.None, PhysicalKey.Y, "y");
            await TestWaits.UntilAsync(() =>
                vm.IsBeforeAfterSplit && vm.BeforeAfterPreviewImage != null);
            Drain();

            Assert.True(split.IsChecked);
            Assert.False(vm.ToggleBeforeAfterCommand.CanExecute(null));
            Assert.Equal(decodeCount, loader.DecodeCount);
            Assert.Equal(renderGeneration, RenderGeneration(vm));
            var panes = pane.GetVisualDescendants().OfType<ZoomPanControl>().ToArray();
            Assert.Equal(2, panes.Length);
            var before = panes.Single(control => !ReferenceEquals(control, after));
            Assert.Equal(vm.ZoomLevel, after.ZoomLevel, 8);
            AssertClose(after.CaptureNormalizedViewport(), before.CaptureNormalizedViewport());
            window.Width += 120;
            window.Height += 40;
            window.UpdateLayout();
            Drain();
            await TestWaits.UntilAsync(() =>
                vm.BeforeAfterPreviewImage?.PixelSize.Width == 1280 &&
                BeforeAfterRenderIsIdle(vm));
            Drain();
            Assert.Equal(vm.ZoomLevel, after.ZoomLevel, 8);
            AssertClose(after.CaptureNormalizedViewport(), before.CaptureNormalizedViewport());
            Assert.True(Descendant<TextBlock>(pane, text: "BEFORE").IsVisible);
            Assert.True(Descendant<TextBlock>(pane, text: "AFTER").IsVisible);
            Assert.Same(histogram, vm.Histogram);
            Assert.Same(waveform, vm.EffectiveWaveform);
            Assert.Same(rawHistogram, vm.RawHistogram);
            Assert.Same(clipping, vm.DisplayClippingStats);
            Assert.Same(clippingMask, vm.PreviewClippingMask);

            var priorViewport = after.CaptureNormalizedViewport();
            var panStart = CenterOf(before, window);
            var panEnd = panStart - new Vector(32, 20);
            window.MouseDown(panStart, MouseButton.Middle, RawInputModifiers.None);
            window.MouseMove(panEnd, RawInputModifiers.MiddleMouseButton);
            window.MouseUp(panEnd, MouseButton.Middle, RawInputModifiers.None);
            Drain();
            Assert.NotEqual(priorViewport.Center, after.CaptureNormalizedViewport().Center);
            AssertClose(after.CaptureNormalizedViewport(), before.CaptureNormalizedViewport());

            var beforeBitmap = vm.BeforeAfterPreviewImage;
            vm.Exposure = 1;
            clock.Advance(TimeSpan.FromMilliseconds(150));
            await TestWaits.UntilAsync(() => image.EditSettings.Exposure == 1);
            Assert.Same(beforeBitmap, vm.BeforeAfterPreviewImage);
            Assert.Equal(decodeCount, loader.DecodeCount);

            vm.ZoomFitCommand!.Execute(null);
            Drain();
            var priorZoom = vm.ZoomLevel;
            window.MouseWheel(CenterOf(before, window), new Vector(0, 1),
                RawInputModifiers.None);
            Drain();
            Assert.True(vm.ZoomLevel > priorZoom);
            AssertClose(after.CaptureNormalizedViewport(), before.CaptureNormalizedViewport());

            var loupeClock = new TestTimeProvider();
            before.SetLoupeTimeProvider(loupeClock);
            after.SetLoupeTimeProvider(loupeClock);
            var beforeRestore = before.CaptureNormalizedViewport();
            var afterRestore = after.CaptureNormalizedViewport();
            var point = before.TranslatePoint(new Point(
                before.Bounds.Width * 0.35, before.Bounds.Height * 0.65), window)!.Value;
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
            loupeClock.Advance(TimeSpan.FromMilliseconds(150));
            Drain();
            Assert.True(before.IsLoupePeekActive);
            Assert.True(after.IsLoupePeekActive);
            await AssertEventuallyCloseAsync(before, after);
            var dragPoint = point + new Vector(24, -16);
            window.MouseMove(dragPoint, RawInputModifiers.LeftMouseButton);
            Drain();
            await AssertEventuallyCloseAsync(before, after);
            window.MouseUp(dragPoint, MouseButton.Left, RawInputModifiers.None);
            Drain();
            AssertClose(beforeRestore, before.CaptureNormalizedViewport());
            AssertClose(afterRestore, after.CaptureNormalizedViewport());
            Assert.Equal(decodeCount, loader.DecodeCount);

            vm.RotateRightCommand.Execute(null);
            clock.Advance(TimeSpan.FromMilliseconds(150));
            await TestWaits.UntilAsync(() =>
                vm.BeforeAfterPreviewImage != null &&
                !ReferenceEquals(beforeBitmap, vm.BeforeAfterPreviewImage));
            Assert.True(vm.IsBeforeAfterSplit);

            vm.HandleEscapeCommand.Execute(null);
            Assert.False(vm.IsBeforeAfterSplit);
            Assert.Null(vm.BeforeAfterPreviewImage);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task LargerRefinementSupersedesInFlightSameSettingsRender()
    {
        using var catalog = await _fx.CreateCatalogAsync("larger-refinement");
        var clock = new TestTimeProvider();
        await using var vm = _fx.CreateViewModel(
            catalog,
            new GrayLoader(),
            _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            timeProvider: clock);
        vm.IsDevelopMode = true;
        vm.SelectedImage = new ImageFile(_fx.Path("larger-refinement.jpg"));
        await TestWaits.UntilAsync(() => vm.PreviewImage != null);
        await vm.ToggleBeforeAfterSplitCommand.ExecuteAsync(null);
        await TestWaits.UntilAsync(() => vm.BeforeAfterPreviewImage != null);
        var interactive = vm.BeforeAfterPreviewImage;
        Assert.Equal(640, Math.Max(
            interactive!.PixelSize.Width, interactive.PixelSize.Height));

        var started = new[] { NewSignal(), NewSignal() };
        var release = NewSignal();
        var gateIndex = -1;
        try
        {
            vm.ImageService.Previews.SideSurfaceRenderGateAsync = () =>
            {
                var index = Interlocked.Increment(ref gateIndex);
                started[index].TrySetResult();
                return release.Task;
            };
            vm.RotateRightCommand.Execute(null);
            clock.Advance(TimeSpan.FromMilliseconds(150));
            await started[0].Task.WaitAsync(TestWaits.Condition);

            vm.PublishBeforeAfterRequiredDeviceLongEdge(3200);
            await started[1].Task.WaitAsync(TestWaits.Condition);
            release.TrySetResult();
            await TestWaits.UntilAsync(() =>
                vm.BeforeAfterPreviewImage != null &&
                !ReferenceEquals(interactive, vm.BeforeAfterPreviewImage) &&
                Math.Max(vm.BeforeAfterPreviewImage.PixelSize.Width,
                    vm.BeforeAfterPreviewImage.PixelSize.Height) == 1280);
        }
        finally
        {
            release.TrySetResult();
            vm.ImageService.Previews.SideSurfaceRenderGateAsync = null;
        }
    }

    [AvaloniaFact]
    public async Task SplitEntryRestoresEditedAfterFromPaintedAndPendingOriginal()
    {
        using var catalog = await _fx.CreateCatalogAsync("entry-original");
        await using var vm = _fx.CreateViewModel(
            catalog,
            new GrayLoader(),
            _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        var image = new ImageFile(_fx.Path("entry-original.jpg"))
        {
            EditSettings = new EditSettings { Exposure = 1 },
            HasEdits = true
        };
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null);

        await vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
        var paintedOriginal = vm.PreviewImage;
        Assert.True(vm.IsShowingOriginal);

        await vm.ToggleBeforeAfterSplitCommand.ExecuteAsync(null);
        await TestWaits.UntilAsync(() => vm.BeforeAfterPreviewImage != null);
        Assert.True(vm.IsBeforeAfterSplit);
        Assert.False(vm.IsShowingOriginal);
        Assert.NotSame(paintedOriginal, vm.PreviewImage);
        Assert.Equal(RenderSettingsHash.Compute(image.EditSettings), PaintedHash(vm));
        await vm.ToggleBeforeAfterSplitCommand.ExecuteAsync(null);

        var started = new[] { NewSignal(), NewSignal() };
        var release = new[] { NewSignal(), NewSignal() };
        var gateIndex = -1;
        Task? original = null;
        try
        {
            vm.ImageService.Previews.RenderGateAsync = () =>
            {
                var index = Interlocked.Increment(ref gateIndex);
                started[index].TrySetResult();
                return release[index].Task;
            };
            original = vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
            await started[0].Task.WaitAsync(TestWaits.Condition);
            var split = vm.ToggleBeforeAfterSplitCommand.ExecuteAsync(null);
            await started[1].Task.WaitAsync(TestWaits.Condition);
            release[1].TrySetResult();
            await split;
            await TestWaits.UntilAsync(() => vm.BeforeAfterPreviewImage != null);

            Assert.True(vm.IsBeforeAfterSplit);
            Assert.False(vm.IsShowingOriginal);
            Assert.Equal(RenderSettingsHash.Compute(image.EditSettings), PaintedHash(vm));
            release[0].TrySetResult();
            await original;
            Assert.False(vm.IsShowingOriginal);
        }
        finally
        {
            foreach (var signal in release) signal.TrySetResult();
            if (original != null) await original;
            vm.ImageService.Previews.RenderGateAsync = null;
        }
    }

    [AvaloniaFact]
    public async Task SelectionPersistsSplitAndTransientModesExitIt()
    {
        using var catalog = await _fx.CreateCatalogAsync();
        await using var vm = _fx.CreateViewModel(
            catalog,
            new GrayLoader(),
            _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        var first = new ImageFile(_fx.Path("first.jpg"))
        {
            EditSettings = new EditSettings { Exposure = 0.5 }
        };
        var second = new ImageFile(_fx.Path("second.jpg"))
        {
            EditSettings = new EditSettings { Exposure = 0.75 }
        };
        vm.Browse.SetImages([first, second]);
        vm.SelectedImage = first;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null);

        await vm.ToggleBeforeAfterSplitCommand.ExecuteAsync(null);
        await TestWaits.UntilAsync(() => vm.BeforeAfterPreviewImage != null);
        var firstBefore = vm.BeforeAfterPreviewImage;
        vm.SelectedImage = second;
        await TestWaits.UntilAsync(() =>
            vm.BeforeAfterPreviewImage != null &&
            !ReferenceEquals(firstBefore, vm.BeforeAfterPreviewImage));
        Assert.True(vm.IsBeforeAfterSplit);

        vm.ToggleCropModeCommand.Execute(null);
        Assert.True(vm.IsCropMode);
        Assert.False(vm.IsBeforeAfterSplit);
        vm.CancelCropCommand.Execute(null);

        await vm.ToggleBeforeAfterSplitCommand.ExecuteAsync(null);
        await TestWaits.UntilAsync(() => vm.BeforeAfterPreviewImage != null);
        vm.IsFullScreenMode = true;
        Assert.False(vm.IsBeforeAfterSplit);
        vm.IsFullScreenMode = false;

        await vm.ToggleBeforeAfterSplitCommand.ExecuteAsync(null);
        await TestWaits.UntilAsync(() => vm.BeforeAfterPreviewImage != null);
        vm.IsDevelopMode = false;
        Assert.False(vm.IsBeforeAfterSplit);
    }

    [AvaloniaFact]
    public async Task BackslashBindingsShareTheEyeCommand()
    {
        using var catalog = _fx.CreateCatalog();
        await using var vm = _fx.CreateViewModel(catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            foreach (var key in new[] { Key.Oem5, Key.OemBackslash })
            {
                var binding = Assert.Single(
                    window.KeyBindings,
                    item => item.Gesture.Key == key);
                Assert.Same(vm.ToggleBeforeAfterCommand, binding.Command);
            }
        }
        finally
        {
            window.DataContext = null;
            window.Close();
        }
    }

    public void Dispose() => _fx.Dispose();

    [AvaloniaFact]
    public async Task SplitToggleSitsBesideFullScreenInTheViewerBar()
    {
        using var catalog = await _fx.CreateCatalogAsync();
        await using var vm = _fx.CreateViewModel(
            catalog,
            new GrayLoader(),
            _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.IsDevelopMode = true;
        var image = new ImageFile(_fx.Path("narrow.jpg"));
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null);

        var window = new MainWindow { Width = 800, Height = 600, DataContext = vm };
        window.Show();
        Drain();
        try
        {
            var split = Descendant<ToggleButton>(window, "BeforeAfterSplitButton");
            var bar = Descendant<Border>(window, "DevelopControlBar");
            Assert.Contains(
                bar.GetVisualDescendants(),
                control => ReferenceEquals(control, split));
            Assert.True(split.IsEffectivelyVisible);
            var full = Descendant<Button>(window, "FullScreenButton");
            var row = Assert.IsType<StackPanel>(split.Parent);
            Assert.Same(row, full.Parent);
            Assert.Equal(row.Children.IndexOf(full) + 1, row.Children.IndexOf(split));
        }
        finally
        {
            window.DataContext = null;
            window.Close();
        }
    }

    private static T Descendant<T>(
        Control root,
        string? name = null,
        string? text = null) where T : Control =>
        root.GetVisualDescendants().Prepend(root).OfType<T>().First(control =>
            (name == null || control.Name == name) &&
            (text == null || control is TextBlock block && block.Text == text));

    private static Point CenterOf(Control control, Control relativeTo) =>
        control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            relativeTo)!.Value;

    private static void AssertClose(
        NormalizedViewport expected,
        NormalizedViewport actual)
    {
        Assert.InRange(Math.Abs(expected.ZoomRelativeToFit -
            actual.ZoomRelativeToFit), 0, 0.01);
        Assert.InRange(Math.Abs(expected.Center.X - actual.Center.X), 0, 0.01);
        Assert.InRange(Math.Abs(expected.Center.Y - actual.Center.Y), 0, 0.01);
    }

    private static long RenderGeneration(MainWindowViewModel vm) =>
        (long)typeof(PreviewService).GetField(
            "_renderGeneration",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!
            .GetValue(vm.ImageService.Previews)!;

    private static string? PaintedHash(MainWindowViewModel vm) =>
        vm.PreviewImage is { } bitmap
            ? vm.ImageService.Previews.TryGetPreviewRenderIdentity(bitmap)?.SettingsHash
            : null;

    private static TaskCompletionSource NewSignal() => new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    private static void Drain()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class GrayLoader : IBaseImageLoader
    {
        private int _decodeCount;
        public int DecodeCount => Volatile.Read(ref _decodeCount);
        public bool CanLoad(ImageFile file) => true;

        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _decodeCount);
            return BaseImageLoadOutcome.Loaded(new PreviewBasePair(
                CreateBase(decode, 640, 480),
                CreateBase(decode, 1280, 960)));
        }

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            CreateBase(decode, 1280, 960);

        private static BaseImage CreateBase(
            BaseDecodeSettings decode,
            uint width,
            uint height) =>
            new(
                new MagickImage(MagickColors.Gray, width, height),
                new BaseImageInfo(
                    BaseSourceKind.Standard,
                    false,
                    decode,
                    null,
                    null,
                    6504,
                    0,
                    false,
                    null,
                    1,
                    1280,
                    960));
    }
}
