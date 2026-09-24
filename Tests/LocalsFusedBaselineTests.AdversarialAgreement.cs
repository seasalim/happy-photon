using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsFusedBaselineTests
{
    // Support is evaluated on the aligned Q16 weight fields, once per pixel (not RGB channel).
    // Empty support is reported explicitly and excluded from the per-local bounds.
    private (int Count, double Sum, double? P99) PrintSupportDisagreement(MagickImage preview, MagickImage export, int ordinal)
    {
        var p = RenderPipelineTestSupport.ReadPixels(preview);
        var e = RenderPipelineTestSupport.ReadPixels(export);
        var errors = new List<double>();
        for (var i = 0; i < p.Length; i += 3)
            if (p[i] > 0 || e[i] > 0) errors.Add(Math.Abs(p[i] - e[i]) / 65535d);
        errors.Sort();
        double? mean = errors.Count == 0 ? null : errors.Average();
        double? p99 = errors.Count == 0 ? null : errors[(int)(errors.Count * .99)];
        Print(_radialCoverage, $"local={ordinal} support_pixels={errors.Count} frame_pixels={p.Length / 3} " +
            $"support_weight_mean={mean?.ToString("F6") ?? "empty"} support_weight_p99={p99?.ToString("F6") ?? "empty"} " +
            "support=aligned_Q16_either_positive weight_resize=Magick_default_linear_Rec2020");
        return (errors.Count, errors.Sum(), p99);
    }

    [Fact]
    public void QualifiedRangeAdversarialAgreement()
    {
        OptIn();
        using var full = Fixture.StartsWith("synthetic") ? AdversarialSynthetic() : Load(true);
        using var preview = Fixture.StartsWith("synthetic") ? SyntheticPreview(full) : Load(false);
        var ranged = RadialSettings(preview);
        var local = ranged.Locals![0];
        local.Feather = .001;
        local.Luminance = new() { Enabled = true, Lower = .35, Upper = .75, Softness = 0 };
        local.Hue = new() { Enabled = true, Center = 30, Width = 90, Softness = 0 };
        var control = new EditSettings { Exposure = 2 };
        var pipeline = new RenderPipeline();
        foreach (var (arm, settings) in new[] { ("ranged", ranged), ("global+2EV-control", control) })
        {
            using var p = pipeline.Render(new(preview, settings, RenderIntent.Preview, 1600, new(false, false)));
            using var e = pipeline.Render(new(full, settings, RenderIntent.Export, null, new(false, false)));
            WysiwygTests.AlignForComparison(e.Image, p.Image);
            var result = GoldenImageComparer.Compare(e.Image, p.Image, GoldenComparisonDomain.DisplaySrgb);
            Print(_radialCoverage, $"adversarial arm={arm} mean_deltaE={result.MeanDeltaE:F6} p99_deltaE={result.P99DeltaE:F6} " +
                $"full={full.Pixels.Width}x{full.Pixels.Height} preview={preview.Pixels.Width}x{preview.Pixels.Height} " +
                "feather=.001 luminance=.35:.75:0 hue=30:90:0 alignment=WysiwygTests.AlignForComparison " +
                "image_resize=Magick_default_display_sRGB bounds=WP6");
            if (arm == "ranged")
            {
                var bounds = Fixture.StartsWith("synthetic") ? (21.4, 60.9) :
                    full.Info.IsRawSource ? (1.9, 11.2) : (1.45, 19.7);
                Assert.InRange(result.MeanDeltaE, 0, bounds.Item1);
                Assert.InRange(result.P99DeltaE, 0, bounds.Item2);
            }
        }
        using var pm = WeightField(preview, ranged);
        using var em = WeightField(full, ranged);
        em.Resize(new MagickGeometry(pm.Width, pm.Height) { IgnoreAspectRatio = true });
        var errors = RenderPipelineTestSupport.ReadPixels(pm).Zip(RenderPipelineTestSupport.ReadPixels(em),
            (a, b) => Math.Abs(a - b) / 65535d).Order().ToArray();
        Print(_radialCoverage, $"adversarial weight_mean={errors.Average():F6} weight_p99={errors[(int)(errors.Length * .99)]:F6} weight_max={errors[^1]:F6}");
        var support = PrintSupportDisagreement(pm, em, local.Ordinal);
        Assert.True(support.Count > 0, "Adversarial workload must have measurable support");
        Assert.InRange(support.Sum / support.Count, 0,
            Fixture.StartsWith("synthetic") ? .74 : full.Info.IsRawSource ? .027 : .115);
        // Hard windows over one-pixel texture inherently reach p99=1; no tail bound.
    }

    private static BaseImage AdversarialSynthetic()
    {
        const int width = 4000, height = 2667;
        var values = new ushort[width * height * 3];
        // Hard vertical edges, 1/3/16 px texture, chromatic and luminance transitions.
        ushort[][] colors = [[42000, 3000, 1000], [3000, 42000, 1000],
            [1000, 3000, 42000], [12000, 12000, 12000], [60000, 20000, 1000], [1000, 1000, 1000]];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var cell = y < height / 3 ? 1 : y < height * 2 / 3 ? 3 : 16;
            var color = colors[(x / 500 + ((x / cell + y / cell) & 1)) % colors.Length];
            for (var c = 0; c < 3; c++) values[(y * width + x) * 3 + c] = color[c];
        }
        return RenderPipelineTestSupport.CreateBase(values, isRaw: true, height: height);
    }
}
