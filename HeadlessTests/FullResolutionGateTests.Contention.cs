using System.Diagnostics;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class FullResolutionGateTests
{
    [AvaloniaFact]
    public async Task ProductionContention()
    {
        OptIn();
        using var fixture = new CatalogVmFixture("full-contention");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var service = new PreviewService(catalog, new RawBaseLoader(), new RenderPipeline(),
            createRenderedThumbnail: false);
        var image = LocalRaw();
        var settings = HealWorkloads.LH8();
        settings.Repairs = HealWorkloads.Repairs(HealWorkloads.S64());
        var (first, _) = await service.ApplyEditsToPreviewAsync(image, settings, skipHistogram: true);
        using var firstBitmap = first;
        var parent = service.TryGetPreviewRenderIdentity(first!)!;
        await using var holder = service.CreateFullBase(image, settings);
        using (var warm = await service.RenderFullResolutionAsync(holder, settings, parent,
            () => Environment.ProcessorCount, CancellationToken.None)) Assert.NotNull(warm);
        using var pair = new GatedBaseImageLoader(new RawBaseLoader(), new SourceAvailabilityService()).LoadPreviewBaseWithOutcome(image, BaseDecodeSettings.Default,
            CancellationToken.None).Pair;
        Assert.NotNull(pair);
        var pipeline = new RenderPipeline();
        // Frozen LH8 contended control from HealGateTests.G4Contention.
        var controlSettings = new EditSettings
        {
            Exposure = .45, Brightness = 12, Contrast = 28, Highlights = -31, Shadows = 24,
            Saturation = 19, Vibrance = 16,
            Detail = new() { CaptureSharpen = 80, LuminanceNr = 55, ChromaNr = 65 },
            Effects = new() { Vignette = -37, Midpoint = 61, Grain = 42, GrainSize = GrainSize.Coarse }
        };
        var controlTick = controlSettings.Clone();
        controlTick.Locals = HealWorkloads.LH8().Locals;
        for (var warmup = 0; warmup < 3; warmup++)
        {
            using var controlWarm = pipeline.Render(new(pair.Interactive, controlTick,
                RenderIntent.Preview, 1600, new(false, false)));
            using var repairedWarm = pipeline.Render(new(pair.Interactive, settings,
                RenderIntent.Preview, 1600, new(false, false)));
        }
        var on = new double[5];
        var off = new double[5];
        var overlapWorkers = new int[5];
        for (var i = 0; i < 5; i++)
        {
            var controlStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var controlRender = Task.Run(() => pipeline.RenderResting(new(pair.Large!, controlSettings,
                RenderIntent.Preview, 3200, new(false, false)),
                RenderExecutionOptions.Resting(CancellationToken.None, 2, stage =>
                {
                    if (stage == "raw-crossing") controlStarted.TrySetResult();
                })));
            await controlStarted.Task.WaitAsync(TestWaits.Condition);
            var timer = Stopwatch.StartNew();
            using (pipeline.Render(new(pair.Interactive, controlTick, RenderIntent.Preview, 1600, new(false, false)))) { }
            off[i] = timer.Elapsed.TotalMilliseconds;
            using var controlBitmap = await controlRender;
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = 0;
            var workerStarted = new long[Environment.ProcessorCount];
            var workerEnded = new long[Environment.ProcessorCount];
            using var cancellation = new CancellationTokenSource();
            service.FullResolutionCrossingWorkerProgress = (count, worker, completed) =>
            {
                if (count != Environment.ProcessorCount) return;
                if (completed) workerEnded[worker] = Stopwatch.GetTimestamp();
                else
                {
                    workerStarted[worker] = Stopwatch.GetTimestamp();
                    started.TrySetResult();
                }
            };
            var refining = service.RenderFullResolutionAsync(holder, settings, parent,
                () => Volatile.Read(ref pending) == 0 ? Environment.ProcessorCount : 2, cancellation.Token);
            await started.Task.WaitAsync(TestWaits.Condition);
            Assert.False(refining.IsCompleted);
            Volatile.Write(ref pending, 1);
            var tick = settings.Clone();
            tick.Exposure += i * .01;
            timer.Restart();
            var tickStarted = Stopwatch.GetTimestamp();
            // A production slider tick invalidates the old refinement at its next cancellation check.
            cancellation.Cancel();
            using (pipeline.Render(new(pair.Interactive, tick, RenderIntent.Preview, 1600, new(false, false)))) { }
            on[i] = timer.Elapsed.TotalMilliseconds;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refining);
            overlapWorkers[i] = Enumerable.Range(0, workerStarted.Length).Count(worker =>
                workerStarted[worker] != 0 && workerStarted[worker] <= tickStarted &&
                workerEnded[worker] >= tickStarted);
            Assert.True(overlapWorkers[i] > 0, "Tick must overlap a full-width worker that processed pixels");
        }
        Report("G4", new { on, off, overlapWorkers, tick = on.Order().ElementAt(2), firstTick = on[0],
            control = off.Order().ElementAt(2), controlMin = 95, controlMax = 145,
            tickLimit = 175, firstTickLimit = 250 });
    }
}
