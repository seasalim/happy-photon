using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class HealGateTests
{
    // Report-only: each disc's error in the completed, creation-ordered S64 render.
    // Overlapping pixels belong to each covering disc; these means are not additive.
    [Fact]
    public void G1PerSpotDiagnostic()
    {
        OptIn();
        Assert.Equal(Heic, Fixture);
        var loader = Loader(); var file = LocalFile(); var pipeline = new RenderPipeline();
        using var pair = loader.LoadPreviewBaseWithOutcome(file, BaseDecodeSettings.Default, CancellationToken.None).Pair;
        using var full = loader.LoadFullBase(file, BaseDecodeSettings.Default, CancellationToken.None);
        Assert.NotNull(pair); Assert.NotNull(full);
        var basis = pair.Interactive;
        Assert.Equal("1200x1600", Size(basis));
        var spots = HealWorkloads.S64();
        foreach (var candidate in HealCandidate.FinalOnly)
        {
            using var repairedFull = new HealProductionStage().Repair(full, spots, candidate);
            using var repaired = new HealProductionStage().Repair(basis, spots, candidate);
            using var reference = pipeline.Render(new(repairedFull, new(), RenderIntent.Export, null, new(false, false)));
            using var actual = pipeline.Render(new(repaired, new(), RenderIntent.Preview, null, new(false, false)));
            using var aligned = new MagickImage(reference.Image);
            WysiwygTests.AlignForComparison(aligned, actual.Image);
            var width = (int)actual.Image.Width; var height = (int)actual.Image.Height;
            for (var i = 0; i < spots.Length; i++)
            {
                var spot = spots[i];
                var radius = Math.Min(spot.Radius * Math.Max(width, height), Math.Min(width, height) / 2d);
                var cx = spot.U * width - .5; var cy = spot.V * height - .5;
                var left = Math.Max(0, (int)Math.Floor(cx - radius));
                var top = Math.Max(0, (int)Math.Floor(cy - radius));
                var w = Math.Min(width - 1, (int)Math.Ceiling(cx + radius)) - left + 1;
                var h = Math.Min(height - 1, (int)Math.Ceiling(cy + radius)) - top + 1;
                var mask = Enumerable.Range(0, w * h).Select(p =>
                    Math.Pow(left + p % w - cx, 2) + Math.Pow(top + p / w - cy, 2) < radius * radius).ToArray();
                using var expectedPatch = new MagickImage(aligned);
                using var actualPatch = new MagickImage(actual.Image);
                var crop = new MagickGeometry(left, top, (uint)w, (uint)h);
                expectedPatch.Crop(crop); expectedPatch.ResetPage();
                actualPatch.Crop(crop); actualPatch.ResetPage();
                var metric = GoldenImageComparer.Compare(expectedPatch, actualPatch, GoldenComparisonDomain.DisplaySrgb, mask);
                Report("G1-per-spot-diagnostic", new { diagnostic = true, formulation = candidate.ToString(),
                    size = Size(basis), spot = i + 1, spotKind = spot.IsClone ? "clone" : "heal", spot.Radius,
                    pixels = mask.Count(v => v), mean = metric.MeanDeltaE,
                    attribution = "final S64 disc; overlaps counted in each covering disc" });
            }
        }
    }
}
