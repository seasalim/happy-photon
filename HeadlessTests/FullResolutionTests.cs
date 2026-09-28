using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class FullResolutionTests
{
    [AvaloniaFact]
    public async Task SingleJumpInstallsFullBaseAndEditsReuseIt()
    {
        using var fixture = new CatalogVmFixture();
        using var catalog = await fixture.CreateCatalogAsync();
        var loader = new FullLoader();
        var clock = new TestTimeProvider();
        await using var vm = await Start(fixture, catalog, loader, clock);
        vm.PublishRequiredDeviceLongEdge(400);
        clock.Advance(TimeSpan.FromMilliseconds(75));
        await TestWaits.UntilAsync(() => vm.PreviewImage?.PixelSize.Width == 400);
        await vm.FullResolutionWork;
        Assert.Equal(1, vm.HeldFullBaseCount);
        Assert.Equal("1:1", vm.OneToOneStatus);
        Assert.NotNull(vm.ImageService.Previews.GetRepairDisplayMap(vm.PreviewImage!));
        vm.Exposure = .42;
        clock.Advance(TimeSpan.FromMilliseconds(150));
        await vm.PendingPreviewDebounceTask!;
        clock.Advance(TimeSpan.FromMilliseconds(75));
        await TestWaits.UntilAsync(() => vm.PreviewImage?.PixelSize.Width == 400);
        await vm.FullResolutionWork;
        Assert.Equal(1, loader.FullCalls);
        vm.PublishRequiredDeviceLongEdge(240);
        await vm.FullResolutionWork;
        Assert.Equal(0, vm.HeldFullBaseCount);
        clock.Advance(TimeSpan.FromMilliseconds(75));
        await TestWaits.UntilAsync(() => vm.PreviewImage?.PixelSize.Width == 240);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullDemandCancelsSupersededRestingWork(bool alreadyRunning)
    {
        using var fixture = new CatalogVmFixture();
        using var catalog = await fixture.CreateCatalogAsync();
        using var restingStarted = new ManualResetEventSlim();
        using var resumeResting = new ManualResetEventSlim();
        using var decodeStarted = new ManualResetEventSlim();
        using var resumeDecode = new ManualResetEventSlim();
        var clock = new TestTimeProvider();
        var loader = new FullLoader();
        await using var vm = await Start(fixture, catalog, loader, clock);
        var service = vm.ImageService.Previews;
        var paints = vm.RestingPaintCount;
        var restingStages = new List<string>();
        var restingEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new System.Collections.Concurrent.ConcurrentQueue<string>();
        service.FullResolutionTrace = step =>
        {
            events.Enqueue(step);
            if (step == "resting-end") restingEnded.TrySetResult();
        };
        service.RestingStageStarted = stage =>
        {
            restingStages.Add(stage);
            if (stage != "snapshot-geometry") return;
            restingStarted.Set();
            Assert.True(resumeResting.Wait(TestWaits.Condition));
        };
        loader.Decode = () =>
        {
            decodeStarted.Set();
            Assert.True(resumeDecode.Wait(TestWaits.Condition));
        };

        try
        {
            vm.PublishRequiredDeviceLongEdge(300);
            if (alreadyRunning)
            {
                clock.Advance(TimeSpan.FromMilliseconds(75));
                await TestWaits.UntilAsync(() => restingStarted.IsSet);
            }
            vm.PublishRequiredDeviceLongEdge(400);
            Assert.False(vm.HasArmedRestingRender);
            // Decode starts without waiting for obsolete resting work to finish.
            await TestWaits.UntilAsync(() => decodeStarted.IsSet);
            resumeResting.Set();
            if (alreadyRunning) await restingEnded.Task.WaitAsync(TestWaits.Condition);
            clock.Advance(TimeSpan.FromMilliseconds(75));
            resumeDecode.Set();
            await vm.FullResolutionWork;

            Assert.Equal(400, vm.PreviewImage!.PixelSize.Width);
            Assert.Equal(paints, vm.RestingPaintCount);
            Assert.DoesNotContain("snapshot-resize", restingStages);
            if (!alreadyRunning) Assert.Empty(restingStages);
            var timeline = events.ToList();
            Assert.Contains("installed", timeline);
            Assert.True(timeline.IndexOf("installed") < timeline.IndexOf("gc-start"));
        }
        finally
        {
            resumeResting.Set();
            resumeDecode.Set();
        }
    }

    [AvaloniaTheory]
    [InlineData(false, "zoom")]
    [InlineData(true, "zoom")]
    [InlineData(true, "navigation")]
    [InlineData(true, "workspace")]
    [InlineData(true, "decode")]
    public async Task ReleaseWhileNativeWorkIsInFlight(bool rendering, string transition)
    {
        using var fixture = new CatalogVmFixture();
        using var catalog = await fixture.CreateCatalogAsync();
        using var started = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var loader = new FullLoader();
        var clock = new TestTimeProvider();
        await using var vm = await Start(fixture, catalog, loader, clock);
        void Pause() { started.Set(); Assert.True(resume.Wait(TestWaits.Condition)); }
        if (rendering) vm.ImageService.Previews.FullResolutionStageStarted = _ => Pause();
        else loader.Decode = Pause;
        vm.PublishRequiredDeviceLongEdge(400);
        clock.Advance(TimeSpan.FromMilliseconds(75));
        await TestWaits.UntilAsync(() => started.IsSet);
        Assert.Equal("1:1 · refining", vm.OneToOneStatus);
        try
        {
            switch (transition)
            {
                case "zoom": vm.PublishRequiredDeviceLongEdge(200); break;
                case "navigation": vm.SelectedImage = null; break;
                case "workspace": vm.IsDevelopMode = false; break;
                case "decode": vm.HlReconstruction = HlReconstructionMode.Blend; break;
            }
        }
        finally { resume.Set(); }
        var releasedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        await vm.FullResolutionWork.WaitAsync(TestWaits.Condition);
        Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(releasedAt) < TimeSpan.FromSeconds(1));
        Assert.Equal(0, vm.HeldFullBaseCount);
        Assert.NotEqual(400, vm.PreviewImage?.PixelSize.Width);
        Assert.True(loader.Released);
    }

    [AvaloniaFact]
    public async Task CloudOnlyNeverLoadsAndBothZoomModesShowStatus()
    {
        using var fixture = new CatalogVmFixture();
        using var catalog = await fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        var loader = new FullLoader();
        var availability = new TestSourceAvailabilityService(SourceAvailability.AvailableLocally);
        await using var vm = await Start(fixture, catalog, loader, clock, availability);
        availability.Availability = SourceAvailability.RequiresHydration;
        vm.PublishRequiredDeviceLongEdge(400);
        Assert.Equal("1:1 · preview detail", vm.OneToOneStatus);
        Assert.Equal(0, loader.FullCalls);
        var viewer = new ZoomPanControl { Source = vm.PreviewImage,
            OriginalViewPixelSize = new PixelSize(400, 200), AutoFit = false, ZoomLevel = 1 };
        using var scope = new TestUiScope(new Window { Content = viewer, Width = 200, Height = 100 });
        foreach (var status in new[] { "1:1 · refining", "1:1", vm.OneToOneStatus })
        {
            viewer.OneToOneStatus = status;
            Assert.True(viewer.IsOneToOneStatusVisible);
            Assert.Equal(status, viewer.FindControl<TextBlock>("LoupeStatus")!.Text);
            viewer.AutoFit = true;
            viewer.ZoomLevel = .5;
            viewer.BeginSynchronizedLoupePeek(new NormalizedPoint(.5, .5));
            Assert.True(viewer.IsOneToOneStatusVisible);
            Assert.Equal(status, viewer.FindControl<TextBlock>("LoupeStatus")!.Text);
            viewer.EndSynchronizedLoupePeek();
            viewer.AutoFit = false;
            viewer.ZoomLevel = 1;
        }
    }

    [AvaloniaFact]
    public async Task RefinementUsesTwoWorkersThroughGestureEndAndQueuedRender()
    {
        using var fixture = new CatalogVmFixture();
        using var catalog = await fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = await Start(fixture, catalog, new FullLoader(), clock);
        vm.HorizonRotation = 2;
        vm.Exposure = .1937;
        clock.Advance(TimeSpan.FromMilliseconds(150));
        await vm.PendingPreviewDebounceTask!;
        vm.OnSliderEditStarted();
        var stages = new List<string>();
        var workers = new List<int>();
        vm.ImageService.Previews.FullResolutionStageStarted = stage => stages.Add(stage);
        vm.ImageService.Previews.FullResolutionWorkersSelected = count => workers.Add(count);
        vm.PublishRequiredDeviceLongEdge(400);
        clock.Advance(TimeSpan.FromMilliseconds(75));
        await TestWaits.UntilAsync(() => vm.OneToOneStatus == "1:1" && vm.HeldFullBaseCount == 1);
        await vm.FullResolutionWork;
        Assert.Contains("geometry-warp", stages);
        Assert.NotEmpty(workers);
        Assert.All(workers, count => Assert.InRange(count, 1, 2));
        vm.OnSliderEditCompleted();
        Assert.Equal(2, vm.RefinementWorkerBudget);
        clock.Advance(TimeSpan.FromMilliseconds(150));
        await vm.PendingPreviewDebounceTask!;
        Assert.Equal(Environment.ProcessorCount, vm.RefinementWorkerBudget);
    }

    [AvaloniaFact]
    public async Task StaleRefinementNeverPaintsAndNewestEditReusesDecode()
    {
        using var fixture = new CatalogVmFixture();
        using var catalog = await fixture.CreateCatalogAsync();
        using var started = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var clock = new TestTimeProvider();
        var loader = new FullLoader();
        await using var vm = await Start(fixture, catalog, loader, clock);
        vm.ImageService.Previews.FullResolutionStageStarted = _ =>
        {
            started.Set();
            Assert.True(resume.Wait(TestWaits.Condition));
        };
        vm.PublishRequiredDeviceLongEdge(400);
        await TestWaits.UntilAsync(() => started.IsSet);
        var stale = vm.FullResolutionWork;
        var lastCollection = DevelopFullBase.LastCollectionMilliseconds;
        vm.Exposure = .7;
        resume.Set();
        await stale;
        Assert.Equal(lastCollection, DevelopFullBase.LastCollectionMilliseconds);
        Assert.NotEqual(400, vm.PreviewImage!.PixelSize.Width);
        double? renderedExposure = null;
        vm.ImageService.Previews.FullResolutionRendered = (request, _) => renderedExposure = request.Settings.Exposure;
        clock.Advance(TimeSpan.FromMilliseconds(150));
        await vm.PendingPreviewDebounceTask!;
        await TestWaits.UntilAsync(() => vm.PreviewImage?.PixelSize.Width == 400);
        await vm.FullResolutionWork;
        Assert.Equal(.7, renderedExposure);
        Assert.Equal(1, loader.FullCalls);
    }

    [AvaloniaFact]
    public async Task LargeJpegUsesNativePixelsAndExportIntent()
    {
        using var fixture = new CatalogVmFixture();
        using var catalog = await fixture.CreateCatalogAsync();
        var path = fixture.Path("large.jpg");
        using (var generated = new MagickImage("gradient:", new MagickReadSettings { Width = 3601, Height = 900 }))
            generated.Write(path);
        await using var vm = fixture.CreateViewModel(catalog, new StandardBaseLoader(), _ => Task.CompletedTask);
        var native = false;
        vm.ImageService.Previews.FullResolutionRendered = (request, _) =>
            native = request.Base.Pixels.Width == 3601 && request.Intent == RenderIntent.Export;
        vm.IsDevelopMode = true;
        vm.SelectedImage = new ImageFile(path);
        vm.PublishRequiredDeviceLongEdge(3601);
        await TestWaits.UntilAsync(() => vm.PreviewImage?.PixelSize.Width == 3601);
        Assert.True(native);
        Assert.Equal(1, ZoomGeometryCalculator.BitmapRelativeZoom(vm.PreviewImage!.PixelSize,
            vm.OriginalViewPixelSize, 1));
    }

    [AvaloniaFact]
    public async Task ControllableStagesCapWarpAndUncachedLutDuringGestureAndPendingFinalRender()
    {
        using var fixture = new CatalogVmFixture();
        using var catalog = await fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = await Start(fixture, catalog, new FullLoader(), clock);
        var service = vm.ImageService.Previews;
        var parent = service.TryGetPreviewRenderIdentity(vm.PreviewImage!) ??
            new PreviewRenderIdentity(vm.SelectedImage!, 0, BaseDecodeSettings.Default.CacheKey, "test",
                new PixelSize(400, 200), new PixelSize(400, 200), DateTime.MinValue);
        await using var holder = service.CreateFullBase(vm.SelectedImage!, vm.SelectedImage!.EditSettings);
        vm.OnSliderEditStarted();
        for (var phase = 0; phase < 2; phase++)
        {
            if (phase == 1) vm.OnSliderEditCompleted();
            using var paused = new ManualResetEventSlim();
            using var resume = new ManualResetEventSlim();
            var stages = new List<string>();
            var caps = new List<int>();
            var inTone = false;
            service.FullResolutionStageStarted = stage =>
            {
                stages.Add(stage);
                inTone = stage == "standard-tone";
            };
            service.FullResolutionWorkersSelected = cap =>
            {
                caps.Add(cap);
                if (!inTone || paused.IsSet) return;
                paused.Set();
                Assert.True(resume.Wait(TestWaits.Condition));
            };
            var render = service.RenderFullResolutionAsync(holder,
                new EditSettings { Exposure = 1.891733 + phase, HorizonRotation = 2 }, parent,
                () => vm.RefinementWorkerBudget, CancellationToken.None);
            await TestWaits.UntilAsync(() => paused.IsSet);
            Assert.Equal(2, vm.RefinementWorkerBudget);
            resume.Set();
            using var bitmap = await render;
            Assert.NotNull(bitmap);
            Assert.Contains("geometry-warp", stages);
            Assert.Contains("standard-tone", stages);
            Assert.DoesNotContain("tone-lut", stages);
            Assert.All(caps, cap => Assert.InRange(cap, 1, 2));
        }
    }

    private static async Task<MainWindowViewModel> Start(CatalogVmFixture fixture, CatalogService catalog,
        FullLoader loader, TestTimeProvider clock, TestSourceAvailabilityService? availability = null)
    {
        var path = fixture.Path("source.jpg");
        await File.WriteAllBytesAsync(path, [0]);
        var vm = fixture.CreateViewModel(catalog, loader, _ => Task.CompletedTask,
            availability ?? new(SourceAvailability.AvailableLocally), timeProvider: clock);
        vm.SelectedImage = new ImageFile(path);
        vm.IsDevelopMode = true;
        vm.PublishRequiredDeviceLongEdge(200);
        await TestWaits.UntilAsync(() => vm.HasArmedRestingRender);
        clock.Advance(TimeSpan.FromMilliseconds(75));
        await TestWaits.UntilAsync(() => vm.PreviewImage?.PixelSize.Width == 200);
        return vm;
    }

    private sealed class FullLoader : IBaseImageLoader
    {
        private readonly CountingPairLoader _pair = new();
        private BaseImage? _loaded;
        internal int FullCalls;
        internal Action? Decode;
        internal bool Released
        {
            get
            {
                try { _ = _loaded!.Pixels; return false; }
                catch (ObjectDisposedException) { return true; }
            }
        }

        public bool CanLoad(ImageFile file) => true;

        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(ImageFile file, BaseDecodeSettings decode,
            CancellationToken token) => _pair.LoadPreviewBaseWithOutcome(file, decode, token);

        public BaseImage? LoadFullBase(ImageFile file, BaseDecodeSettings decode, CancellationToken token)
        {
            Interlocked.Increment(ref FullCalls);
            Decode?.Invoke();
            return _loaded = new BaseImage(new MagickImage(MagickColors.Orange, 400, 200),
                new(BaseSourceKind.Standard, false, decode, null, null, 6504, 0, false, null, 1, 400, 200));
        }
    }
}
