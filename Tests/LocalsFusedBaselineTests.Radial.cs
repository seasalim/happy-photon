using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsFusedBaselineTests
{
    private double _radialCoverage;
    private EditSettings RadialSettings(BaseImage b, bool eight = false)
    {
        var w = (int)b.Pixels.Width;
        var h = (int)b.Pixels.Height;
        var edge = Math.Max(w, h);
        var local = new LocalAdjustment { Type = "radial", Angle = 30, Feather = .5, Exposure = 2 };
        var settings = new EditSettings { Locals = [local] };
        if (eight)
            settings.Locals = Enumerable.Range(0, 8).Select(i => local with
            {
                Id = Guid.NewGuid().ToString("N"), Ordinal = i + 1, Cu = .2 * (i % 4 + 1),
                Cv = (i / 4 + 1) / 3d, Rx = .11, Ry = .11, Angle = 0
            }).ToList();
        else
        {
            // Centered ellipse fits both frozen fixture frames; solve its area in L units.
            local.Rx = Math.Sqrt(.45 * w / edge * h / edge / (Math.PI * 2 / 3));
            local.Ry = local.Rx * 2 / 3;
        }
        using var geometry = RenderGeometry.Apply(b.Pixels, settings, out var trace);
        var plan = RenderLocals.Create(settings, trace, w, h)!;
        var geometric = 0;
        var nonIdentity = 0;
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        {
            var inside = settings.Locals.Any(l => RadialWeight(l, (x + .5) / edge, (y + .5) / edge,
                w / (double)edge, h / (double)edge) != 0);
            if (inside) geometric++;
            if (plan.Gain(y * w + x) != 1) nonIdentity++;
        }
        Assert.Equal(geometric, nonIdentity);
        var coverage = geometric / (double)(w * h);
        _radialCoverage = coverage;
        Print(coverage, $"workload={(eight ? "R8" : "R1")} size={w}x{h} geometric={geometric} non_identity={nonIdentity}");
        // R8 asserts after timing so an invalid frozen workload still reports its tick cost.
        if (!eight) Assert.InRange(coverage, .44, .46);
        return settings;
    }

    private static double RadialWeight(LocalAdjustment local, double x, double y, double fw, double fh)
    {
        var theta = local.Angle * Math.PI / 180;
        var dx = x - local.Cu * fw;
        var dy = y - local.Cv * fh;
        var a = (dx * Math.Cos(theta) + dy * Math.Sin(theta)) / local.Rx;
        var b = (dy * Math.Cos(theta) - dx * Math.Sin(theta)) / local.Ry;
        var radius = Math.Sqrt(a * a + b * b);
        var inside = radius >= 1 ? 0 : radius <= 1 - local.Feather ? 1 :
            1 - Math.Pow((radius - 1 + local.Feather) / local.Feather, 2) *
                (3 - 2 * (radius - 1 + local.Feather) / local.Feather);
        return local.Outside ? 1 - inside : inside;
    }

    [Fact]
    public void QualifiedRadialG5()
    {
        OptIn();
        using var full = Load(true);
        using var preview = Load(false);
        var radial = RadialSettings(preview);
        var failures = new List<string>();
        // Run 245 pins (user-approved 2026-09-05): the radial excess over linear is exposure
        // non-commutation with the resize, ordered off < linear < radial 45 % < global 100 %;
        // the weight field itself is pinned exact below.
        var raw = Fixture.EndsWith("cr2");
        (double Mean, double P99) global = default;
        foreach (var arm in new[] { "off", "linear", "global", "radial-soft", "radial-hard" })
        {
            var settings = arm switch
            {
                "off" => new EditSettings(), "linear" => LocalSettings(preview, .25, .45),
                "global" => new EditSettings { Exposure = 2 }, _ => radial.Clone()
            };
            if (arm == "radial-hard") settings.Locals![0].Feather = 0;
            using var p = ProductionGeometry(preview, settings);
            using var e = ProductionGeometry(full, settings);
            WysiwygTests.AlignForComparison(e, p);
            var comparison = GoldenImageComparer.Compare(e, p, GoldenComparisonDomain.DisplaySrgb);
            Print(.45, $"G5 arm={arm} mean={comparison.MeanDeltaE:F6} p99={comparison.P99DeltaE:F6}");
            if (arm == "global") global = (comparison.MeanDeltaE, comparison.P99DeltaE);
            if (!arm.StartsWith("radial")) continue;
            var hard = arm == "radial-hard";
            var (mean, p99) = raw ? (hard ? (4.0, 20.5) : (3.2, 17.5)) : (hard ? (1.8, 23.5) : (1.6, 21.0));
            if (comparison.MeanDeltaE > mean || comparison.P99DeltaE > p99)
                failures.Add($"{arm}: {comparison.MeanDeltaE:F6}/{comparison.P99DeltaE:F6} exceeds {mean}/{p99}");
            if (comparison.MeanDeltaE > global.Mean || comparison.P99DeltaE > global.P99)
                failures.Add($"{arm}: exceeds the global +2 EV ceiling {global.Mean:F6}/{global.P99:F6}");
        }
        foreach (var feather in new[] { .5, 0d })
        {
            var settings = radial.Clone();
            settings.Locals![0].Feather = feather;
            var (mean, p99, gross) = WeightFieldDisagreement(full, preview, settings);
            Print(.45, $"G5 feather={feather} weight_field mean={mean:F6} p99={p99:F6} gross_fraction={gross:F6}");
            if (feather == .5 && (mean > .001 || p99 > .005) || feather == 0 && gross > .003)
                failures.Add($"weight field at feather {feather}: {mean:F6}/{p99:F6}/{gross:F6}");
        }
        Assert.True(failures.Count == 0, "G5 pinned bound: " + string.Join("; ", failures));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RadialExactBypass(bool raw, bool mono)
    {
        var values = Enumerable.Range(0, 12000).Select(i => (ushort)(i * 7 % 65536)).ToArray();
        if (mono) for (var i = 0; i < values.Length; i += 3) values[i + 1] = values[i + 2] = values[i];
        using var b = RenderPipelineTestSupport.CreateBase(values, raw, 40, isMonochrome: mono);
        using var baseline = ProductionGeometry(b, Settings);
        var expected = RenderPipelineTestSupport.ReadPixels(baseline);
        foreach (var arm in new[] { "disabled", "zero", "outside-frame", "inside-inner", "feather-band" })
        {
            var local = new LocalAdjustment { Type = "radial", Exposure = 2, Angle = 30, Feather = .5 };
            if (arm == "disabled") local.Enabled = false;
            if (arm == "zero") local.Exposure = 0;
            if (arm == "outside-frame") local.Cu = -1;
            if (arm is "inside-inner" or "feather-band")
            { local.Outside = true; local.Rx = local.Ry = 1; local.Feather = arm == "inside-inner" ? .1 : .8; }
            using var actual = ProductionGeometry(b, new EditSettings { Locals = [local] });
            var differing = expected.Zip(RenderPipelineTestSupport.ReadPixels(actual), (a, z) => a != z).Count(v => v);
            if (arm == "feather-band") Assert.True(differing > 0);
            else Assert.Equal(0, differing);
        }
        Assert.Equal(values, RenderPipelineTestSupport.ReadPixels(b.Pixels));
    }
}
