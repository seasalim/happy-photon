using System.Diagnostics;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class FullResolutionGateTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task ProductionLatencyAndMemory()
    {
        OptIn();
        using var fixture = new CatalogVmFixture("full-gates");
        using var catalog = await fixture.CreateCatalogAsync();
        var loader = new RawBaseLoader();
        await using var vm = fixture.CreateViewModel(catalog, loader, _ => Task.CompletedTask);
        var image = LocalRaw();
        image.EditSettings = HealWorkloads.LH8();
        image.EditSettings.Repairs = HealWorkloads.Repairs(HealWorkloads.S64());
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        var timeline = new System.Collections.Concurrent.ConcurrentQueue<(string Step, long Timestamp)>();
        vm.ImageService.Previews.FullResolutionTrace = step =>
            timeline.Enqueue((step, Stopwatch.GetTimestamp()));
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;
        vm.PublishRequiredDeviceLongEdge(1600);
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
        var latency = Environment.GetEnvironmentVariable("HAPPY_PHOTON_WP5_ARM") == "latency";
        double? controlSeconds = null;
        if (latency)
        {
            var controlTimer = Stopwatch.StartNew();
            using (var control = await vm.ImageService.Previews.LoadComparePreviewAsync(image, new(), 10000))
                Assert.NotNull(control);
            controlSeconds = controlTimer.Elapsed.TotalSeconds;
        }
        var fit = PrivateBytes();
        var edge = Math.Max(vm.OriginalViewPixelSize.Width, vm.OriginalViewPixelSize.Height);
        var pixels = (long)vm.OriginalViewPixelSize.Width * vm.OriginalViewPixelSize.Height;
        var zoomAt = Stopwatch.GetTimestamp();
        var timer = Stopwatch.StartNew();
        timeline.Enqueue(("zoom", zoomAt));
        vm.PublishRequiredDeviceLongEdge(edge);
        await Refined(vm, edge);
        var cold = timer.Elapsed.TotalSeconds;
        await vm.FullResolutionWork;
        vm.ImageService.Previews.FullResolutionTrace = null;
        Report("cold-breakdown", timeline.Select(mark => new
        {
            step = mark.Step,
            ms = Stopwatch.GetElapsedTime(zoomAt, mark.Timestamp).TotalMilliseconds
        }).ToArray());
        var idle = PrivateBytes();
        var collection = DevelopFullBase.LastCollectionMilliseconds;
        await vm.ToggleSpotsModeCommand.ExecuteAsync(null);
        vm.SelectedSpot = vm.Spots[0];
        var spot = vm.SelectedSpot;
        Assert.True(vm.BeginSpotsGesture(SpotHandle.Destination, new(spot.U, spot.V)));
        vm.MoveSpotsGesture(new(spot.U + .001, spot.V), 5);
        timer.Restart();
        await vm.CompleteSpotsGestureAsync();
        await Refined(vm, edge);
        var warm = timer.Elapsed.TotalSeconds;
        await vm.FullResolutionWork;
        var commitCollection = DevelopFullBase.LastCollectionMilliseconds;
        idle = PrivateBytes();
        var previousBitmap = vm.PreviewImage;
        long peak = PrivateBytes();
        using var sampler = new Timer(_ =>
        {
            var value = PrivateBytes();
            long previous;
            do { previous = Interlocked.Read(ref peak); }
            while (value > previous && Interlocked.CompareExchange(ref peak, value, previous) != previous);
        }, null, 0, 2);
        vm.RefreshFullResolution();
        Assert.Same(previousBitmap, vm.PreviewImage);
        await vm.FullResolutionWork;
        Assert.NotSame(previousBitmap, vm.PreviewImage);
        sampler.Change(Timeout.Infinite, Timeout.Infinite);
        var warmIdle = PrivateBytes();
        var warmCollection = DevelopFullBase.LastCollectionMilliseconds;
        vm.PublishRequiredDeviceLongEdge(1600);
        await vm.FullResolutionWork;
        var releaseGc = DevelopFullBase.LastCollectionMilliseconds;
        // test-wait-policy: allow - G7 specifies the private sample five seconds after zoom-out.
        await Task.Delay(TimeSpan.FromSeconds(5));
        var released = PrivateBytes();
        Assert.Equal(0, vm.HeldFullBaseCount);
        Report("G1-G3-G6-G7", new { backend = "Skia", latency, pixels, fit, controlSeconds,
            baselineValid = fit is >= 150000000 and <= 900000000 && (!latency || controlSeconds is >= 1.8 and <= 3.6),
            warm, warmLimit = 1.5, cold, coldLimit = controlSeconds * 1.1,
            idleDelta = idle - fit, warmIdleDelta = warmIdle - fit, idleLimit = pixels * 12 * 1.1,
            peakDelta = peak - idle, peakLimit = pixels * 14 * 1.1,
            releasedDelta = released - fit, releaseLimit = 50000000, collection, commitCollection, warmCollection, releaseGc });
    }

    [AvaloniaFact]
    public async Task ProductionPreEncodeParity()
    {
        OptIn();
        using var fixture = new CatalogVmFixture("full-parity");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var service = new PreviewService(catalog, new RawBaseLoader(), new RenderPipeline());
        var image = LocalRaw();
        foreach (var repairs in new[] { false, true })
        {
            var settings = repairs ? HealWorkloads.LH8() : new EditSettings();
            if (repairs) settings.Repairs = HealWorkloads.Repairs(HealWorkloads.S64());
            var (interactive, _) = await service.ApplyEditsToPreviewAsync(image, settings, skipHistogram: true);
            using var preview = interactive;
            var parent = service.TryGetPreviewRenderIdentity(preview!)!;
            await using var holder = service.CreateFullBase(image, settings);
            long differing = -1;
            service.FullResolutionRendered = (request, actual) =>
            {
                Assert.True(request.Settings.Detail.ResolveCaptureSharpen(true) > 0);
                var pipeline = new RenderPipeline();
                using var upstream = pipeline.RenderDisplayRec2020(request);
                using var expected = RenderFinalizer.Finalize(upstream, null, OutputColorSpace.Srgb,
                    OutputSharpeningMode.Off, false, effects: settings.Effects);
                using var a = actual.GetPixels();
                using var b = expected.GetPixels();
                var aa = a.ToShortArray(PixelMapping.RGB)!;
                var bb = b.ToShortArray(PixelMapping.RGB)!;
                differing = 0;
                for (var i = 0; i < aa.Length; i++) if (aa[i] != bb[i]) differing++;
            };
            using var bitmap = await service.RenderFullResolutionAsync(holder, settings, parent,
                () => Environment.ProcessorCount, CancellationToken.None);
            Assert.NotNull(bitmap);
            Report("G5", new { repairs, differing, limit = 0 });
            Assert.Equal(0, differing);
        }
    }

    private static Task Refined(MainWindowViewModel vm, int edge) => TestWaits.UntilAsync(() =>
        vm.PreviewImage?.PixelSize.Width == edge && vm.OneToOneStatus == "1:1");

    private static ImageFile LocalRaw()
    {
        var path = GoldenTestPaths.Asset("canon-eos-6d-iso-6400.cr2");
        Assert.Equal(SourceAvailability.AvailableLocally, new SourceAvailabilityService().GetAvailability(path));
        return new(path);
    }

    private static void OptIn()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1", "Opt-in WP5 gates");
        Assert.True(Environment.ProcessorCount > 2);
    }

    private static long PrivateBytes()
    {
        using var process = Process.GetCurrentProcess();
        return process.PrivateMemorySize64;
    }

    private void Report(string gate, object values) => output.WriteLine("WP5 " + JsonSerializer.Serialize(
        new { gate, pid = Environment.ProcessId, cpu = Environment.ProcessorCount, values }));
}
