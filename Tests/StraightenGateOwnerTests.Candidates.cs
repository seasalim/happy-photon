using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class StraightenGateOwnerTests
{
    [Theory]
    [MemberData(nameof(Photos))]
    public void CandidateDiagnostics(string name)
    {
        // Resolve the opt-in before loading anything; no report-only photos or image outputs.
        StraightenGateOwnerFixtures.PathFor(name);
        var regions = Regions(name);
        Assert.SkipUnless(regions.Length > 0, "No labelled regions");
        var label = (double)HorizonDetectionFixtureTests.OwnerLabels().Single(row => (string)row[0] == name)[1];
        using var pair = StraightenGateOwnerFixtures.Load(name);
        var basis = pair.Interactive.Pixels;

        foreach (var tilt in new double[] { -3, -1.5, -.5, 0, .5, 1.5, 3 })
        {
            if (Math.Abs(label - tilt) > 4.5) continue;

            using var frame = StraightenGateScenes.Tilt(basis, tilt);
            var map = new RenderGeometryMap((int)basis.Width, (int)basis.Height, tilt, null);
            var candidates = new List<HorizonDetection.CandidateDiagnostics>();
            var result = HorizonDetection.Detect(frame, out var diagnostics, candidates.Add);
            Print("candidate-stages", new { name, tilt, diagnostics, result });

            for (var regionIndex = 0; regionIndex < regions.Length; regionIndex++)
            {
                var region = regions[regionIndex];
                var start = map.MapForward(region.Vertical ? region.AcrossStart : region.Start,
                    region.Vertical ? region.Start : region.AcrossStart);
                var end = map.MapForward(region.Vertical ? region.AcrossEnd : region.End,
                    region.Vertical ? region.End : region.AcrossEnd);
                var matches = candidates.Where(c => c.Vertical == region.Vertical &&
                    (InCorridor(c.Theta, c.Rho, start, end, region.Radius) ||
                        c.FittedTheta is { } theta && c.FittedRho is { } rho &&
                        InCorridor(theta, rho, start, end, region.Radius))).ToArray();
                Print("candidate-corridor", new { name, tilt, regionIndex, start, end, region.Radius,
                    count = matches.Length });

                foreach (var candidate in matches)
                {
                    Print("candidate", new { name, tilt, regionIndex, candidate });
                }
            }
        }
    }

    private static bool InCorridor(double theta, double rho, GeometryPoint start, GeometryPoint end, double radius)
    {
        var radians = theta * Math.PI / 180;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);

        return Math.Abs(start.X * cosine + start.Y * sine - rho) <= radius &&
            Math.Abs(end.X * cosine + end.Y * sine - rho) <= radius;
    }
}
