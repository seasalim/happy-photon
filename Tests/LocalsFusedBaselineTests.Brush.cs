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
        using var actual = new LocalsBrushRenderer().Tick(basis, settings, null);
        var a = RenderPipelineTestSupport.ReadPixels(expected.Image);
        var b = RenderPipelineTestSupport.ReadPixels(actual);
        Print(0, $"brushes_off_vs_production differing_codes={a.Zip(b).Count(p => p.First != p.Second)} " +
            $"length_match={a.Length == b.Length}");
        Assert.Equal(a, b);
    }

    [Fact]
    public void BrushOrdinaryTick()
    {
        BrushOptIn(); using var basis = Load(false);
        var control = LocalsBrushWorkloads.Settings(true);
        var renderer = new LocalsBrushRenderer();
        foreach (var eight in new[] { false, true })
        {
            var settings = LocalsBrushWorkloads.Settings(eight);
            var docs = LocalsBrushWorkloads.Create(eight, (int)basis.Pixels.Width, (int)basis.Pixels.Height);
            BrushCoverage(basis, settings, docs, eight); BrushBypass(basis, settings);
            void Run(int arm)
            {
                if (arm == 0) { using var result = new RenderPipeline().Render(new(basis, control, RenderIntent.Preview, 1600, new(false, false))); }
                else { using var result = renderer.Tick(basis, settings, arm == 2 ? docs : null); }
            }
            for (var warm = 0; warm < 3; warm++) for (var arm = 0; arm < 3; arm++) Run(arm);
            var times = new double[3][]; for (var a = 0; a < 3; a++) times[a] = new double[Samples];
            var build = new double[Samples]; var evaluation = new double[2][] { new double[Samples], new double[Samples] };
            for (var sample = 0; sample < Samples; sample++)
            for (var step = 0; step < 3; step++)
            {
                var arm = (step + sample) % 3;
                var value = Measure(() => Run(arm)); times[arm][sample] = value.Ms;
                if (arm > 0) evaluation[arm - 1][sample] = renderer.KernelMilliseconds;
                if (arm == 2) { build[sample] = renderer.IndexMilliseconds; output.WriteLine(renderer.BuildReport); }
                Print(0, $"brush_tick workload={(eight ? "BCap" : "B1")} sample={sample} arm={new[] { "LH8", "brushes-off", "brushes-on" }[arm]} " +
                    $"ms={value.Ms:R} private_bytes={value.Peak} caller_alloc_bytes={value.Allocated} " +
                    $"index_ms={(arm == 2 ? renderer.IndexMilliseconds : 0):R} kernel_ms={(arm > 0 ? renderer.KernelMilliseconds : 0):R}");
            }
            Print(0, $"brush_tick workload={(eight ? "BCap" : "B1")} samples={Samples} LH8_median_ms={Median(times[0]):R} " +
                $"off_median_ms={Median(times[1]):R} on_median_ms={Median(times[2]):R} " +
                $"paired_delta_ms={Median(times[2].Zip(times[1], (a, b) => a - b).ToArray()):R} " +
                $"index_median_ms={Median(build):R} kernel_delta_ms={Median(evaluation[1].Zip(evaluation[0], (a, b) => a - b).ToArray()):R} approved_gate=True");
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
            LocalsBrushEvaluation Build() => new(documents, w, h);
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

    [Fact]
    public void BrushWeightOnly()
    {
        BrushOptIn(); using var basis = Load(false);
        foreach (var eight in new[] { false, true })
        {
            var settings = LocalsBrushWorkloads.Settings(eight);
            var w = (int)basis.Pixels.Width; var h = (int)basis.Pixels.Height;
            var documents = LocalsBrushWorkloads.Create(eight, w, h);
            BrushCoverage(basis, settings, documents, eight);
            using var geometry = RenderGeometry.Apply(basis.Pixels, settings, out _);
            DcpHueSatRenderer.Apply(geometry, basis.Info.DcpProfile?.HueSatMap);
            var values = RenderPipelineTestSupport.ReadPixels(geometry);
            var wb = new AgxCrossing.Matrix3x3(RenderChromaticStage.CreateWhiteBalanceMatrix(basis.Info, settings));
            var sums = new double[h];
            double buildMs = 0;
            void Run(bool on)
            {
                LocalsBrushEvaluation? grids = null;
                buildMs = Time(() => { if (on) grids = new LocalsBrushEvaluation(documents, w, h); });
                Parallel.For(0, h, y =>
                {
                    double sum = 0;
                    for (var x = 0; x < w; x++)
                    {
                        var p = y * w + x; var o = p * 3;
                        var r = values[o] / 65535d; var g = values[o + 1] / 65535d; var b = values[o + 2] / 65535d;
                        if (!on) { sum += r + g + b; continue; }
                        OklabColor.Classification? lab = null;
                        for (var j = 0; j < grids!.Count; j++)
                        {
                            var weight = grids.Weight(j, p);
                            if (weight == 0) continue;
                            var local = settings.Locals![j];
                            if (local.Luminance?.Enabled != true && local.Hue?.Enabled != true) { sum += weight; continue; }
                            lab ??= OklabColor.Classify(wb.Row0(r, g, b), wb.Row1(r, g, b), wb.Row2(r, g, b));
                            sum += weight * LocalsBrushPlan.RangeWeight(local, lab.Value);
                        }
                    }
                    sums[y] = sum;
                });
            }
            Run(false); Run(true);
            var times = new[] { new double[Samples], new double[Samples] };
            for (var sample = 0; sample < Samples; sample++) foreach (var on in new[] { false, true })
            {
                var value = Measure(() => Run(on)); times[on ? 1 : 0][sample] = value.Ms;
                Print(0, $"brush_weight_only workload={(eight ? "BCap" : "B1")} sample={sample} on={on} ms={value.Ms:R} " +
                    $"index_ms={buildMs:R} checksum={sums.Sum():R} diagnostic_only=True");
            }
            Print(0, $"brush_weight_only off_median_ms={Median(times[0]):R} on_median_ms={Median(times[1]):R} " +
                $"samples={Samples} diagnostic_only=True not_a_fused_bound=True");
        }
    }
}
