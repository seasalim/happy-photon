using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportWorkspaceRunTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("export-run");

    [Fact]
    public async Task OriginalOverwriteRefusal_BlocksEveryTargetBeforePixelWork()
    {
        var loader = new RecordingBaseLoader();
        await using var vm = CreateViewModel(loader);
        var captures = CreateCaptures("original-a.jpg", "original-b.jpg");
        SelectForExport(vm, captures);
        vm.ExportSettings.OutputFolder = _fx.Root;
        vm.ExportSettings.Format = ExportFormat.Jpeg;

        await vm.RunExportCommand.ExecuteAsync(null);

        Assert.Equal("Export blocked", vm.ExportReport?.Heading);
        Assert.Contains("2 export targets", vm.ExportReport?.Summary);
        Assert.Empty(loader.FullLoads);
        Assert.Equal(0, vm.ExportActivityScopeStartCount);
    }

    [Fact]
    public async Task RecipePathCollisionRefusal_BlocksBeforeQueueOrPixelWork()
    {
        var loader = new RecordingBaseLoader();
        await using var vm = CreateViewModel(loader);
        var capture = Assert.Single(CreateCaptures("recipe.jpg"));
        SelectForExport(vm, [capture]);
        var job = CreateJob(
            [capture],
            [new("web", 2048), new("small", 1024)],
            useSubfolders: false);

        await vm.RunExportJobForTestAsync(job);

        Assert.True(job.HasPathCollisions);
        Assert.Equal("Export blocked", vm.ExportReport?.Heading);
        Assert.Empty(loader.FullLoads);
        Assert.Equal(0, vm.ExportActivityScopeStartCount);
    }

    [Fact]
    public async Task RawJpegPairCollisionRefusal_NamesPairAndExistingRemedy()
    {
        var loader = new RecordingBaseLoader();
        await using var vm = CreateViewModel(loader);
        var raw = CreateRawCapture(Path.Combine("pair", "DSCF8280.RAF"));
        var jpeg = CreateCapture(Path.Combine("pair", "DSCF8280.JPG"));
        SelectForExport(vm, [raw, jpeg]);
        var job = CreateJob(
            [raw, jpeg],
            [new("web", 2048)],
            useSubfolders: false);

        await vm.RunExportJobForTestAsync(job);

        Assert.True(job.HasPathCollisions);
        Assert.Equal("Export blocked", vm.ExportReport?.Heading);
        Assert.Contains("DSCF8280.RAF", vm.ExportReport?.Summary);
        Assert.Contains("DSCF8280.JPG", vm.ExportReport?.Summary);
        Assert.Contains("one capture shot RAW+JPEG", vm.ExportReport?.Summary);
        Assert.Contains("Uncheck one in the Export filmstrip", vm.ExportReport?.Summary);
        Assert.Empty(loader.FullLoads);
        Assert.Equal(0, vm.ExportActivityScopeStartCount);
    }

    [Fact]
    public async Task UnrelatedSameStemCollisionRefusal_UsesGenericExplanation()
    {
        var loader = new RecordingBaseLoader();
        await using var vm = CreateViewModel(loader);
        var first = CreateCapture(Path.Combine("one", "same.jpg"));
        var second = CreateCapture(Path.Combine("two", "same.jpg"));
        SelectForExport(vm, [first, second]);
        var job = CreateJob(
            [first, second],
            [new("web", 2048)],
            useSubfolders: false);

        await vm.RunExportJobForTestAsync(job);

        Assert.True(job.HasPathCollisions);
        Assert.Equal("Export blocked", vm.ExportReport?.Heading);
        Assert.Contains("shared by multiple targets", vm.ExportReport?.Summary);
        Assert.DoesNotContain("RAW+JPEG", vm.ExportReport?.Summary);
        Assert.Empty(loader.FullLoads);
        Assert.Equal(0, vm.ExportActivityScopeStartCount);
    }

    [Fact]
    public async Task ExistingFileConfirmation_AuthorizesEveryConfirmedTarget()
    {
        var loader = new RecordingBaseLoader();
        await using var vm = CreateViewModel(loader);
        var captures = CreateCaptures("existing-a.jpg", "existing-b.jpg");
        SelectForExport(vm, captures);
        var job = CreateJob(
            captures,
            [new("web", 2048), new("small", 1024)],
            useSubfolders: true);
        foreach (var target in job.Targets)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target.ResolvedPath)!);
            File.WriteAllText(target.ResolvedPath, "old");
        }
        var confirmedCount = 0;
        vm.ConfirmExportOverwriteAsync = (count, paths) =>
        {
            confirmedCount = count;
            Assert.Equal(job.Targets.Count, paths.Count);
            return Task.FromResult(true);
        };

        await vm.RunExportJobForTestAsync(job);

        Assert.Equal(4, confirmedCount);
        Assert.Equal("4 of 4 files exported.", vm.ExportReport?.Summary);
        Assert.All(job.Targets, target =>
            Assert.NotEqual("old", File.ReadAllText(target.ResolvedPath)));
    }

    [Fact]
    public async Task HydrationConfirmation_ApprovesEveryCaptureInTheJob()
    {
        var loader = new RecordingBaseLoader();
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.RequiresHydration);
        await using var vm = CreateViewModel(loader, availability);
        var captures = CreateCaptures("cloud-a.jpg", "cloud-b.jpg");
        SelectForExport(vm, captures);
        var job = CreateJob(
            captures,
            [new("web", 2048), new("small", 1024)],
            useSubfolders: true);
        ExportHydrationScope? confirmedScope = null;
        vm.ConfirmExportHydrationAsync = scope =>
        {
            confirmedScope = scope;
            return Task.FromResult(true);
        };

        await vm.RunExportJobForTestAsync(job);

        Assert.Equal(2, confirmedScope?.FileCount);
        Assert.Equal(2, loader.FullLoads.Count);
        Assert.Equal("4 of 4 files exported.", vm.ExportReport?.Summary);
    }

    [Fact]
    public async Task PartialFailureReportAndRetryFailedOnly_KeepSuccessfulSiblings()
    {
        var loader = new RecordingBaseLoader();
        await using var vm = CreateViewModel(loader);
        var captures = CreateCaptures("retry-a.jpg", "retry-b.jpg");
        SelectForExport(vm, captures);
        var job = CreateJob(
            captures,
            [new("web", 2048), new("small", 1024)],
            useSubfolders: true);
        var blocked = new[] { job.Targets[0], job.Targets[3] };
        foreach (var target in blocked)
            Directory.CreateDirectory(target.ResolvedPath);

        await vm.RunExportJobForTestAsync(job);

        Assert.True(vm.ExportReport?.HasFailures);
        Assert.Equal(2, vm.ExportReport?.FailedTargets.Count);
        Assert.Equal(["web", "small"],
            vm.ExportReport!.FailedTargets.Select(target => target.Recipe.Name));
        var successful = job.Targets.Except(blocked).ToList();
        var protectedTime = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        foreach (var target in successful)
            File.SetLastWriteTimeUtc(target.ResolvedPath, protectedTime);
        foreach (var target in blocked)
            Directory.Delete(target.ResolvedPath);

        await vm.RetryFailedExportCommand.ExecuteAsync(null);

        Assert.Equal("2 of 2 files exported.", vm.ExportReport?.Summary);
        Assert.Equal(2, vm.ExportProgressMaximum);
        Assert.All(successful, target => Assert.Equal(
            protectedTime,
            File.GetLastWriteTimeUtc(target.ResolvedPath)));
        Assert.All(blocked, target => Assert.True(File.Exists(target.ResolvedPath)));
    }

    [Fact]
    public async Task QueueSurvivesModeSwitchAndDuplicateStartIsRefused()
    {
        var loader = new BlockingBaseLoader();
        var vm = CreateViewModel(loader);
        var capture = Assert.Single(CreateCaptures("blocking.jpg"));
        SelectForExport(vm, [capture]);
        vm.ExportSettings.ExportWeb = true;
        var run = vm.RunExportCommand.ExecuteAsync(null);
        Assert.True(loader.WaitUntilStarted());

        Assert.True(vm.IsExportJobRunning);
        Assert.True(vm.IsExportQueueVisible);
        vm.HandleEscapeCommand.Execute(null);
        Assert.True(vm.IsExportJobRunning);
        Assert.False(vm.IsExportQueueVisible);
        vm.SwitchToExportCommand.Execute(null);
        Assert.True(vm.IsExportQueueVisible);
        await vm.EnterDevelopModeCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.DuplicateExportStartRefusalCount);

        loader.Release();
        await run;
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_CancelsAndDrainsBeforeDependentServicesDispose()
    {
        var loader = new BlockingBaseLoader();
        var vm = CreateViewModel(loader);
        var capture = Assert.Single(CreateCaptures("dispose.jpg"));
        SelectForExport(vm, [capture]);
        _ = vm.RunExportCommand.ExecuteAsync(null);
        Assert.True(loader.WaitUntilStarted());
        var order = new List<string>();
        vm.ExportJobDrainStarted += () => order.Add("drain-start");
        vm.ExportJobDrainCompleted += () => order.Add("drain-complete");
        vm.DependentExportServicesDisposing += () => order.Add("services-dispose");

        await vm.DisposeAsync();

        Assert.True(loader.CancellationObserved);
        Assert.Equal(
            ["drain-start", "drain-complete", "services-dispose"],
            order);
        Assert.True(vm.ActiveExportJobTask?.IsCompleted);
    }

    private MainWindowViewModel CreateViewModel(
        IBaseImageLoader loader,
        ISourceAvailabilityService? availability = null) =>
        _fx.CreateViewModel(
            _fx.CreateCatalog(Guid.NewGuid().ToString("N")),
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: availability ?? new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            postSelection: action => action());

    private List<ImageFile> CreateCaptures(params string[] names) =>
        names.Select(CreateCapture).ToList();

    private ImageFile CreateCapture(string relativePath)
    {
        var path = _fx.Path(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = new MagickImage(MagickColors.Orange, 32, 24);
        image.Format = MagickFormat.Jpeg;
        image.Write(path);
        var capture = new ImageFile(path);
        capture.ApplyMetadata(new ImageMetadata());
        return capture;
    }

    private ImageFile CreateRawCapture(string relativePath)
    {
        var path = _fx.Path(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0]);
        var capture = new ImageFile(path);
        capture.ApplyMetadata(new ImageMetadata());
        return capture;
    }

    private void SelectForExport(
        MainWindowViewModel vm,
        IReadOnlyList<ImageFile> captures)
    {
        vm.Browse.SetImages(captures);
        foreach (var capture in captures) vm.Browse.ToggleSelection(capture);
        vm.SelectedImage = captures[0];
        vm.RefreshSelectedCount();
        vm.SwitchToExportCommand.Execute(null);
        vm.ExportSettings.OutputFolder = _fx.Path("outputs");
        vm.ExportSettings.Format = ExportFormat.Png;
    }

    private ExportJob CreateJob(
        IReadOnlyList<ImageFile> captures,
        IReadOnlyList<ExportVariant> recipes,
        bool useSubfolders) => new ExportSettings
        {
            OutputFolder = _fx.Path("outputs"),
            Format = ExportFormat.Png
        }.CreateJob(captures, recipes, useSubfolders);

    public void Dispose() => _fx.Dispose();

    private class RecordingBaseLoader : IBaseImageLoader
    {
        public List<string> FullLoads { get; } = [];
        public bool CanLoad(ImageFile file) => true;

        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.Failed(BaseImageLoadFailure.DecodeFailed);

        public virtual BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            FullLoads.Add(file.FilePath);
            return CreateBase(decode);
        }
    }

    private sealed class BlockingBaseLoader : RecordingBaseLoader
    {
        private readonly ManualResetEventSlim _started = new();
        private readonly ManualResetEventSlim _release = new();
        public bool CancellationObserved { get; private set; }

        public override BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            FullLoads.Add(file.FilePath);
            _started.Set();
            try
            {
                _release.Wait(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
            return CreateBase(decode);
        }

        public bool WaitUntilStarted() => _started.Wait(TestWaits.Condition);
        public void Release() => _release.Set();
    }

    private static BaseImage CreateBase(BaseDecodeSettings decode) => new(
        new MagickImage(MagickColors.Orange, 32, 24),
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
            32,
            24));
}
