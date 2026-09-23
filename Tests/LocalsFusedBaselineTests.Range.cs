using HappyPhoton.Services;
using HappyPhoton.Views;
using Xunit;
using static HappyPhoton.Tests.LocalsRangeOracle;

namespace HappyPhoton.Tests;

public sealed partial class LocalsFusedBaselineTests
{
    [Fact] public void QualifiedRangeControlTick() => ColorTick(true);
    [Fact] public Task QualifiedRangeControlContention() => ContendedTick(true, true, true, true);
    [Fact] public Task QualifiedRangeControlExport() => ExportDelta(true, true);
    [Fact] public void QualifiedRangeClassification() => RangeCost(false, false);
    [Fact] public void QualifiedRangeClassificationExport() => RangeCost(true, false);
    [Fact] public void QualifiedRangeOverlay() => RangeCost(false, true);
    [Fact] public void QualifiedRangeLuminanceClassification() => RangeCost(false, false, true);

    private void RangeCost(bool full, bool overlay, bool luminanceOnly = false)
    {
        OptIn();
        using var basis = Load(full);
        using var geometry = RenderGeometry.Apply(basis.Pixels, Settings, out _);
        // Existing Q16 DCP seam, before global WB. No originals are changed.
        DcpHueSatRenderer.Apply(geometry, basis.Info.DcpProfile?.HueSatMap);
        var source = RenderPipelineTestSupport.ReadPixels(geometry);
        var width = (int)geometry.Width; var height = (int)geometry.Height;
        var pixels = width * height;
        var edge = Math.Max(width, height);
        var radial = overlay ? null : RadialSettings(basis, true);
        var terms = radial?.Locals!.Select(l => RangeRadial.From(l, width / (double)edge, height / (double)edge)).ToArray();
        var covered = 0; long windowEvaluations = 0;
        if (terms != null)
            for (var p = 0; p < pixels; p++)
            {
                var active = RangeActive(terms, p, width, edge);
                if (active != 0) covered++;
                windowEvaluations += System.Numerics.BitOperations.PopCount((uint)active);
            }
        var wb = RenderChromaticStage.CreateWhiteBalanceMatrix(basis.Info, Settings);
        var matrix = new DcpRenderMatrix(wb);
        var workers = Math.Min(Environment.ProcessorCount, Math.Max(1, (pixels + 32767) / 32768));
        var totals = new double[workers];
        var windows = Enumerable.Range(0, 8).Select(i => (
            L: new LightWindow(i * .08, .4 + i * .08, i % 3 == 0 ? 0 : .1),
            H: new HueWindow(i * 45, i == 7 ? 360 : 60, i % 3 == 0 ? 0 : 30))).ToArray();
        var fast = new LocalsRangeFast(windows);
        // The selected mask's geometric factor is the existing analytic 45% linear.
        var mask = Mask.Create(width, height, .25, .45);
        float[]? field = null;
        byte[]? bitmap = null;
        var tint = HappyPhotonColors.MixerMagentaColor;
        double allocateMs = 0, computeMs = 0;
        void Run(int arm)
        {
            if (arm == 3)
                allocateMs = Time(() =>
                {
                    field = new float[pixels]; bitmap = new byte[pixels * 4];
                    // Commit all pages here so lazy zeroing/page faults belong to allocation.
                    Array.Fill(field, -1f); Array.Fill(bitmap, (byte)255);
                });
            var computeStart = System.Diagnostics.Stopwatch.GetTimestamp();
            Parallel.For(0, workers, worker =>
            {
                double sum = 0;
                for (var p = pixels * worker / workers; p < pixels * (worker + 1) / workers; p++)
                {
                    var o = p * 3;
                    var r = source[o] / 65535d; var g = source[o + 1] / 65535d; var b = source[o + 2] / 65535d;
                    if (arm == 0) { sum += r + g + b; continue; }
                    var active = arm is 4 or 6 or 7 ? RangeActive(terms!, p, width, edge) : (byte)255;
                    if (active == 0) continue;
                    var wr = matrix.Row0(r, g, b); var wg = matrix.Row1(r, g, b); var wbValue = matrix.Row2(r, g, b);
                    if (arm == 7)
                    {
                        var lightness = ClassifyLightness(wr, wg, wbValue);
                        for (var j = 0; j < windows.Length; j++)
                            if ((active & (1 << j)) != 0) sum += windows[j].L.Weight(lightness);
                        continue;
                    }
                    Lab lab; double c, h;
                    if (arm is 5 or 6) lab = fast.ClassifyGuarded(wr, wg, wbValue, active, out _, out c, out h);
                    else { lab = Classify(wr, wg, wbValue); c = lab.C; h = lab.Hue; }
                    if (arm == 1) { sum += lab.L + c + h; continue; }
                    var reliability = Reliability(c);
                    if (arm is 2 or 5)
                    {
                        foreach (var window in windows)
                            sum += window.L.Weight(lab.L) * window.H.Weight(h) * reliability;
                    }
                    else if (arm is 4 or 6 or 7)
                    {
                        for (var j = 0; j < windows.Length; j++)
                            if ((active & (1 << j)) != 0)
                                sum += windows[j].L.Weight(lab.L) * windows[j].H.Weight(h) * reliability;
                    }
                    else
                    {
                        var weight = (mask.Gain(p) - 1) / 3 *
                            windows[0].L.Weight(lab.L) * windows[0].H.Weight(h) * reliability;
                        field![p] = (float)weight;
                        var alpha = (byte)Math.Round(weight * 128);
                        bitmap![p * 4] = (byte)(tint.B * alpha / 255);
                        bitmap[p * 4 + 1] = (byte)(tint.G * alpha / 255);
                        bitmap[p * 4 + 2] = (byte)(tint.R * alpha / 255);
                        bitmap[p * 4 + 3] = alpha;
                        sum += weight;
                    }
                }
                totals[worker] = sum;
            });
            computeMs = System.Diagnostics.Stopwatch.GetElapsedTime(computeStart).TotalMilliseconds;
            Assert.True(double.IsFinite(totals.Sum()));
        }
        var arms = luminanceOnly ? new[] { 0, 7 } : overlay ? new[] { 0, 3 } : new[] { 0, 1, 2, 4, 5, 6 };
        foreach (var arm in arms) Run(arm);
        var allocationsMs = new double[Samples]; var computationsMs = new double[Samples];
        var checksums = new double[8];
        var times = arms.ToDictionary(a => a, _ => new double[Samples]);
        var memory = arms.ToDictionary(a => a, _ => new double[Samples]);
        var allocation = arms.ToDictionary(a => a, _ => new double[Samples]);
        for (var i = 0; i < Samples; i++)
        foreach (var arm in arms)
        {
            field = null; bitmap = null;
            var result = Measure(() => Run(arm));
            if (arm == 3) { allocationsMs[i] = allocateMs; computationsMs[i] = computeMs; }
            checksums[arm] = totals.Sum();
            times[arm][i] = result.Ms; memory[arm][i] = result.Peak; allocation[arm][i] = result.Allocated;
        }
        foreach (var arm in arms.Where(a => a != 0))
        {
            var delta = Median(times[arm].Zip(times[0], (a, b) => a - b).ToArray());
            var privateDelta = Median(memory[arm].Zip(memory[0], (a, b) => a - b).ToArray());
            Print(arm is 4 or 6 or 7 ? covered / (double)pixels : overlay ? .45 : 1, $"range arm={arm} full={full} pixels={pixels} samples={Samples} candidate={(arm is 5 or 6 ? "FAST-guarded" : "double-oracle")} culled={arm is 4 or 6 or 7} luminance_only={luminanceOnly} " +
                $"baseline_ms={Median(times[0]):F4} total_ms={Median(times[arm]):F4} increment_ms={delta:F4} " +
                $"ns_per_pixel={delta * 1e6 / pixels:F4} private_delta={privateDelta} " +
                $"caller_allocation={Median(allocation[arm])} checksum={checksums[arm]:R} " +
                $"times=[{string.Join(',', times[arm])}] private=[{string.Join(',', memory[arm])}]");
            if (arm != 3) Assert.True(Median(allocation[arm]) <= 65536, "Classification caller allocation");
        }
        if (luminanceOnly)
        {
            Assert.Equal(covered / (double)pixels, _radialCoverage);
            Print(_radialCoverage, $"L8 window_evaluations={windowEvaluations} no_hue=True no_reliability=True");
        }
        else if (!overlay)
        {
            Assert.Equal(covered / (double)pixels, _radialCoverage);
            foreach (var arm in new[] { 2, 4, 5, 6 })
            {
                var increment = Median(times[arm].Zip(times[0], (a, b) => a - b).ToArray());
                Print(covered / (double)pixels, $"range_projection arm={arm} full={full} " +
                    $"window_evaluations={(arm is 4 or 6 or 7 ? windowEvaluations : pixels * 8L)} " +
                    $"windows_per_pixel={(arm is 4 or 6 or 7 ? windowEvaluations / (double)pixels : 8):F4} " +
                    $"increment_ms={increment:F4} " + (full ? $"projected_export_increment_ms={increment:F4}" :
                    $"projected_tick_ms={(basis.Info.IsRawSource ? 58.4 : 54.6) + increment:F4} " +
                    $"projected_contended_ms={(basis.Info.IsRawSource ? 125.8 : 132.0) + increment:F4}") +
                    " projection_only=True fixed_tick_ceiling_ms=150");
            }
            var windowsMs = Median(times[2].Zip(times[1], (a, b) => a - b).ToArray());
            var total = Median(times[2].Zip(times[0], (a, b) => a - b).ToArray());
            Print(.45, $"eight_window_increment_ms={windowsMs:F4} full={full} " +
                (full ? $"projected_export_increment_ms={total:F4}" : $"projected_tick_ms={(basis.Info.IsRawSource ? 58.4 : 54.6) + total:F4} " +
                $"projected_contended_ms={(basis.Info.IsRawSource ? 125.8 : 132.0) + total:F4} " +
                "projection_only=True fixed_tick_ceiling_ms=150"));
        }
        else
        {
            Print(.45, $"overlay_allocate_commit_ms={Median(allocationsMs):F4} field_tint_compute_ms={Median(computationsMs):F4}");
            Assert.Equal(pixels, field!.Length); Assert.Equal(pixels * 4, bitmap!.Length);
            Print(.45, $"overlay_float_field_bytes={pixels * 4L} premultiplied_bgra_bytes={pixels * 4L}; native upload excluded");
        }
    }
}
