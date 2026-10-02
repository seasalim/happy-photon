using System.Text.Json;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class HorizonDetectionEdgeTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Arms() => StraightenGateScenes.Tilts.SelectMany(
        tilt => new[] { false, true }.Select(vertical => new object[] { tilt, vertical }));

    [Theory]
    [MemberData(nameof(Arms))]
    public void IsolatedEdgeSurvivesBothSigns(double tilt, bool vertical)
    {
        using var frame = CreateEdge(tilt, vertical, degraded: false);
        Check(frame, tilt, vertical, .05, "isolated-edge");
    }

    [Theory]
    [InlineData(-3, false)]
    [InlineData(3, false)]
    [InlineData(-3, true)]
    [InlineData(3, true)]
    public void SoftBowedGappedEdgeRemainsACandidate(double tilt, bool vertical)
    {
        using var frame = CreateEdge(tilt, vertical, degraded: true);
        Check(frame, tilt, vertical, .1, "soft-bowed-gapped-edge");
    }

    [Theory]
    [InlineData(-3, false)]
    [InlineData(3, false)]
    [InlineData(-3, true)]
    [InlineData(3, true)]
    public void SoftContrastChangesKeepOneLine(double tilt, bool flipPolarity)
    {
        using var frame = CreateEdge(tilt, false, degraded: false,
            pattern: flipPolarity ? "polarity" : "weak-middle");
        Check(frame, tilt, false, .1, flipPolarity ? "polarity" : "weak-middle");
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(3)]
    public void SoftEdgeWithMissingMiddleUsesBothEnds(double tilt)
    {
        using var frame = CreateEdge(tilt, false, degraded: false, pattern: "missing-middle");
        Check(frame, tilt, false, .1, "missing-middle");
    }

    [Fact]
    public void CandidateTracingPreservesDetectionAndBaseCoordinates()
    {
        using var frame = CreateEdge(3, false, degraded: false, pattern: "weak-middle");
        var candidates = new List<HorizonDetection.CandidateDiagnostics>();
        var plain = HorizonDetection.Detect(frame, out var plainDiagnostics);
        var traced = HorizonDetection.Detect(frame, out var tracedDiagnostics, candidates.Add);
        Assert.Equal(plain, traced);
        Assert.Equal(plainDiagnostics, tracedDiagnostics);
        var candidate = candidates.First(c => c.Rejection == HorizonDetection.CandidateRejection.None);
        var radians = candidate.FittedTheta!.Value * Math.PI / 180;
        var distance = Math.Abs(799.5 * Math.Cos(radians) + 533 * Math.Sin(radians) - candidate.FittedRho!.Value);
        Assert.True(distance <= 2, $"Fitted line must pass through the base edge centre: {distance}");

        foreach (var endpoint in new[] { candidate.SegmentStart, candidate.SegmentEnd })
        {
            var offset = endpoint.X * Math.Cos(radians) + endpoint.Y * Math.Sin(radians) - candidate.FittedRho.Value;
            Assert.InRange(Math.Abs(offset), 0, 1e-9);
        }
    }

    [Theory]
    [InlineData(-3, "separated-noisy")]
    [InlineData(3, "separated-noisy")]
    [InlineData(-3, "extended-noisy")]
    [InlineData(3, "extended-noisy")]
    public void LocalSegmentsExcludeUnrelatedSupport(double tilt, string pattern)
    {
        using var frame = CreateEdge(tilt, false, degraded: false, pattern);
        var candidates = new List<HorizonDetection.CandidateDiagnostics>();
        HorizonDetection.Detect(frame, out var diagnostics, candidates.Add);
        var accepted = candidates.Where(c => !c.Vertical &&
            c.Rejection == HorizonDetection.CandidateRejection.None &&
            Math.Abs(799.5 * Math.Cos(c.FittedTheta!.Value * Math.PI / 180) +
                533 * Math.Sin(c.FittedTheta.Value * Math.PI / 180) - c.FittedRho!.Value) < 5).ToArray();

        foreach (var candidate in accepted)
        {
            output.WriteLine("CANDIDATE " + JsonSerializer.Serialize(candidate));
        }

        Assert.Single(accepted);
        Assert.InRange(Math.Abs(diagnostics.Horizontal.Mean - tilt), 0, .1);
        Assert.InRange(Math.Abs(accepted[0].FittedTheta!.Value - 90 - tilt), 0, .1);
        Assert.InRange(accepted[0].Span, pattern == "separated-noisy" ? 960 : 560,
            pattern == "separated-noisy" ? 1040 : 640);
    }

    private void Check(MagickImage frame, double tilt, bool vertical, double tolerance, string scene)
    {
        var candidates = new List<HorizonDetection.CandidateDiagnostics>();
        var result = HorizonDetection.Detect(frame, out var diagnostics, candidates.Add);
        output.WriteLine("STRAIGHTEN " + JsonSerializer.Serialize(new { gate = "robustness",
            pid = Environment.ProcessId, values = new { scene, tilt, vertical, diagnostics, result } }));
        var accepted = candidates.Where(c => c.Rejection == HorizonDetection.CandidateRejection.None).ToArray();
        Assert.True(candidates.Count >= diagnostics.Stages.AccumulatorPeaks);
        Assert.Equal(diagnostics.Stages.FittedCandidates, accepted.Length);

        foreach (var candidate in accepted)
        {
            output.WriteLine("CANDIDATE " + JsonSerializer.Serialize(candidate));
        }

        var family = vertical ? diagnostics.Vertical : diagnostics.Horizontal;
        Assert.True(family.Lines > 0, "The isolated edge must supply a fitted candidate");
        if (scene == "isolated-edge") Assert.Equal(1, family.Lines);
        Assert.True(Math.Abs(family.Mean - tilt) <= tolerance,
            $"Expected {tilt}, fitted {family.Mean}");
        Assert.True(family.Spread <= tolerance, "The edge must not supply a second angle");

        if (scene is "weak-middle" or "polarity" or "missing-middle")
        {
            Assert.Contains(accepted, c => !c.Vertical && c.Span >= .8 * (scene == "polarity" ? 740 : 600));

            foreach (var candidate in accepted)
            {
                var normal = candidate.FittedTheta!.Value;
                var angle = normal - 90 * Math.Floor((normal + 45) / 90);
                Assert.True(Math.Abs(angle - tilt) <= .1, $"Unexpected candidate angle {angle}");
            }
        }
    }

    private static MagickImage CreateEdge(double tilt, bool vertical, bool degraded, string? pattern = null)
    {
        const int width = 1600;
        const int height = 1067;
        var radians = (tilt + (vertical ? 90 : 0)) * Math.PI / 180;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        var pixels = new ushort[width * height * 3];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sum = 0d;

                for (var sy = 0; sy < 4; sy++)
                {
                    for (var sx = 0; sx < 4; sx++)
                    {
                        var dx = x + (sx + .5) / 4 - width * .5;
                        var dy = y + (sy + .5) / 4 - height * .5;
                        var along = dx * cosine + dy * sine;
                        var across = -dx * sine + dy * cosine;
                        var bow = degraded ? 2 * (1 - along * along / (200 * 200)) : 0;
                        var gap = degraded && Math.Abs(Math.Abs(along) - 80) < 15;
                        var contrast = !degraded || Math.Abs(along) < 200 && !gap ? .25 : 0;

                        if (pattern is not null)
                        {
                            var halfLength = pattern == "polarity" ? 370 : 300;
                            contrast = Math.Abs(along) < halfLength ? .25 : 0;
                            if (pattern == "weak-middle" && Math.Abs(along) < 180) contrast *= .2;
                            if (pattern == "missing-middle" && Math.Abs(along) < 180) contrast = 0;
                            if (pattern == "polarity" && along > 0) contrast *= -1;

                            if (pattern is "separated-noisy" or "extended-noisy")
                            {
                                var onEdge = pattern == "separated-noisy"
                                    ? Math.Abs(along) is > 200 and < 500 : Math.Abs(along) < 300;
                                // Short offset streaks share the Hough band but are separated
                                // by 70px gaps and cannot establish a rigid structure.
                                var noise = !onEdge && Math.Abs(along % 80) < 5;
                                contrast = onEdge || noise ? .25 : 0;
                                if (noise) bow = 2.5 * Math.Sin(Math.Floor(along / 80) * 2);
                            }

                            // Fade away from the target edge so its endpoints and contrast
                            // transitions cannot create long perpendicular reference lines.
                            contrast *= Math.Exp(-across * across / (2 * 20 * 20));
                        }

                        sum += .4 + contrast *
                            (across < bow ? -1 : 1);
                    }
                }

                var value = (ushort)Math.Round(sum / 16 * ushort.MaxValue);
                var index = (y * width + x) * 3;
                pixels[index] = value;
                pixels[index + 1] = value;
                pixels[index + 2] = value;
            }
        }

        using var basis = RenderPipelineTestSupport.CreateBase(pixels, height: height);
        var frame = new MagickImage(basis.Pixels);
        if (degraded || pattern is not null) frame.GaussianBlur(0, 1.5);

        return frame;
    }
}
