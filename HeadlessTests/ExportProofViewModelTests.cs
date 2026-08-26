using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
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
        Assert.Equal("PREVIEW · JPEG · sRGB", vm.ExportProofCaption);

        vm.ActiveExportCapture = vm.ExportCaptures.Single(capture =>
            ReferenceEquals(capture.Image, second));
        await TestWaits.UntilAsync(() =>
            ReferenceEquals(vm.SelectedImage, second) &&
            loader.PreviewLoadCount >= 2 &&
            vm.PreviewImage != null);

        Assert.Same(first, vm.ExportCaptures[0].Image);
        Assert.Equal(0, loader.FullLoadCount);
        Assert.Equal("PREVIEW · JPEG · sRGB", vm.ExportProofCaption);
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

        vm.ExportSettings.ShowProof = true;
        await TestWaits.UntilAsync(() =>
            vm.PreviewImage?.PixelSize.Width == 96 &&
            vm.ExportProofCaption == "PROOF · JPEG · sRGB · 96 PX");
        Assert.Equal(1, loader.FullLoadCount);

        vm.ExportSettings.WebMaxSize = 48;
        await TestWaits.UntilAsync(() =>
            vm.PreviewImage?.PixelSize.Width == 48 &&
            loader.FullLoadCount == 2);

        vm.ExportSettings.ShowProof = false;
        await TestWaits.UntilAsync(() =>
            vm.PreviewImage?.PixelSize.Width == 128 &&
            vm.ExportProofCaption == "PREVIEW · JPEG · sRGB · 48 PX");
        Assert.Equal(2, loader.FullLoadCount);
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
        loader.PauseFullLoads = true;

        vm.ExportSettings.ShowProof = true;
        Assert.True(loader.FullLoadStarted.Wait(TestWaits.Condition));
        Assert.Equal("PREVIEW · JPEG · sRGB · 96 PX", vm.ExportProofCaption);

        vm.ExportSettings.WebMaxSize = 48;
        await TestWaits.UntilAsync(() => loader.FullLoadCount >= 2);
        Assert.Equal("PREVIEW · JPEG · sRGB · 48 PX", vm.ExportProofCaption);

        loader.ReleaseFullLoads.Set();
        await TestWaits.UntilAsync(() =>
            vm.PreviewImage?.PixelSize.Width == 48 &&
            vm.ExportProofCaption == "PROOF · JPEG · sRGB · 48 PX");
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
            vm.ExportProofCaption == "PROOF · JPEG · sRGB · 96 PX");
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
