using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsFusedBaselineTests
{
    private static EditSettings RestrictLuminance(EditSettings settings)
    {
        for (var i = 0; i < settings.Locals!.Count; i++)
            settings.Locals[i].Luminance = new() { Enabled = true, Lower = i * .08, Upper = .4 + i * .08, Softness = i % 3 == 0 ? 0 : .1 };
        return settings;
    }

    [Fact] public Task QualifiedRangeProductionContention() => ContendedTick(true, true, true, true, true);
    [Fact] public Task QualifiedRangeProductionExport() => ExportDelta(true, true, true);

    [Fact]
    public void QualifiedRangeProductionTick() => RangeProductionTick(false);

    private void RangeProductionTick(bool hue)
    {
        OptIn(); using var basis = Load(false);
        var control = Colorize(RadialSettings(basis, true));
        var ranged = RestrictLuminance(control.Clone());
        if (hue) RestrictHue(ranged);
        void Tick(EditSettings s) { using var result = new RenderPipeline().Render(new(basis, s, RenderIntent.Preview, 1600, new(false, false))); }
        for (var warm = 0; warm < 10; warm++) { Tick(control); Tick(ranged); }
        var off = new double[Samples]; var on = new double[Samples];
        var memory = new double[Samples]; var allocation = new double[Samples];
        var offPrivate = new long[Samples]; var onPrivate = new long[Samples];
        var offCaller = new long[Samples]; var onCaller = new long[Samples];
        for (var i = 0; i < Samples; i++)
        {
            var a = Measure(() => Tick(control)); var b = Measure(() => Tick(ranged));
            off[i] = a.Ms; on[i] = b.Ms; memory[i] = b.Peak - a.Peak; allocation[i] = b.Allocated - a.Allocated;
            offPrivate[i] = a.Peak; onPrivate[i] = b.Peak; offCaller[i] = a.Allocated; onCaller[i] = b.Allocated;
        }
        var increment = on.Zip(off, (a, b) => a - b).ToArray();
        Print(_radialCoverage, $"{(hue ? "LH8" : "L8")} fused control={Median(off):F4} tick={Median(on):F4} increment={Median(increment):F4} " +
            $"private_increment={Median(memory)} caller_increment={Median(allocation)} no_retained_L=True " +
            $"off=[{string.Join(',', off)}] on=[{string.Join(',', on)}] delta=[{string.Join(',', increment)}] " +
            $"private=[{string.Join(',', memory)}] caller=[{string.Join(',', allocation)}] private_report_only=True " +
            $"private_off=[{string.Join(',', offPrivate)}] private_on=[{string.Join(',', onPrivate)}] " +
            $"caller_off=[{string.Join(',', offCaller)}] caller_on=[{string.Join(',', onCaller)}]");
        AssertControlValid(Median(off), basis.Info.IsRawSource ? 55 : 50, "C8 tick");
        Assert.True(Median(on) <= 150, "L8 ordinary tick");
        Assert.True(Median(increment) <= 22, "L8 fused increment");
        // Q16 page commits can land in either tick arm; the dedicated test owns the private-memory gate.
        Assert.True(Median(allocation) <= 65536, "L8 classification caller allocation increment");
    }

    [Fact]
    public void QualifiedRangeProductionClassificationMemory() => RangeProductionClassificationMemory(false);

    private void RangeProductionClassificationMemory(bool hue)
    {
        OptIn(); using var basis = Load(false);
        var control = Colorize(RadialSettings(basis, true));
        var ranged = RestrictLuminance(control.Clone());
        if (hue) RestrictHue(ranged);
        using var geometry = RenderGeometry.Apply(basis.Pixels, control, out var trace);
        DcpHueSatRenderer.Apply(geometry, basis.Info.DcpProfile?.HueSatMap);
        var source = RenderPipelineTestSupport.ReadPixels(geometry);
        var width = (int)geometry.Width; var height = (int)geometry.Height;
        var wb = new AgxCrossing.Matrix3x3(RenderChromaticStage.CreateWhiteBalanceMatrix(basis.Info, ranged));
        var workers = Environment.ProcessorCount; var sums = new double[workers];
        void Classify(EditSettings settings)
        {
            // Include per-render preparation. Only the immutable RGB input and WB are shared;
            // no scalar classification image or prepared per-pixel weight field exists.
            var plan = RenderLocals.Create(settings, trace, width, height, info: basis.Info)!;
            Parallel.For(0, workers, worker =>
            {
                double sum = 0;
                for (var pixel = width * height * worker / workers; pixel < width * height * (worker + 1) / workers; pixel++)
                {
                    var o = pixel * 3;
                    var r = source[o] / 65535d; var g = source[o + 1] / 65535d; var b = source[o + 2] / 65535d;
                    var cr = wb.Row0(r, g, b); var cg = wb.Row1(r, g, b); var cb = wb.Row2(r, g, b);
                    plan.ApplyColor(pixel, ref cr, ref cg, ref cb);
                    sum += cr + cg + cb;
                }
                sums[worker] = sum;
            });
        }
        Classify(control); Classify(ranged);
        var memory = new double[Samples]; var allocations = new double[Samples];
        var offPrivate = new long[Samples]; var onPrivate = new long[Samples];
        var offCaller = new long[Samples]; var offMs = new double[Samples]; var onMs = new double[Samples];
        for (var i = 0; i < Samples; i++)
        {
            var a = Measure(() => Classify(control)); var b = Measure(() => Classify(ranged));
            memory[i] = b.Peak - a.Peak; allocations[i] = b.Allocated;
            offPrivate[i] = a.Peak; onPrivate[i] = b.Peak; offCaller[i] = a.Allocated;
            offMs[i] = a.Ms; onMs[i] = b.Ms;
        }
        Print(_radialCoverage, $"classification_private=[{string.Join(',', memory)}] caller_absolute=[{string.Join(',', allocations)}] " +
            $"private_increment_off_bytes=[{string.Join(',', offPrivate)}] private_increment_on_bytes=[{string.Join(',', onPrivate)}] " +
            $"caller_off_bytes=[{string.Join(',', offCaller)}] caller_on_bytes=[{string.Join(',', allocations)}] " +
            $"off_ms=[{string.Join(',', offMs)}] on_ms=[{string.Join(',', onMs)}] " +
            $"no_retained_L=True checksum={sums.Sum():R}");
        Assert.True(Median(memory) <= Math.Max(width * height * 6 * .01, 1048576), "Classification private increment median");
        Assert.All(allocations, value => Assert.True(value <= 65536));
    }

    [Fact]
    public void QualifiedRangeProductionBypass()
    {
        OptIn(); using var basis = Load(false);
        foreach (var color in new[] { false, true })
        {
            var absent = RadialSettings(basis, true);
            if (color) Colorize(absent);
            using var expected = ProductionGeometry(basis, absent);
            var codes = RenderPipelineTestSupport.ReadPixels(expected);
            // Frozen from an isolated build of 7f7c708, Windows Q16 RGB, before this implementation.
            var frozenHash = (basis.Info.IsRawSource, color) switch
            {
                (true, false) => "9D7B78101EC6EFFF1CA5D63717D1C7C4D97C562FAACFB56ADF3A718533F4EAD0",
                (true, true) => "7A1F77B472CE30A4324B7D48140BDCE1B372C6C6BC94FDE4EE6265E9288A18AB",
                (false, false) => "615E69C85E73DF6FF6618127C02DB0E410983BA1C55646E6C5FE5CCA4BD4DC15",
                _ => "0C3A17664620450A415D1C22584E6285C3FD582379E952F54A11C4578FD41198"
            };
            var bytes = new byte[codes.Length * 2]; Buffer.BlockCopy(codes, 0, bytes, 0, bytes.Length);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            if (OperatingSystem.IsWindows()) Assert.Equal(frozenHash, hash);
            Print(_radialCoverage, $"frozen_base=7f7c708 color={color} absent_hash={hash}");
            foreach (var range in new[] { new LuminanceRange { Enabled = true }, new LuminanceRange { Enabled = false, Lower = .47 } })
            {
                var variant = absent.Clone(); foreach (var local in variant.Locals!) local.Luminance = range;
                using var actual = ProductionGeometry(basis, variant);
                Assert.Equal(codes, RenderPipelineTestSupport.ReadPixels(actual));
                Print(_radialCoverage, $"bypass color={color} enabled={range.Enabled} open={range.Lower == 0} differing_codes=0");
            }
        }
    }

    [Fact]
    public void QualifiedRangeProductionAgreement()
    {
        OptIn(); using var full = Load(true); using var preview = Load(false);
        var settings = RadialSettings(preview);
        settings.Locals![0].Luminance = new() { Enabled = true, Lower = .47 };
        var pipeline = new RenderPipeline();
        using var p = pipeline.Render(new(preview, settings, RenderIntent.Preview, 1600, new(false, false)));
        using var e = pipeline.Render(new(full, settings, RenderIntent.Export, null, new(false, false)));
        WysiwygTests.AlignForComparison(e.Image, p.Image);
        var result = GoldenImageComparer.Compare(e.Image, p.Image, GoldenComparisonDomain.DisplaySrgb);
        using var pm = WeightField(preview, settings); using var em = WeightField(full, settings);
        em.Resize(new MagickGeometry(pm.Width, pm.Height) { IgnoreAspectRatio = true });
        var errors = RenderPipelineTestSupport.ReadPixels(pm).Zip(RenderPipelineTestSupport.ReadPixels(em),
            (a, b) => Math.Abs(a - b) / 65535d).Order().ToArray();
        Print(.45, $"L1 preview_export mean_deltaE={result.MeanDeltaE:F6} p99_deltaE={result.P99DeltaE:F6} " +
            $"weight_mean={errors.Average():F6} weight_p99={errors[(int)(errors.Length * .99)]:F6} weight_max={errors[^1]:F6} " +
            "alignment=WysiwygTests.AlignForComparison weight_resize=Magick_default_linear_Rec2020 bounds=WP6");
        Assert.InRange(result.MeanDeltaE, 0, preview.Info.IsRawSource ? 1.85 : 1.5);
        Assert.InRange(result.P99DeltaE, 0, preview.Info.IsRawSource ? 11.1 : 21.1);
    }

    private static MagickImage WeightField(BaseImage basis, EditSettings settings)
    {
        var image = RenderGeometry.Apply(basis.Pixels, settings, out var trace);
        DcpHueSatRenderer.Apply(image, basis.Info.DcpProfile?.HueSatMap);
        var neutral = settings.Clone(); neutral.Locals![0].Exposure = 1; neutral.Locals[0].Luminance = null; neutral.Locals[0].Hue = null;
        var plan = RenderLocals.Create(neutral, trace, (int)image.Width, (int)image.Height)!;
        var wb = new AgxCrossing.Matrix3x3(RenderChromaticStage.CreateWhiteBalanceMatrix(basis.Info, settings));
        using var pixels = image.GetPixels();
        var values = pixels.GetArea(0, 0, image.Width, image.Height)!;
        Parallel.For(0, values.Length / 3, pixel =>
        {
            var o = pixel * 3;
            var r = values[o] / 65535d; var g = values[o + 1] / 65535d; var b = values[o + 2] / 65535d;
            var weight = (plan.Gain(pixel) - 1) * LuminanceWindow.Weight(settings.Locals![0].Luminance!,
                OklabColor.ClassifyLightness(wb.Row0(r, g, b), wb.Row1(r, g, b), wb.Row2(r, g, b)));
            if (settings.Locals[0].Hue is { } hue)
            {
                var lab = OklabColor.Classify(wb.Row0(r, g, b), wb.Row1(r, g, b), wb.Row2(r, g, b));
                weight *= HueWindow.Weight(hue, lab.Hue, lab.Chroma);
            }
            values[o] = values[o + 1] = values[o + 2] = (ushort)Math.Round(weight * 65535);
        });
        pixels.SetArea(0, 0, image.Width, image.Height, values);
        return image;
    }
}
