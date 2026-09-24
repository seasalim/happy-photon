using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsContractPrototypeTests
{
    // Fixed BEFORE any measurements. The optimized evaluator must retain this numerical agreement bound.
    private const double BrushAgreementTolerance = 1e-10;

    [Fact]
    public void BrushGridAgreesWithIndependentOracle()
    {
        const int seed = 271042;
        var random = new Random(seed);
        double worst = 0, sum = 0;
        long comparisons = 0;
        foreach (var (width, height) in new[] { (320, 213), (213, 320), (256, 256), (511, 37) })
        {
            var strokes = new List<BrushStroke>
            {
                new([new(.1, .1), new(.9, .9), new(.1, .1)], .04, .5, .35, false), // self overlap
                new([new(.1, .9), new(.9, .1)], .04, 1, 1, false), // crossing
                new([new(.5, -.2), new(.5, 1.2)], .025, .5, .8, true), // erase after paint
                new([new(.5, .5)], .125, 0, .05, false), // single point, hard disc, min flow
                new([new(.5, .5), new(.5, .5)], .04, .5, 1, true), // duplicate points
                new([new(-1, -1), new(2, 2)], .01, 1, .6, false), // outside endpoints
                new([new(.25, 0), new(.25, 1)], .03, .01, .3, false)
            };
            for (var i = 0; i < 30; i++)
            {
                var points = Enumerable.Range(0, i % 7 + 1)
                    .Select(_ => new BrushPoint(random.NextDouble() * 1.4 - .2, random.NextDouble() * 1.4 - .2)).ToArray();
                strokes.Add(new(points, .01 + random.NextDouble() * .08, i % 3 == 0 ? 0 : random.NextDouble(),
                    .05 + .95 * random.NextDouble(), i % 4 == 3));
            }
            var document = new BrushDocument(strokes.ToArray());
            Compare(document);
            Compare(LocalsBrushOracle.Decode(LocalsBrushOracle.Encode(document)));
            // Crossing axis-aligned segments, erase and a single-point hard disc.
            if (width == height)
                Compare(new([
                    new([new(.5, 0), new(.5, 1)], .03, .5, .35, false),
                    new([new(0, .5), new(1, .5)], .03, 1, .8, false),
                    new([new(.5, 0), new(.5, 1)], .015, .5, .6, true),
                    new([new(.5, .5)], .125, 0, .05, false)]));

            void Compare(BrushDocument doc)
            {
                doc = LocalsBrushProduction.Quantized(doc);
                var grid = new LocalBrushEvaluator(LocalsBrushProduction.Strokes(doc), width, height);
                for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                    Check((x + .5) / width, (y + .5) / height);
                // Fitted-grid boundaries and adjacent doubles on both axes.
                foreach (var (u, v) in grid.Boundaries())
                foreach (var du in new[] { -1, 0, 1 }) foreach (var dv in new[] { -1, 0, 1 })
                    Check(Math.Clamp(du < 0 ? Math.BitDecrement(u) : du > 0 ? Math.BitIncrement(u) : u, 0, 1),
                        Math.Clamp(dv < 0 ? Math.BitDecrement(v) : dv > 0 ? Math.BitIncrement(v) : v, 0, 1));
                // Check the complete geometric x luminance x hue contract independently too.
                // The index arm uses production range math; the oracle uses LocalsRangeOracle.
                for (var i = 0; i < 512; i++)
                {
                    var u = random.NextDouble(); var v = random.NextDouble();
                    var r = random.NextDouble() * 2 - .1; var g = random.NextDouble(); var b = random.NextDouble();
                    var window = i % 8;
                    var light = new LocalsRangeOracle.LightWindow(window * .08, .4 + window * .08, window % 3 == 0 ? 0 : .1);
                    var hue = new LocalsRangeOracle.HueWindow(window * 45, window == 7 ? 360 : 60, window % 3 == 0 ? 0 : 30);
                    var expected = LocalsBrushOracle.RangedWeight(doc, u, v, width, height, r, g, b, light, hue);
                    var lab = HappyPhoton.Services.OklabColor.Classify(r, g, b);
                    var actual = grid.Weight(u, v) * HappyPhoton.Services.LuminanceWindow.Weight(
                        new() { Enabled = true, Lower = light.Lower, Upper = light.Upper, Softness = light.Softness }, lab.L) *
                        HappyPhoton.Services.HueWindow.Weight(new() { Enabled = true, Center = hue.Center,
                            Width = hue.Width, Softness = hue.Softness }, lab.Hue, lab.Chroma);
                    var error = Math.Abs(expected - actual);
                    worst = Math.Max(worst, error); sum += error; comparisons++;
                }
                void Check(double u, double v)
                {
                    var expected = LocalsBrushOracle.Weight(doc, u, v, width, height);
                    var actual = grid.Weight(u, v);
                    var error = Math.Abs(expected - actual);
                    worst = Math.Max(worst, error); sum += error; comparisons++;
                }
            }
        }
        output.WriteLine($"brush_oracle seed={seed} comparisons={comparisons} fixed_tolerance={BrushAgreementTolerance:R} " +
            $"max_absolute_error={worst:R} mean_absolute_error={sum / comparisons:R} persistence_roundtrip_included=True");
        Assert.True(worst <= BrushAgreementTolerance, $"Brush grid error {worst:R} exceeds fixed {BrushAgreementTolerance:R}");
    }

}
