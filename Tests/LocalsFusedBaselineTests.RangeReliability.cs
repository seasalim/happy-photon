using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;
using static HappyPhoton.Tests.LocalsRangeOracle;

namespace HappyPhoton.Tests;

public sealed partial class LocalsFusedBaselineTests
{
    [Fact]
    public void QualifiedRangeReliability()
    {
        OptIn();
        var fixtures = new[] { "canon-eos-6d-iso-6400.cr2", "iphone-14-pro-iso-1000.heic" };
        var distributions = new List<double[]>();
        var crossings = new List<(double? Start, double? End)>();
        foreach (var fixture in fixtures)
        {
            var path = GoldenTestPaths.Asset(fixture);
            var attributes = File.GetAttributes(path);
            Assert.True(((int)attributes & (0x1000 | 0x40000 | 0x400000)) == 0, "Fixture must be locally available");
            using var basis = Loader().LoadPreviewBase(new ImageFile(path), BaseDecodeSettings.Default, CancellationToken.None)!;
            Assert.NotNull(basis);
            using var image = RenderGeometry.Apply(basis.Pixels, Settings, out _);
            DcpHueSatRenderer.Apply(image, basis.Info.DcpProfile?.HueSatMap);
            var values = RenderPipelineTestSupport.ReadPixels(image);
            var width = (int)image.Width; var height = (int)image.Height;
            Assert.Equal(1600, Math.Max(width, height));
            var matrix = new DcpRenderMatrix(RenderChromaticStage.CreateWhiteBalanceMatrix(basis.Info, Settings));
            var rows = new Lab[3 * width]; // Bounded rolling rows, never a full-frame L/hue image.
            var bins = Enumerable.Range(0, 201).Select(_ => new List<double>()).ToArray();
            void Row(int y)
            {
                for (var x = 0; x < width; x++)
                {
                    var o = (y * width + x) * 3;
                    var r = values[o] / 65535d; var g = values[o + 1] / 65535d; var b = values[o + 2] / 65535d;
                    rows[(y % 3) * width + x] = Classify(matrix.Row0(r, g, b), matrix.Row1(r, g, b), matrix.Row2(r, g, b));
                }
            }
            Row(0); Row(1);
            for (var y = 1; y < height - 1; y++)
            {
                Row(y + 1);
                for (var x = 1; x < width - 1; x++)
                {
                    double sin = 0, cos = 0;
                    for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
                    {
                        var lab = rows[((y + dy) % 3) * width + x + dx];
                        // An exactly undefined hue contributes no coherent vector.
                        if (lab.C == 0) continue;
                        sin += lab.B / lab.C; cos += lab.A / lab.C;
                    }
                    var resultant = Math.Clamp(Math.Sqrt(sin * sin + cos * cos) / 9, 1e-15, 1);
                    var deviation = Math.Sqrt(-2 * Math.Log(resultant)) * 180 / Math.PI;
                    var c = rows[(y % 3) * width + x].C;
                    bins[Math.Min(200, (int)(c / .002))].Add(deviation);
                }
            }
            var d = bins.Select(bin => bin.Count >= 100 ? Median(bin.ToArray()) : double.NaN).ToArray();
            distributions.Add(d);
            var crossing = RangeCrossings(d); crossings.Add(crossing);
            output.WriteLine($"reliability fixture={fixture} size={width}x{height} bin_width=.002 min_count=100 " +
                $"start={crossing.Start?.ToString("R") ?? "missing"} end={crossing.End?.ToString("R") ?? "missing"}");
            for (var i = 0; i < bins.Length; i++)
                if (bins[i].Count != 0) output.WriteLine($"C=[{i * .002:F3},{(i + 1) * .002:F3}) n={bins[i].Count} D_deg={d[i]:R}");
        }
        var worse = distributions[0].Zip(distributions[1], Math.Max).ToArray();
        var combined = RangeCrossings(worse);
        var inconclusive = crossings.Any(c => c.Start == null || c.End == null || c.Start >= c.End) ||
            combined.Start == null || combined.End == null || combined.Start >= combined.End ||
            Ratio(crossings.Select(c => c.Start)) > 2 || Ratio(crossings.Select(c => c.End)) > 2;
        output.WriteLine($"reliability_result={(inconclusive ? "INCONCLUSIVE" : "proposed")} " +
            $"combined_start={combined.Start} combined_end={combined.End}; pinned ramp=.01-.04");
        Sweep(.01, .04, "pinned");
        if (!inconclusive) Sweep(combined.Start!.Value, combined.End!.Value, "measured");
        void Sweep(double start, double end, string label)
        {
            // Saturated dark blue Rec.2020 at 0 EV; scale input radiance, not global Exposure.
            var sample = Classify(.0005, .00075, .0075);
            double? belowOne = null, belowHalf = null;
            for (var step = 0; step <= 400; step++)
            {
                var ev = -step / 100d;
                var c = sample.C * Math.Pow(2, ev / 3);
                var r = Reliability(c, start, end);
                if (r < 1) belowOne ??= ev;
                if (r < .5) belowHalf ??= ev;
                if (step % 100 == 0) output.WriteLine($"EV_sweep {label} RGB0=[.0005,.00075,.0075] EV={ev} C={c:R} r={r:R}");
            }
            output.WriteLine($"EV_crossings {label} r_below_1={belowOne?.ToString() ?? "not in -4..0"} " +
                $"r_below_half={belowHalf?.ToString() ?? "not in -4..0"} step=.01EV");
        }
    }

    // Conservative monotone envelopes: prefix minimum for the >=45-degree region,
    // suffix maximum for the <=15-degree region. Missing interior bins break evidence.
    internal static (double? Start, double? End) RangeCrossings(double[] deviations)
    {
        var last = Array.FindLastIndex(deviations, double.IsFinite);
        double? start = null, end = null;
        var prefix = double.PositiveInfinity;
        for (var i = 0; i <= last; i++)
        {
            prefix = Math.Min(prefix, deviations[i]);
            if (prefix >= 45) start = (i + 1) * .002;
        }
        var suffix = double.NegativeInfinity;
        for (var i = last; i >= 0; i--)
        {
            suffix = Math.Max(suffix, deviations[i]);
            if (suffix <= 15) end = i * .002;
        }
        return (start, end);
    }

    private static double Ratio(IEnumerable<double?> source)
    {
        var values = source.Select(v => v ?? double.NaN).ToArray();
        return values.Min() <= 0 ? double.PositiveInfinity : values.Max() / values.Min();
    }
}
