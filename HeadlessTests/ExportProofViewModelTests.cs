using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportProofViewModelTests
{
    [AvaloniaFact]
    public async Task ProofOff_UsesStandardPreviewForExportCaptureChanges()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await catalog.InitializeAsync();
        var loader = new ProofLoader();
        await using var vm = CreateViewModel(catalog, loader);
        var (first, second) = PrepareTwoCaptures(vm, root.Path);

        vm.WorkspaceMode = WorkspaceMode.Export;
        await TestWaits.UntilAsync(() =>
            vm.PreviewImage != null && loader.PreviewLoadCount >= 1);

        Assert.False(vm.ExportSettings.ShowProof);
        Assert.Equal(0, loader.FullLoadCount);
        Assert.Equal("PREVIEW · edits applied", vm.ExportProofCaption);

        vm.ActiveExportCapture = vm.ExportCaptures.Single(capture =>
            ReferenceEquals(capture.Image, second));
        await TestWaits.UntilAsync(() =>
            ReferenceEquals(vm.SelectedImage, second) &&
            loader.PreviewLoadCount >= 2 &&
            vm.PreviewImage != null);

        Assert.Same(first, vm.ExportCaptures[0].Image);
        Assert.Equal(0, loader.FullLoadCount);
        Assert.Equal("PREVIEW · edits applied", vm.ExportProofCaption);
    }

    [AvaloniaFact]
    public async Task ProofToggle_RendersRecipeAndOffRestoresStandardPreview()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await catalog.InitializeAsync();
        var loader = new ProofLoader();
        await using var vm = CreateViewModel(catalog, loader);
        PrepareOneCapture(vm, root.Path);
        ArmSizedRecipe(vm, 96);

        vm.WorkspaceMode = WorkspaceMode.Export;
        await TestWaits.UntilAsync(() => vm.PreviewImage?.PixelSize.Width == 128);
        using var paneScope = ShowPreview(vm);
        AssertPaintedPreviewSize(paneScope, 320, 160);

        vm.ExportSettings.ShowProof = true;
        await TestWaits.UntilAsync(() =>
            vm.PreviewImage?.PixelSize.Width == 96 &&
            vm.ExportProofCaption == "PROOF · Web · 96 PX · sRGB");
        Assert.Equal(1, loader.FullLoadCount);
        AssertPaintedPreviewSize(paneScope, 96, 48);

        vm.ExportSettings.WebMaxSize = 48;
        await TestWaits.UntilAsync(() =>
            vm.PreviewImage?.PixelSize.Width == 48 &&
            loader.FullLoadCount == 2);
        AssertPaintedPreviewSize(paneScope, 48, 24);

        vm.ExportSettings.ShowProof = false;
        await TestWaits.UntilAsync(() =>
            vm.PreviewImage?.PixelSize.Width == 128 &&
            vm.ExportProofCaption == "PREVIEW · edits applied");
        Assert.Equal(2, loader.FullLoadCount);
        AssertPaintedPreviewSize(paneScope, 320, 160);
    }

    [AvaloniaTheory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("70000")]
    public async Task InvalidSize_ProofUsesLargestValidEnabledSize(string invalidSize)
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await catalog.InitializeAsync();
        var loader = new ProofLoader();
        await using var vm = CreateViewModel(catalog, loader);
        PrepareOneCapture(vm, root.Path);
        ArmSizedRecipe(vm, 96);
        vm.ExportSettings.ExportSmall = true;
        vm.ExportSettings.SmallMaxSize = 48;
        vm.ExportSettings.WebMaxSizeText = invalidSize;
        vm.WorkspaceMode = WorkspaceMode.Export;
        await TestWaits.UntilAsync(() => vm.PreviewImage?.PixelSize.Width == 128);
        vm.ExportSettings.ShowProof = true;
        await TestWaits.UntilAsync(() => vm.ExportProofCaption == "PROOF · Small · 48 PX · sRGB");
        Assert.Equal(48, vm.PreviewImage!.PixelSize.Width);
        Assert.False(vm.CanRunExport);
    }

    [AvaloniaFact]
    public async Task SupersededPausedProof_LabelsOnlyTheAcceptedPaintAsProof()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await catalog.InitializeAsync();
        var loader = new ProofLoader();
        await using var vm = CreateViewModel(catalog, loader);
        PrepareOneCapture(vm, root.Path);
        ArmSizedRecipe(vm, 96);
        vm.WorkspaceMode = WorkspaceMode.Export;
        await TestWaits.UntilAsync(() => vm.PreviewImage?.PixelSize.Width == 128);
        using var paneScope = ShowPreview(vm);
        AssertPaintedPreviewSize(paneScope, 320, 160);
        loader.PauseFullLoads = true;

        vm.ExportSettings.ShowProof = true;
        Assert.True(loader.FullLoadStarted.Wait(TestWaits.Condition));
        Assert.Equal("PREVIEW · edits applied · UPDATING…", vm.ExportProofCaption);
        AssertPaintedPreviewSize(paneScope, 320, 160);

        vm.ExportSettings.WebMaxSize = 48;
        await TestWaits.UntilAsync(() => loader.FullLoadCount >= 2);
        Assert.Equal("PREVIEW · edits applied · UPDATING…", vm.ExportProofCaption);
        AssertPaintedPreviewSize(paneScope, 320, 160);

        loader.ReleaseFullLoads.Set();
        await TestWaits.UntilAsync(() =>
            vm.PreviewImage?.PixelSize.Width == 48 &&
            vm.ExportProofCaption == "PROOF · Web · 48 PX · sRGB");
        AssertPaintedPreviewSize(paneScope, 48, 24);
    }

    [AvaloniaFact]
    public async Task DisplayedProof_CancelsInFlightRestingUpgrade()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await catalog.InitializeAsync();
        var clock = new TestTimeProvider();
        var loader = new ProofLoader();
        var vm = CreateViewModel(catalog, loader, clock);
        PrepareOneCapture(vm, root.Path);
        ArmSizedRecipe(vm, 96);
        vm.PublishRequiredDeviceLongEdge(240);
        vm.WorkspaceMode = WorkspaceMode.Export;
        await TestWaits.UntilAsync(() => vm.PreviewImage?.PixelSize.Width == 128);
        await TestWaits.UntilAsync(() => vm.HasArmedRestingRender);

        using var restingStarted = new ManualResetEventSlim();
        using var releaseResting = new ManualResetEventSlim();
        vm.ImageService.Previews.RestingStageStarted = stage =>
        {
            if (stage != "pipeline") return;
            restingStarted.Set();
            releaseResting.Wait(TestWaits.Condition);
        };
        clock.Advance(TimeSpan.FromMilliseconds(75));
        Assert.True(restingStarted.Wait(TestWaits.Condition));

        vm.ExportSettings.ShowProof = true;
        await TestWaits.UntilAsync(() =>
            vm.ExportProofCaption == "PROOF · Web · 96 PX · sRGB");
        releaseResting.Set();
        await vm.DisposeAsync();

        Assert.Equal(0, vm.RestingPaintCount);
    }

    [AvaloniaFact]
    public async Task DisposeAsync_DrainsPausedProofBeforeImageServices()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await catalog.InitializeAsync();
        var loader = new ProofLoader { PauseFullLoads = true };
        var vm = CreateViewModel(catalog, loader);
        PrepareOneCapture(vm, root.Path);
        vm.WorkspaceMode = WorkspaceMode.Export;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null);
        var proofExitedBeforeServices = false;
        vm.DependentExportServicesDisposing += () =>
            proofExitedBeforeServices = loader.FullLoadExited.IsSet;

        vm.ExportSettings.ShowProof = true;
        Assert.True(loader.FullLoadStarted.Wait(TestWaits.Condition));
        await vm.DisposeAsync();

        Assert.True(proofExitedBeforeServices);
    }

    [AvaloniaFact]
    public async Task CloudOnlyProof_DoesNotReadUnapprovedSource()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await catalog.InitializeAsync();
        var loader = new ProofLoader();
        var availability = new TestSourceAvailabilityService(SourceAvailability.RequiresHydration);
        var vm = new MainWindowViewModel(catalog, loader,
            loadMetadataAsync: _ => Task.CompletedTask, availabilityService: availability);
        try
        {
            PrepareOneCapture(vm, root.Path);
            vm.WorkspaceMode = WorkspaceMode.Export;
            var checks = availability.CallCount;
            vm.ExportSettings.ShowProof = true;
            await TestWaits.UntilAsync(() => availability.CallCount > checks &&
                !vm.ExportProofCaption.Contains("UPDATING"));
        }
        finally { await vm.DisposeAsync(); }
        Assert.Equal(0, loader.FullLoadCount);
        Assert.Equal(0, loader.PreviewLoadCount);
    }

    private static TestUiScope ShowPreview(MainWindowViewModel vm)
    {
        var pane = new ExportPreviewPane { DataContext = vm };
        // Caption positioning is covered separately; isolate the solid image pixels.
        pane.FindControl<TextBlock>("ExportProofCaption")!.Opacity = 0;
        var window = new Window { Width = 832, Height = 660, Content = pane };
        return new TestUiScope(window, afterShow: () =>
        {
            window.SetRenderScaling(1.5);
            Dispatcher.UIThread.RunJobs();
            pane.UpdateLayout();
        });
    }

    private static void AssertPaintedPreviewSize(TestUiScope scope, int width, int height)
    {
        var window = scope.Window!;
        var pane = (ExportPreviewPane)window.Content!;
        Assert.Equal(((MainWindowViewModel)pane.DataContext!).ExportSettings.ShowProof, pane.FindControl<TextBlock>("ExportProofHelp")!.IsEffectivelyVisible);
        Dispatcher.UIThread.RunJobs();
        pane.UpdateLayout();
        var frame = pane.FindControl<UniformImageOverlayPanel>("ExportPreviewImageFrame")!;
        using var capture = window.CaptureRenderedFrame() ??
            throw new InvalidOperationException("Export proof frame was empty.");
        using var buffer = capture.Lock();
        var origin = frame.TranslatePoint(default, window)!.Value;
        var left = (int)Math.Round(origin.X * window.RenderScaling);
        var top = (int)Math.Round(origin.Y * window.RenderScaling);
        var fw = (int)Math.Round(frame.Bounds.Width * window.RenderScaling);
        var fh = (int)Math.Round(frame.Bounds.Height * window.RenderScaling);
        int Pixel(int x, int y) =>
            Marshal.ReadInt32(buffer.Address + y * buffer.RowBytes + x * 4);
        var center = Pixel(left + fw / 2, top + fh / 2);
        Assert.NotEqual(Pixel(left, top), center);
        var paintedWidth = Enumerable.Range(left, fw).Count(x => Pixel(x, top + fh / 2) == center);
        var paintedHeight = Enumerable.Range(top, fh).Count(y => Pixel(left + fw / 2, y) == center);
        Assert.True(Math.Abs(paintedWidth - width) <= 1,
            $"Painted {paintedWidth}x{paintedHeight}; expected {width}x{height}");
        Assert.True(Math.Abs(paintedHeight - height) <= 1,
            $"Painted {paintedWidth}x{paintedHeight}; expected {width}x{height}");
    }

    [AvaloniaFact]
    public async Task WatermarkBurst_RequestsOneProofAfterTrailingDelay()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await catalog.InitializeAsync();
        var clock = new TestTimeProvider();
        var loader = new ProofLoader();
        await using var vm = CreateViewModel(catalog, loader, clock);
        PrepareOneCapture(vm, root.Path);
        vm.WorkspaceMode = WorkspaceMode.Export;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null);
        vm.ExportSettings.ShowProof = true;
        await TestWaits.UntilAsync(() => vm.ExportProofCaption.StartsWith("PROOF") &&
            !vm.ExportProofCaption.Contains("UPDATING"));
        Assert.Equal(1, loader.FullLoadCount);
        var previous = vm.PreviewImage;
        for (var i = 0; i < 10; i++)
        {
            vm.ExportSettings.Watermark.Text = $"Text {i}";
            clock.Advance(TimeSpan.FromMilliseconds(100));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, loader.FullLoadCount);
        }
        clock.Advance(TimeSpan.FromMilliseconds(149));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, loader.FullLoadCount);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await TestWaits.UntilAsync(() => vm.PreviewImage != previous &&
            !vm.ExportProofCaption.Contains("UPDATING"));
        Assert.Equal(2, loader.FullLoadCount);

        // An immediate non-watermark trigger consumes any pending watermark refresh.
        vm.ExportSettings.Watermark.Size = 4;
        previous = vm.PreviewImage;
        vm.ExportSettings.OutputSharpening = OutputSharpeningMode.Off;
        await TestWaits.UntilAsync(() => vm.PreviewImage != previous);
        clock.Advance(TimeSpan.FromSeconds(1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, loader.FullLoadCount);
    }

    private static MainWindowViewModel CreateViewModel(
        CatalogService catalog,
        ProofLoader loader,
        TimeProvider? timeProvider = null) => new(
            catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            timeProvider: timeProvider);

    private static (ImageFile First, ImageFile Second) PrepareTwoCaptures(
        MainWindowViewModel vm,
        string root)
    {
        var first = new ImageFile(Path.Combine(root, "first.jpg"));
        var second = new ImageFile(Path.Combine(root, "second.jpg"));
        vm.Browse.SetImages([first, second]);
        vm.ToggleImageSelection(first);
        vm.ToggleImageSelection(second);
        vm.SelectedImage = first;
        return (first, second);
    }

    private static void PrepareOneCapture(MainWindowViewModel vm, string root)
    {
        var image = new ImageFile(Path.Combine(root, "proof.jpg"));
        vm.Browse.SetImages([image]);
        vm.ToggleImageSelection(image);
        vm.SelectedImage = image;
    }

    private static void ArmSizedRecipe(MainWindowViewModel vm, int size)
    {
        vm.ExportSettings.ExportHiRes = false;
        vm.ExportSettings.ExportWeb = true;
        vm.ExportSettings.WebMaxSize = size;
        vm.ExportSettings.OutputSharpening = OutputSharpeningMode.Off;
    }

    private sealed class ProofLoader : IBaseImageLoader
    {
        private int _previewLoadCount;
        private int _fullLoadCount;

        public int PreviewLoadCount => Volatile.Read(ref _previewLoadCount);
        public int FullLoadCount => Volatile.Read(ref _fullLoadCount);
        public bool PauseFullLoads { get; set; }
        public ManualResetEventSlim FullLoadStarted { get; } = new();
        public ManualResetEventSlim FullLoadExited { get; } = new();
        public ManualResetEventSlim ReleaseFullLoads { get; } = new();

        public bool CanLoad(ImageFile file) => true;

        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _previewLoadCount);
            return BaseImageLoadOutcome.Loaded(new PreviewBasePair(
                CreateBase(decode, 128, 64),
                CreateBase(decode, 320, 160)));
        }

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _fullLoadCount);
            FullLoadStarted.Set();
            try
            {
                if (PauseFullLoads)
                {
                    ReleaseFullLoads.Wait(cancellationToken);
                }
                return CreateBase(decode, 128, 64);
            }
            finally
            {
                FullLoadExited.Set();
            }
        }

        private static BaseImage CreateBase(
            BaseDecodeSettings decode,
            uint width,
            uint height) => new(
                new MagickImage(MagickColors.Gray, width, height)
                {
                    Depth = 16,
                    ColorSpace = ColorSpace.RGB
                },
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
                    320,
                    160));
    }
}
