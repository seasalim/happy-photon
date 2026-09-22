using HappyPhoton.Services;
using Xunit;
using static HappyPhoton.Tests.LocalsRangeOracle;

namespace HappyPhoton.Tests;

public sealed partial class LocalsContractPrototypeTests
{
    [Fact]
    public void RangeOracle_LightnessOpenEndsEmptyPeakAndBounds()
    {
        Assert.Equal((0d, 1d, .1), (LightWindow.Default.Lower, LightWindow.Default.Upper, LightWindow.Default.Softness));
        foreach (var l in new[] { -100d, -1, 0, .25, .5, 1, 2, 100 })
            Assert.Equal(1, LightWindow.Default.Weight(l));
        Assert.Equal(1, new LightWindow(0, 0, 0).Weight(-1));
        Assert.Equal(1, new LightWindow(1, 1, 0).Weight(2));
        foreach (var l in new[] { 0d, .25, .5, .75, 1 })
            Assert.Equal(0, new LightWindow(.5, .5, 0).Weight(l));
        var peak = new LightWindow(.5, .5, .1);
        Assert.Equal(1, peak.Weight(.5));
        Assert.Equal(.5, peak.Weight(.45), 12);
        Assert.Equal(.5, peak.Weight(.55), 12);
        Assert.Equal(0, peak.Weight(.3));
        Assert.Equal(.5, new LightWindow(.3, .6, .2).Weight(.2), 12);
        Assert.Equal(.5, new LightWindow(.3, .6, .2).Weight(.7), 12);
        Assert.Throws<ArgumentOutOfRangeException>(() => new LightWindow(.6, .3, .1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LightWindow(-.1, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LightWindow(0, 1.1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LightWindow(0, 1, .5001));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LightWindow(0, 1, -.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LightWindow(double.NaN, 1, 0));
    }

    [Fact]
    public void RangeOracle_HueWrapFullCircleReliabilityAndBounds()
    {
        Assert.Equal((240d, 60d, 30d), (HueWindow.Default.Center, HueWindow.Default.Width, HueWindow.Default.Softness));
        var window = new HueWindow(0, 60, 30);
        Assert.Equal(.5, window.Weight(45), 12);
        Assert.Equal(window.Weight(359), window.Weight(1), 12);
        Assert.Equal(window.Weight(-1), window.Weight(359));
        Assert.Equal(window.Weight(0), window.Weight(360));
        Assert.Equal(0, new HueWindow(0, 0, 0).Weight(0));
        Assert.Equal(1, new HueWindow(0, 0, 30).Weight(0));
        Assert.Equal(.5, new HueWindow(0, 0, 30).Weight(15), 12);
        Assert.InRange(new HueWindow(0, 300, 90).Weight(180), 0.001, .999);
        foreach (var h in new[] { -360d, 0, 120, 240, 360, 720 })
        {
            Assert.Equal(1, new HueWindow(0, 360, 0).Weight(h));
            Assert.Equal(0, new HueWindow(0, 360, 0).Weight(h) * Reliability(0));
        }
        Assert.Equal(0, Reliability(.01));
        Assert.Equal(.5, Reliability(.025), 12);
        Assert.Equal(1, Reliability(.04));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HueWindow(360, 60, 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HueWindow(-1, 60, 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HueWindow(0, 361, 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HueWindow(0, -1, 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HueWindow(0, 60, 91));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HueWindow(0, 60, -1));
    }

    [Fact]
    public void RangeOracle_BoundaryAdjacentCorpusAndSoftnessMonotonicity()
    {
        var count = 0;
        foreach (var softness in new[] { 0d, 1e-6, 1e-3 })
        {
            var light = new LightWindow(.25, .75, softness);
            var hue = new HueWindow(0, 60, softness);
            foreach (var endpoint in new[] { .25 - softness, .25, .75, .75 + softness })
            foreach (var l in Adjacent(endpoint))
            {
                var actual = light.Weight(l);
                if (softness == 0) Assert.Equal(l >= .25 && l <= .75 ? 1 : 0, actual);
                Assert.InRange(actual, 0, 1);
                Assert.True(new LightWindow(.25, .75, .5).Weight(l) >= actual);
                count++;
            }
            foreach (var endpoint in new[] { 0d, 30, 30 + softness, 330 - softness, 330, 360 })
            foreach (var h in Adjacent(endpoint))
            {
                Assert.InRange(hue.Weight(h), 0, 1);
                if (softness == 0 && h >= 0 && h <= 360)
                    Assert.Equal(h <= 30 || h >= 330 ? 1 : 0, hue.Weight(h));
                Assert.True(new HueWindow(0, 60, 90).Weight(h) >= hue.Weight(h));
                Assert.InRange(Math.Abs(hue.Weight(h) - hue.Weight(-h)), 0, 1e-7);
                count++;
            }
        }
        output.WriteLine($"Double oracle: {count} boundary-adjacent window samples; guarded candidate fidelity is covered by RangeFast.");
    }

    private static IEnumerable<double> Adjacent(double value)
    {
        yield return value;
        yield return Math.BitDecrement(value); yield return Math.BitIncrement(value);
        foreach (var e in new[] { 1e-12, 1e-9, 1e-6, 1e-3 })
        { yield return value - e; yield return value + e; }
    }

    [Fact]
    public void RangeOracle_PickerCircularVectorFootprintAndRejections()
    {
        static Lab Color(double h, double c = .1) => new(.5, c * Math.Cos(h * Math.PI / 180), c * Math.Sin(h * Math.PI / 180));
        Assert.Equal("no matching base", Pick(10, 10, 5, 5, null).Rejection);
        Assert.Equal("off-image", Pick(10, 10, 10, 5, _ => Color(0)).Rejection);
        Assert.Equal("too neutral", Pick(10, 10, 5, 5, _ => Color(0, .001)).Rejection);
        var singleton = Pick(10, 10, 5.1, 5.1, _ => Color(240));
        Assert.Equal(1, singleton.Count); Assert.Equal(240, singleton.Hue!.Value, 10);
        var wrap = Pick(1600, 1000, 800, 500, p => Color(p % 1600 < 800 ? 359 : 1));
        Assert.Null(wrap.Rejection); Assert.InRange(Distance(wrap.Hue!.Value, 0), 0, 1e-10);
        var expected = 0;
        for (var y = 493; y <= 506; y++) for (var x = 793; x <= 806; x++)
            if (Math.Pow(x + .5 - 800, 2) + Math.Pow(y + .5 - 500, 2) <= 6.4 * 6.4) expected++;
        Assert.Equal(expected, wrap.Count);
        var mixed = Pick(1600, 1000, 800, 500, p => Color(p % 1600 < 800 ? 60 : 300, .2));
        Assert.Equal("mixed colors", mixed.Rejection); Assert.Null(mixed.Hue);
        // Weight by chroma: this is the direction of mean(a,b), not mean angle.
        var unequal = Pick(1600, 1000, 800, 500, p => p % 1600 < 800 ? Color(0, .2) : Color(30, .1));
        Assert.InRange(unequal.Hue!.Value, 9, 11);
        var old = HueWindow.Default;
        var retained = mixed.Hue is { } picked ? new HueWindow(picked, old.Width, old.Softness) : old;
        Assert.Equal(old, retained);
    }

    [Fact]
    public void RangeOracle_ClassificationAnchorsAndExtendedValues()
    {
        foreach (var vector in ColorScienceOracleData.Load().Oklab.RgbVectors)
        {
            var v = vector.LinearRec2020;
            var lab = Classify(v[0], v[1], v[2]);
            Assert.InRange(Math.Abs(lab.L - vector.Oklch[0]), 0, 1e-12);
            Assert.InRange(Math.Abs(lab.C - vector.Oklch[1]), 0, 1e-12);
        }
        var a = Classify(.5, .2, .1); var b = Classify(4, 1.6, .8);
        Assert.Equal(a.L * 2, b.L, 12); Assert.Equal(a.C * 2, b.C, 12);
        Assert.True(Classify(-1, -1, -1).L < 0);
        Assert.True(Classify(8, 8, 8).L > 1);
        Assert.True(double.IsFinite(Classify(-2, .1, 3).Hue));
    }

    [Fact]
    public void RangeOracle_ReliabilityEnvelopeUsesBothPersistentCrossings()
    {
        var clean = LocalsFusedBaselineTests.RangeCrossings([60, 50, 30, 12, 10]);
        Assert.Equal(.004, clean.Start); Assert.Equal(.006, clean.End);
        var rebound = LocalsFusedBaselineTests.RangeCrossings([60, 50, 12, 25, 10]);
        Assert.Equal(.004, rebound.Start); Assert.Equal(.008, rebound.End);
        Assert.Null(LocalsFusedBaselineTests.RangeCrossings([40, 30, 10]).Start);
        Assert.Null(LocalsFusedBaselineTests.RangeCrossings([60, 50, 30]).End);
        var gap = LocalsFusedBaselineTests.RangeCrossings([60, double.NaN, 12]);
        Assert.Equal(.002, gap.Start); Assert.Equal(.004, gap.End);
    }

    [Fact]
    public void RangeOracle_PickerThresholdsAndTermComposition()
    {
        Assert.Equal("too neutral", Pick(1, 1, .5, .5, _ => new(.5, .025 - 1e-9, 0)).Rejection);
        Assert.Null(Pick(1, 1, .5, .5, _ => new(.5, .025 + 1e-9, 0)).Rejection);
        foreach (var coherence in new[] { .75 - 1e-6, .75 + 1e-6 })
        {
            var pick = Pick(1600, 1000, 800, 500, p =>
                new(.5, .1 * coherence, (p % 1600 < 800 ? 1 : -1) * .1 * Math.Sqrt(1 - coherence * coherence)));
            Assert.Equal(coherence < .75 ? "mixed colors" : null, pick.Rejection);
        }
        var lab = new Lab(.2, .025, 0);
        var light = new LightWindow(.3, .6, .2);
        var hue = new HueWindow(0, 360, 0);
        double Weight(bool l, bool h, bool mono) =>
            (l ? light.Weight(lab.L) : 1) * (h && !mono ? hue.Weight(lab.Hue) * Reliability(lab.C) : 1);
        Assert.Equal(1, Weight(false, false, false));
        Assert.Equal(.25, Weight(true, true, false), 12);
        Assert.Equal(.5, Weight(true, true, true), 12);
        Assert.Equal(1, Weight(false, true, true));
    }}
