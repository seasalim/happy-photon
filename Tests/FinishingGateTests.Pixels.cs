using HappyPhoton.Services;
using Xunit;
using static HappyPhoton.Tests.FinishingGateSupport;

namespace HappyPhoton.Tests;

public sealed partial class FinishingGateTests
{
    [Fact]
    public void G3Parity()
    {
        OptIn();
        var candidates = Candidates();
        var gated = candidates.ToDictionary(candidate => candidate.Id, GatedSettings);
        var file = LocalFile(GoldenTestPaths.Asset(Fixture));
        var loader = Loader();
        using var pair = loader.LoadPreviewBaseWithOutcome(file, BaseDecodeSettings.Default, default).Pair;
        using var full = loader.LoadFullBase(file, BaseDecodeSettings.Default, default);
        Assert.NotNull(pair?.Large);
        Assert.NotNull(full);
        var bases = new[] { pair.Interactive, pair.Large };
        var controls = new GoldenComparison[2];
        using var controlFull = Render(full, new(), RenderIntent.Export);

        for (var index = 0; index < bases.Length; index++)
        {
            var basis = bases[index];
            Assert.Equal(full.Info.IsRawSource ? (index == 0 ? 1600u : 2748u) : (index == 0 ? 1200u : 2400u),
                basis.Pixels.Width);
            using var control = Render(basis, new(), RenderIntent.Preview);
            controls[index] = Compare(controlFull, control);
            var pin = ParityPin(full.Info.IsRawSource, index == 1);
            Assert.InRange(controls[index].MeanDeltaE, pin.Mean * .85, pin.Mean * 1.15);
            Assert.InRange(controls[index].P99DeltaE, pin.P99 * .85, pin.P99 * 1.15);
        }

        var allPass = true;

        foreach (var candidate in candidates)
        {
            foreach (var arm in new[] { "gated", "full" })
            {
                var settings = arm == "gated" ? gated[candidate.Id] : candidate.Settings;
                using var reference = Render(full, settings, RenderIntent.Export);

                for (var index = 0; index < bases.Length; index++)
                {
                    var basis = bases[index];
                    using var actual = Render(basis, settings, RenderIntent.Preview);
                    var control = controls[index];
                    var metric = Compare(reference, actual);
                    var pin = ParityPin(full.Info.IsRawSource, index == 1);
                    var meanLimit = control.MeanDeltaE + .5;
                    var p99Limit = control.P99DeltaE + 2;
                    var withinBound = metric.MeanDeltaE <= meanLimit && metric.P99DeltaE <= p99Limit;
                    var pass = arm == "full" || withinBound;
                    allPass &= pass;
                    Report("G3", candidate, Fixture, new
                    {
                        arm, width = basis.Pixels.Width, height = basis.Pixels.Height,
                        controlMean = control.MeanDeltaE, controlP99 = control.P99DeltaE,
                        pinnedMean = pin.Mean, pinnedP99 = pin.P99, controlTolerance = .15,
                        mean = metric.MeanDeltaE, p99 = metric.P99DeltaE, meanLimit, p99Limit,
                        withinBound, pass, reportOnly = arm == "full", exemptions = Exemptions(candidate)
                    });
                }
            }
        }

        Assert.True(allPass, "G3 gated-arm miss; full looks remain subject to visual approval");
    }

    [Fact]
    public void G4Clipping()
    {
        OptIn();
        var candidates = Candidates();
        var allPass = true;

        foreach (var pin in Controls)
        {
            using var pair = Loader().LoadPreviewBaseWithOutcome(
                LocalFile(GoldenTestPaths.Asset(pin.Name)), BaseDecodeSettings.Default, default).Pair;
            Assert.NotNull(pair);
            using var controlImage = Render(pair.Interactive, new(), RenderIntent.Preview, 1600);
            var control = Clipping(controlImage);
            var approved = new FinishingClip(pin.Pixels, pin.Black, pin.White);
            var tolerance = pin.Name == "fujifilm-x30.raf" ? .1 : 0;
            Assert.Equal(pin.Pixels, control.Pixels);
            Assert.InRange(control.BlackPercent, approved.BlackPercent - tolerance, approved.BlackPercent + tolerance);
            Assert.InRange(control.WhitePercent, approved.WhitePercent - tolerance, approved.WhitePercent + tolerance);

            foreach (var candidate in candidates)
            {
                using var image = Render(pair.Interactive, candidate.Settings, RenderIntent.Preview, 1600);
                var actual = Clipping(image);
                Assert.Equal(control.Pixels, actual.Pixels);
                var blackLimit = control.BlackPercent + 1;
                var whiteLimit = control.WhitePercent + 1;
                var pass = actual.BlackPercent <= blackLimit && actual.WhitePercent <= whiteLimit;
                allPass &= pass;
                Report("G4", candidate, pin.Name, new
                {
                    control, approved, tolerance, actual, blackLimit, whiteLimit, pass,
                    width = image.Width, height = image.Height, scale = 65535, maxDimension = 1600
                });
            }
        }

        Assert.True(allPass, "G4 clipping miss; retune or drop failed candidates before owner review");
    }
}
