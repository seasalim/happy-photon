using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsFusedBaselineTests
{
    private static void BrushOptIn()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1", "Opt-in approved brush gates");
        PerfEnvironment.AssertFullCpu();
        Assert.Equal(5, Samples);
#if DEBUG
        throw new InvalidOperationException("Use Release for brush measurements");
#endif
    }

    private void BrushCoverage(BaseImage basis, EditSettings settings, BrushDocument[] documents, bool eight)
    {
        var (geometry, support) = LocalsBrushCoverage.Scan(basis, settings, documents);
        var pixels = (long)basis.Pixels.Width * basis.Pixels.Height;
        output.WriteLine(LocalsBrushWorkloads.Description(eight));
        for (var i = 0; i < geometry.Length; i++)
            Print(support[i] / (double)pixels, $"brush={i + 1} geometric_pixels={geometry[i]} support_pixels={support[i]} " +
                $"frame_pixels={pixels} geometric_coverage={geometry[i] / (double)pixels:R} support_scan_outside_timing=True");
    }

    // Small controls (paired deltas, overlays) get an absolute floor so noise cannot invalidate quiet runs.
    private void BrushControl(double observed, double reference, string arm, double floorMs = 0)
    {
        var band = Math.Max(reference * .25, floorMs);
        var valid = observed >= reference - band && observed <= reference + band;
        output.WriteLine(
            $"control={arm} observed_ms={observed:R} reference_ms={reference:R} band=[{reference - band:R},{reference + band:R}] valid={valid}");
        Assert.True(valid, $"Invalid run: {arm} control {observed:R} ms is outside ±{band:R} ms of {reference:R} ms");
    }

    private void BrushBypass(BaseImage basis, EditSettings settings)
    {
        var off = settings.Clone(); off.Locals = null;
        using var expected = new RenderPipeline().Render(new(basis, off, RenderIntent.Preview, 1600, new(false, false)));
        using var actual = new RenderPipeline().Render(new(basis, LocalsBrushProduction.Attach(settings, null), RenderIntent.Preview, 1600, new(false, false)));
        var a = RenderPipelineTestSupport.ReadPixels(expected.Image);
        var b = RenderPipelineTestSupport.ReadPixels(actual.Image);
        Print(0, $"brushes_off_vs_production differing_codes={a.Zip(b).Count(p => p.First != p.Second)} " +
            $"length_match={a.Length == b.Length}");
        Assert.Equal(a, b);
    }

    [Fact]
    public void BrushOrdinaryTick()
    {
        BrushOptIn(); using var basis = Load(false);
        var control = LocalsBrushWorkloads.Settings(true);
        var pipeline = new RenderPipeline();
        foreach (var eight in new[] { false, true })
        {
            var settings = LocalsBrushWorkloads.Settings(eight);
            var docs = LocalsBrushWorkloads.Create(eight, (int)basis.Pixels.Width, (int)basis.Pixels.Height);
            BrushCoverage(basis, settings, docs, eight); BrushBypass(basis, settings);
            var on = LocalsBrushProduction.Attach(settings, docs);
            var off = LocalsBrushProduction.Attach(settings, null);
            void Run(int arm)
            {
                if (arm == 0) { using var result = new RenderPipeline().Render(new(basis, control, RenderIntent.Preview, 1600, new(false, false))); }
                else { using var result = pipeline.Render(new(basis, arm == 2 ? on : off, RenderIntent.Preview, 1600, new(false, false))); }
            }
            for (var warm = 0; warm < 3; warm++) for (var arm = 0; arm < 3; arm++) Run(arm);
            var times = new double[3][]; for (var a = 0; a < 3; a++) times[a] = new double[Samples];
            for (var sample = 0; sample < Samples; sample++)
            for (var step = 0; step < 3; step++)
            {
                var arm = (step + sample) % 3;
                var value = Measure(() => Run(arm)); times[arm][sample] = value.Ms;
                Print(0, $"brush_tick workload={(eight ? "BCap" : "B1")} sample={sample} arm={new[] { "LH8", "brushes-off", "brushes-on" }[arm]} " +
                    $"ms={value.Ms:R} private_bytes={value.Peak} caller_alloc_bytes={value.Allocated} ");
            }
            Print(0, $"brush_tick workload={(eight ? "BCap" : "B1")} samples={Samples} LH8_median_ms={Median(times[0]):R} " +
                $"off_median_ms={Median(times[1]):R} on_median_ms={Median(times[2]):R} " +
                $"paired_delta_ms={Median(times[2].Zip(times[1], (a, b) => a - b).ToArray()):R} " + "production=True approved_gate=True");
            BrushControl(Median(times[0]), basis.Info.IsRawSource ? 47.6 : 42.3, "LH8 ordinary");
            Assert.True(Median(times[2]) <= 150, "Brush ordinary tick exceeds 150 ms");
            if (!eight) Assert.True(Median(times[2].Zip(times[1], (a, b) => a - b).ToArray()) <= 40,
                "B1 incremental brush cost exceeds 40 ms");
        }
    }

    [Fact]
    public void BrushIndexBuild()
    {
        BrushOptIn(); using var basis = Load(false);
        foreach (var eight in new[] { false, true })
        {
            var w = (int)basis.Pixels.Width; var h = (int)basis.Pixels.Height;
            var documents = LocalsBrushWorkloads.Create(eight, w, h);
            output.WriteLine(LocalsBrushWorkloads.Description(eight));
            var settings = LocalsBrushProduction.Attach(LocalsBrushWorkloads.Settings(eight), documents);
            LocalsBrushIndexMeasurement Build() => new(settings, w, h);
            GC.KeepAlive(Build());
            var times = new double[Samples]; var allocations = new double[Samples];
            for (var sample = 0; sample < Samples; sample++)
            {
                var index = Build();
                times[sample] = index.BuildMilliseconds; allocations[sample] = index.AllocatedBytes;
                Print(0, $"brush_index workload={(eight ? "BCap" : "B1")} sample={sample} size={w}x{h} {index.Report}");
            }
            Print(0, $"brush_index workload={(eight ? "BCap" : "B1")} median_ms={Median(times):R} " +
                $"median_allocation_bytes={Median(allocations)} samples={Samples} approved_gate=True");
            Assert.All(allocations, bytes => Assert.True(bytes <= 1024 * 1024, "Brush index exceeds 1 MiB"));
        }
    }

}
