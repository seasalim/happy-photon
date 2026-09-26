using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class OpsGateTests
{
    [Fact]
    public void HazeConstruction()
    {
        OptIn();
        using var full = Loader().LoadFullBase(LocalFile("canon-eos-6d-iso-6400.cr2"), BaseDecodeSettings.Default, CancellationToken.None);
        Assert.NotNull(full);
        using var pixels = new MagickImage(full.Pixels); pixels.Resize(1600, 0);
        using var small = new BaseImage(new MagickImage(pixels), full.Info);
        foreach (var basis in new[] { full, small })
        {
            using var original = Render(basis, OpsArm.Off);
            // Only operators-off images are consulted when calibrating construction.
            foreach (var transmission in new[] { .90, .95, .975, .98 })
            {
                var variation = (1 - transmission) / 2;
                using var haze = OpsWorkloads.Haze(basis, transmission, variation);
                using var actual = Render(haze, OpsArm.Off);
                var baseline = Compare(original, actual).MeanDeltaE;
                Report("haze-construction", new { size = Size(basis), transmission, variation,
                    airlight = OpsWorkloads.HazeAirlight, baseline, valid = baseline >= 10 && baseline <= 20 });
            }
        }
    }

    [Fact]
    public void G1SharedAnalysis()
    {
        OptIn(); var loader = Loader(); var file = LocalFile();
        using var pair = loader.LoadPreviewBaseWithOutcome(file, BaseDecodeSettings.Default, CancellationToken.None).Pair;
        using var full = loader.LoadFullBase(file, BaseDecodeSettings.Default, CancellationToken.None);
        Assert.NotNull(pair?.Large); Assert.NotNull(full);
        var fullAnalysis = OpsDehaze.Build(full.Pixels,
            RenderChromaticStage.CreateWhiteBalanceMatrix(full.Info, new()), Workers);
        using var controlFull = Render(full, OpsArm.Off);
        foreach (var arm in new[] { new OpsArm("DH+", Dehaze: 50), OpsArm.Stack })
        {
            using var reference = OpsRenderHarness.Render(full, arm, Candidate, Refine, workers: Workers, analysis: fullAnalysis);
            foreach (var basis in new[] { pair.Interactive, pair.Large })
            {
                var own = OpsDehaze.Build(basis.Pixels,
                    RenderChromaticStage.CreateWhiteBalanceMatrix(basis.Info, new()), Workers);
                var shared = fullAnalysis.Resample(own.Transmission.Width, own.Transmission.Height);
                using var controlImage = Render(basis, OpsArm.Off, RenderIntent.Preview);
                using var ownImage = OpsRenderHarness.Render(basis, arm, Candidate, Refine, RenderIntent.Preview, Workers, analysis: own);
                using var sharedImage = OpsRenderHarness.Render(basis, arm, Candidate, Refine, RenderIntent.Preview, Workers, analysis: shared);
                var control = Compare(controlFull, controlImage);
                var original = Compare(reference, ownImage); var metric = Compare(reference, sharedImage);
                Report("G1-shared-analysis", new { arm = arm.Name, size = Size(basis), full = Size(full), reportOnly = true,
                    lattice = $"{shared.Transmission.Width}x{shared.Transmission.Height}",
                    controlMean = control.MeanDeltaE, controlP99 = control.P99DeltaE,
                    originalMean = original.MeanDeltaE, originalP99 = original.P99DeltaE,
                    mean = metric.MeanDeltaE, p99 = metric.P99DeltaE,
                    meanLimit = control.MeanDeltaE + .5, p99Limit = control.P99DeltaE + 2,
                    excessPersists = metric.MeanDeltaE > control.MeanDeltaE + .5 || metric.P99DeltaE > control.P99DeltaE + 2,
                    airlightRelative = own.Airlight.Zip(shared.Airlight, (a, b) => Math.Abs(a - b) / Math.Abs(b)).ToArray(),
                    transmissionMaxDifference = own.Transmission.Values.Zip(shared.Transmission.Values, (a, b) => Math.Abs(a - b)).Max() });
            }
        }
    }
}
