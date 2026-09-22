using System.Diagnostics;
using System.Reflection;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

// Approved acceptance gates, using the same workloads as the measured baseline.
public sealed class ExportFeedbackBaselineTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public Task ProofSizeA() => MeasureProofSize(full: true, expected: 3000);

    [AvaloniaFact]
    public Task ProofSizeB() => MeasureProofSize(full: false, expected: 2048);

    private async Task MeasureProofSize(bool full, int expected)
    {
        var elapsed = Stopwatch.StartNew();
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await catalog.InitializeAsync();
        using var loader = new BaselineLoader();
        await using var vm = CreateViewModel(catalog, loader);
        PrepareProof(vm, root.Path, full, small: true);
        await TestWaits.UntilAsync(() => vm.PreviewImage != null);
        vm.ExportSettings.ShowProof = true;
        await TestWaits.UntilAsync(() => vm.ExportProofCaption.StartsWith("PROOF"));
        var size = vm.PreviewImage!.PixelSize;
        var longEdge = Math.Max(size.Width, size.Height);
        output.WriteLine($"G-{(full ? 1 : 2)}: accepted={longEdge} px; bitmap={size}; elapsed={elapsed.Elapsed.TotalSeconds:F3}s");
        Assert.Equal(expected, longEdge);
        Assert.Equal(1, loader.FullLoadCount);
        vm.SelectedExportProofSize = vm.ExportProofSizes.Single(size => size.Name == "Small");
        await TestWaits.UntilAsync(() => vm.PreviewImage?.PixelSize.Width == 1024);
        vm.SelectedExportProofSize = vm.ExportProofSizes.Single(size => size.Name == "Web");
        await TestWaits.UntilAsync(() => vm.PreviewImage?.PixelSize.Width == 2048);
        vm.SelectedExportProofSize = vm.ExportProofSizes.Single(size => size.Name == "Small");
        await TestWaits.UntilAsync(() => vm.PreviewImage?.PixelSize.Width == 1024);
        vm.ExportSettings.ExportSmall = false;
        await TestWaits.UntilAsync(() => vm.PreviewImage?.PixelSize.Width == expected);
        Assert.Equal(full ? "Full size" : "Web", vm.SelectedExportProofSize!.Name);
        vm.ExportSettings.ExportSmall = true;
        vm.SelectedExportProofSize = vm.ExportProofSizes.Single(size => size.Name == "Small");
        await TestWaits.UntilAsync(() => vm.PreviewImage?.PixelSize.Width == 1024);
        vm.ExportSettings.SmallMaxSizeText = "invalid";
        await TestWaits.UntilAsync(() => vm.PreviewImage?.PixelSize.Width == expected);
        Assert.DoesNotContain(vm.ExportProofSizes, size => size.Name == "Small");
    }

    [AvaloniaFact]
    public async Task CaptionTruth()
    {
        var elapsed = Stopwatch.StartNew();
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await catalog.InitializeAsync();
        using var loader = new BaselineLoader();
        await using var vm = CreateViewModel(catalog, loader);
        PrepareProof(vm, root.Path, full: false, small: false);
        await TestWaits.UntilAsync(() => vm.PreviewImage != null);
        vm.ExportSettings.ShowProof = true;
        await TestWaits.UntilAsync(() => vm.ExportProofCaption.StartsWith("PROOF"));
        Assert.Equal(2048, vm.PreviewImage!.PixelSize.Width);
        Assert.Equal(DisplaySourceColorSpace.Srgb, vm.PreviewDisplayColorSpace);
        Assert.Equal("PROOF · Web · 2048 PX · sRGB", vm.ExportProofCaption);
        var oldBitmap = vm.PreviewImage;
        loader.FullLoadStarted.Reset();
        loader.PauseFullLoads = true;
        try
        {
            vm.ExportSettings.OutputColorSpace = OutputColorSpace.DisplayP3;
            Assert.True(loader.FullLoadStarted.Wait(TestWaits.Condition));
            Assert.Same(oldBitmap, vm.PreviewImage);
            output.WriteLine($"G-3: caption={vm.ExportProofCaption}; display={vm.PreviewDisplayColorSpace}; oldBitmapSame={ReferenceEquals(oldBitmap, vm.PreviewImage)}; elapsed={elapsed.Elapsed.TotalSeconds:F3}s");
            Assert.Equal("PROOF · Web · 2048 PX · sRGB · UPDATING…", vm.ExportProofCaption);
            Assert.Equal(DisplaySourceColorSpace.Srgb, vm.PreviewDisplayColorSpace);
        }
        finally
        {
            loader.ReleaseFullLoads.Set();
        }
        await TestWaits.UntilAsync(() => !ReferenceEquals(oldBitmap, vm.PreviewImage));
        Assert.Equal("PROOF · Web · 2048 PX · Display P3", vm.ExportProofCaption);
        Assert.Equal(DisplaySourceColorSpace.DisplayP3, vm.PreviewDisplayColorSpace);
        loader.ReleaseFullLoads.Reset();
        loader.FullLoadStarted.Reset();
        oldBitmap = vm.PreviewImage;
        try
        {
            vm.ExportSettings.OutputSharpening = OutputSharpeningMode.Screen;
            Assert.True(loader.FullLoadStarted.Wait(TestWaits.Condition));
            Assert.Same(oldBitmap, vm.PreviewImage);
            Assert.Equal("PROOF · Web · 2048 PX · Display P3 · UPDATING…", vm.ExportProofCaption);
        }
        finally { loader.ReleaseFullLoads.Set(); }
        await TestWaits.UntilAsync(() => !ReferenceEquals(oldBitmap, vm.PreviewImage));
        Assert.DoesNotContain("UPDATING", vm.ExportProofCaption);
    }

    [AvaloniaFact]
    public async Task StopKeepsFiles()
    {
        var elapsed = Stopwatch.StartNew();
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await catalog.InitializeAsync();
        using var loader = new BaselineLoader(fullWidth: 32, fullHeight: 24);
        var availability = new TestSourceAvailabilityService(SourceAvailability.AvailableLocally);
        var fileOperations = new RecordingFileOperations();
        await using var vm = new MainWindowViewModel(catalog, loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: availability, fileOperationService: fileOperations);
        var captures = Enumerable.Range(1, 3).Select(index =>
        {
            var path = Path.Combine(root.Path, $"photo-{index}.jpg");
            using var image = new MagickImage(MagickColors.Gray, 32, 24);
            image.Write(path);
            var capture = new ImageFile(path);
            capture.ApplyMetadata(new ImageMetadata());
            return capture;
        }).ToArray();
        vm.Browse.SetImages(captures);
        var renderedPhotos = 0;
        var exportService = new ImageExportService(
            new RenderPipeline(), new GatedBaseImageLoader(loader, availability),
            new ExportMetadataService("baseline", availability),
            new DcpProfileService(availability),
            renderDisplayRec2020: request =>
            {
                Interlocked.Increment(ref renderedPhotos);
                return (MagickImage)request.Base.Pixels.Clone();
            });
        // Test-only injection preserves the VM TryStart/preflight/run/report path.
        Field(typeof(ImageService), "_exportService").SetValue(vm.ImageService, exportService);
        var destination = Path.Combine(root.Path, "outputs");
        var settings = new ExportSettings
        {
            OutputFolder = destination,
            Format = ExportFormat.Png,
            OutputSharpening = OutputSharpeningMode.Off
        };
        var job = settings.CreateJob(captures,
            [new("web", 2048), new("small", 1024)], useSubfolders: true);
        Assert.Equal(6, job.Targets.Count);
        vm.WorkspaceMode = WorkspaceMode.Export;
        var targets = job.Targets;
        var installations = 0;
        ExportEncoder.InstallStarting = temporaryPath =>
        {
            if (Interlocked.Increment(ref installations) != 3) return;
            Assert.True(File.Exists(temporaryPath));
            Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
            {
                Assert.True(vm.StopExportCommand.CanExecute(null));
                Assert.Equal("Export in progress…", vm.ExportButtonLabel);
                vm.ExportSettings.OutputFolder = Path.Combine(root.Path, "next-job");
                captures[2].Flag = ImageFlag.Picked;
                vm.Browse.RefreshFilters();
                vm.UsePickedPhotosCommand.Execute(null);
                vm.StopExportCommand.Execute(null);
                Assert.Same(targets, job.Targets);
                Assert.Equal(6, job.Targets.Count);
            });
        };
        try { await vm.RunExportJobForTestAsync(job).WaitAsync(TestWaits.Condition); }
        finally { ExportEncoder.InstallStarting = null; }
        var files = Directory.GetFiles(destination, "*", SearchOption.AllDirectories);
        var temporary = files.Where(path => path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)).ToArray();
        output.WriteLine($"G-4: files={files.Length}; tmp={temporary.Length}; heading={vm.ExportReport?.Heading}; summary={vm.ExportReport?.Summary}; renderedPhotos={renderedPhotos}; elapsed={elapsed.Elapsed.TotalSeconds:F3}s");
        output.WriteLine("Installed: " + string.Join(", ", files.Select(path => Path.GetRelativePath(destination, path))));
        Assert.Equal(2, files.Length);
        Assert.Empty(temporary);
        Assert.All(job.Targets.Where(target => ReferenceEquals(target.Capture, captures[0])),
            target => Assert.True(File.Exists(target.ResolvedPath)));
        Assert.All(job.Targets.Where(target => !ReferenceEquals(target.Capture, captures[0])),
            target => Assert.False(File.Exists(target.ResolvedPath)));
        Assert.Equal(2, renderedPhotos);
        Assert.Equal("Export stopped", vm.ExportReport?.Heading);
        Assert.Equal("2 of 6 files completed and kept.", vm.ExportReport?.Summary);
        Assert.True(vm.ExportReport!.CanOpenFolder);
        Assert.Equal(destination, vm.ExportReport.DestinationFolder);
        Assert.Equal(2, vm.ExportReport.SuccessfulCount);
        await vm.OpenExportFolderCommand.ExecuteAsync(null);
        Assert.Equal(destination, fileOperations.OpenedFolder);
        var freshJob = vm.ExportSettings.CreateJob([captures[2]]);
        await vm.RunExportJobForTestAsync(freshJob).WaitAsync(TestWaits.Condition);
        Assert.Equal("Export complete", vm.ExportReport!.Heading);
        Assert.All(freshJob.Targets, target => Assert.True(File.Exists(target.ResolvedPath)));
        Assert.Same(targets, job.Targets);
    }

    private sealed class RecordingFileOperations : IFileOperationService
    {
        public string? OpenedFolder { get; private set; }
        public TrashPathAssessment AssessTrashPath(string path) => new(false, null);
        public Task<bool> MoveToTrashAsync(string filePath) => Task.FromResult(false);
        public Task<bool> RevealFileAsync(string filePath) => Task.FromResult(false);
        public Task<bool> OpenFolderAsync(string folderPath)
        {
            OpenedFolder = folderPath;
            return Task.FromResult(true);
        }
    }

    private static FieldInfo Field(Type type, string name) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Missing baseline seam: {type.Name}.{name}");

    private static MainWindowViewModel CreateViewModel(CatalogService catalog, IBaseImageLoader loader) =>
        new(catalog, loader, loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));

    private static void PrepareProof(MainWindowViewModel vm, string root, bool full, bool small)
    {
        var capture = new ImageFile(Path.Combine(root, "proof.jpg"));
        vm.Browse.SetImages([capture]);
        vm.ToggleImageSelection(capture);
        vm.SelectedImage = capture;
        vm.ExportSettings.ExportHiRes = full;
        vm.ExportSettings.ExportWeb = true;
        vm.ExportSettings.WebMaxSize = 2048;
        vm.ExportSettings.ExportSmall = small;
        vm.ExportSettings.SmallMaxSize = 1024;
        vm.ExportSettings.Format = ExportFormat.Jpeg;
        vm.ExportSettings.OutputColorSpace = OutputColorSpace.Srgb;
        vm.ExportSettings.OutputSharpening = OutputSharpeningMode.Off;
        vm.WorkspaceMode = WorkspaceMode.Export;
    }

    // Same full-load pause shape as ExportProofViewModelTests.ProofLoader.
    // Neutral geometry leaves the full synthetic 3000 x 1500 source unchanged.
    private sealed class BaselineLoader(uint fullWidth = 3000, uint fullHeight = 1500)
        : IBaseImageLoader, IDisposable
    {
        private int _fullLoadCount;
        public int FullLoadCount => Volatile.Read(ref _fullLoadCount);
        public bool PauseFullLoads { get; set; }
        public ManualResetEventSlim FullLoadStarted { get; } = new();
        public ManualResetEventSlim ReleaseFullLoads { get; } = new();
        public bool CanLoad(ImageFile file) => true;

        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
            ImageFile file, BaseDecodeSettings decode, CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.Loaded(new PreviewBasePair(CreateBase(decode, 128, 64), null));

        public BaseImage? LoadFullBase(
            ImageFile file, BaseDecodeSettings decode, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _fullLoadCount);
            FullLoadStarted.Set();
            if (PauseFullLoads)
                Assert.True(ReleaseFullLoads.Wait(TestWaits.Condition, cancellationToken));
            return CreateBase(decode, fullWidth, fullHeight);
        }

        private BaseImage CreateBase(BaseDecodeSettings decode, uint width, uint height) => new(
            new MagickImage(MagickColors.Gray, width, height) { Depth = 16, ColorSpace = ColorSpace.RGB },
            new BaseImageInfo(BaseSourceKind.Standard, false, decode, null, null,
                6504, 0, false, null, 1, (int)fullWidth, (int)fullHeight));

        public void Dispose()
        {
            FullLoadStarted.Dispose();
            ReleaseFullLoads.Dispose();
        }
    }
}

