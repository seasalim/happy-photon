using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsFusedBaselineTests
{
    private static EditSettings RestrictHue(EditSettings settings)
    {
        for (var i = 0; i < settings.Locals!.Count; i++)
            settings.Locals[i].Hue = new() { Enabled = true, Center = i * 45,
                Width = i == 7 ? 360 : 60, Softness = i % 3 == 0 ? 0 : 30 };
        return settings;
    }
    [Fact] public void QualifiedRangeHueTick() => RangeProductionTick(true);
    [Fact] public void QualifiedRangeHueClassificationMemory() => RangeProductionClassificationMemory(true);
    [Fact] public Task QualifiedRangeHueContention() => ContendedTick(true, true, true, true, true, true);
    [Fact] public Task QualifiedRangeHueExport() => ExportDelta(true, true, true, true);
    [Fact]
    public void QualifiedRangeHueBypass()
    {
        OptIn(); using var basis = Load(false);
        foreach (var luminance in new[] { false, true })
        foreach (var hueOff in new[] { false, true })
        foreach (var color in new[] { false, true })
        {
            var settings = RadialSettings(basis, true);
            if (color) Colorize(settings);
            if (luminance) RestrictLuminance(settings);
            if (hueOff) foreach (var local in settings.Locals!) local.Hue = new() { Center = 350 };
            using var geometry = RenderGeometry.Apply(basis.Pixels, settings, out var trace);
            var previous = Wp3RenderLocals.Create(settings, trace, trace.Width, trace.Height, info: basis.Info)!;
            var current = RenderLocals.Create(settings, trace, trace.Width, trace.Height, info: basis.Info)!;
            var values = RenderPipelineTestSupport.ReadPixels(geometry);
            var differences = 0;
            Parallel.For(0, trace.Width * trace.Height, pixel =>
            {
                var o = pixel * 3;
                var r = values[o] / 65535d; var g = values[o + 1] / 65535d; var b = values[o + 2] / 65535d;
                var cr = r; var cg = g; var cb = b;
                previous.ApplyColor(pixel, ref r, ref g, ref b, 1.3);
                current.ApplyColor(pixel, ref cr, ref cg, ref cb, 1.3);
                if ((r, g, b) != (cr, cg, cb) || previous.Gain(pixel) != current.Gain(pixel)) Interlocked.Increment(ref differences);
            });
            Assert.Equal(0, differences);
            if (luminance)
            {
                using var rendered = ProductionGeometry(basis, settings);
                var codes = RenderPipelineTestSupport.ReadPixels(rendered);
                var bytes = new byte[codes.Length * 2]; Buffer.BlockCopy(codes, 0, bytes, 0, bytes.Length);
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
                // Isolated Release build of 6303626, Canon/HEIC 1600 px, Q16 RGB.
                var expected = (basis.Info.IsRawSource, color) switch
                {
                    (true, false) => "17E4201CA447FB53F13E6FEE1EDAEF3EF9FF1E2E39753EB49C70B6831332817C",
                    (true, true) => "B8BF8B03A705AEDBCC31FA030A81408B20654630C8F334CA9FA87A5F00B347EC",
                    (false, false) => "F4D73C757E27F60C1C70E825F74148CCAA15D5B7296A24805B09F11489AE96FE",
                    _ => "7BA2A7B4B729A4C25B82FE79A962E71B70413B4D28F26113734B2B66FD54524F"
                };
                if (OperatingSystem.IsWindows()) Assert.Equal(expected, hash);
                Print(_radialCoverage, $"frozen_base=6303626 luminance=True hueOff={hueOff} color={color} hash={hash} differing_codes=0");
            }
            Print(_radialCoverage, $"base=6303626 luminance={luminance} hueOff={hueOff} color={color} differing_evaluator_doubles={differences}");
        }
        QualifiedRangeProductionBypass();
    }

    [Fact]
    public void QualifiedRangeHueAgreement()
    {
        OptIn(); using var full = Load(true); using var preview = Load(false);
        var settings = RestrictHue(RestrictLuminance(Colorize(RadialSettings(preview, true))));
        var pipeline = new RenderPipeline();
        using var p = pipeline.Render(new(preview, settings, RenderIntent.Preview, 1600, new(false, false)));
        using var e = pipeline.Render(new(full, settings, RenderIntent.Export, null, new(false, false)));
        WysiwygTests.AlignForComparison(e.Image, p.Image);
        var result = GoldenImageComparer.Compare(e.Image, p.Image, GoldenComparisonDomain.DisplaySrgb);
        Print(_radialCoverage, $"LH8 preview_export mean_deltaE={result.MeanDeltaE:F6} p99_deltaE={result.P99DeltaE:F6} observation_only=True");
        foreach (var local in settings.Locals!)
        {
            var isolated = settings.Clone(); isolated.Locals = [local];
            using var pm = WeightField(preview, isolated); using var em = WeightField(full, isolated);
            em.Resize(new MagickGeometry(pm.Width, pm.Height) { IgnoreAspectRatio = true });
            var errors = RenderPipelineTestSupport.ReadPixels(pm).Zip(RenderPipelineTestSupport.ReadPixels(em),
                (a, b) => Math.Abs(a - b) / 65535d).Order().ToArray();
            Print(_radialCoverage, $"local={local.Ordinal} weight_mean={errors.Average():F6} weight_p99={errors[(int)(errors.Length * .99)]:F6} weight_max={errors[^1]:F6}");
        }
    }
}
