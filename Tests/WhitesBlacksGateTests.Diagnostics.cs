using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class WhitesBlacksGateTests
{
    [Fact]
    public void R1ParityDiagnostic()
    {
        OptIn(); var loader = Loader(); var file = File();
        using var pair = loader.LoadPreviewBaseWithOutcome(file, BaseDecodeSettings.Default, CancellationToken.None).Pair;
        using var full = loader.LoadFullBase(file, BaseDecodeSettings.Default, CancellationToken.None);
        Assert.NotNull(pair?.Large); Assert.NotNull(full);
        foreach (var production in new[] { pair.Interactive, pair.Large })
        {
            // Clone the full neutral base before resizing: no preview decode or metadata/bias gap.
            using var derived = new BaseImage(new MagickImage(full.Pixels), full.Info);
            derived.Pixels.Resize(new MagickGeometry(production.Pixels.Width, production.Pixels.Height) { IgnoreAspectRatio = true });
            foreach (var sharpenOff in new[] { false, true })
            foreach (var amount in new[] { 0, -100, 100 })
            {
                var settings = Points(amount);
                if (sharpenOff) settings.Detail.CaptureSharpen = 0;
                using var expected = Render(full, settings, RenderIntent.Export);
                using var actual = Render(derived, settings, RenderIntent.Preview);
                var comparison = Compare(expected, actual);
                // This additional arm really has identical pixels at the operator input.
                // Downsample-before-tone vs tone-before-downsample above still do not commute.
                using var sameInput = Render(derived, settings, RenderIntent.Export);
                var intent = Compare(sameInput, actual);
                var left = RenderPipelineTestSupport.ReadPixels(sameInput);
                var right = RenderPipelineTestSupport.ReadPixels(actual);
                Report("R1-derived-base-parity", new { diagnostic = true, sharpenOff, amount, width = derived.Pixels.Width,
                    height = derived.Pixels.Height, mean = comparison.MeanDeltaE, p99 = comparison.P99DeltaE,
                    identicalInputMean = intent.MeanDeltaE, identicalInputP99 = intent.P99DeltaE,
                    identicalInputDifferingCodes = left.Zip(right).Count(p => p.First != p.Second),
                    previewBiasEv = production.Info.SourceExposureBiasEv, fullBiasEv = full.Info.SourceExposureBiasEv });
                if (sharpenOff) Assert.Equal(left, right);
            }
        }
        foreach (var raw in new[] { false, true })
        {
            var op = new WhitesBlacksOperator(100, 100, raw);
            var low = raw ? -10 : -5.5; var high = raw ? 6.5 : Math.Log2(1 / .18);
            var slopes = Enumerable.Range(0, 100001).Select(i =>
            {
                var y = .18 * Math.Pow(2, low + (high - low) * i / 100000);
                var h = y * 1e-6;
                double Map(double x) => x * op.Gain(WhitesBlacksOperator.Position(x));
                return (Map(y + h) - Map(y - h)) / (2 * h);
            }).Order().ToArray();
            Report("R1-slope", new { diagnostic = true, raw, amount = 100, samples = slopes.Length,
                distribution = "uniform log2 luminance across the frozen regime window",
                metric = "d(output luminance)/d(input luminance), before tone", maximum = slopes[^1], p99 = slopes[99000] });
        }
    }
}
