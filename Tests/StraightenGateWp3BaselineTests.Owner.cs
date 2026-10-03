using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class StraightenGateWp3BaselineTests
{
    [Fact]
    public void OwnerLandscapeMetadata()
    {
        StraightenGateOwnerFixtures.ReadLandscapeLabels();

        foreach (var name in StraightenGateOwnerFixtures.LandscapeNames.Concat(
            StraightenGateOwnerFixtures.LandscapeReportOnly))
        {
            Print("WP3-owner-metadata", StraightenGateOwnerFixtures.LandscapeMetadata(name));
        }
    }

    [Theory]
    [InlineData("DSCF0423.JPG", 4)]
    [InlineData("DSCF2263.RAF", 4)]
    [InlineData("DSCF6915.JPG", 3)]
    [InlineData("DSCF7257.RAF", 4)]
    [InlineData("DSCF7806.JPG", 4)]
    [InlineData("DSCF7825.JPG", 5)]
    [InlineData("DSCF8355.JPG", 5)]
    [InlineData("DSCF8477.JPG", 5)]
    [InlineData("DSCF8495.JPG", 4)]
    public void OwnerLandscapeMembership(string name, int expectedArms)
    {
        var labels = StraightenGateOwnerFixtures.ReadLandscapeLabels();

        MeasureOwnerLandscape(name, labels[name], expectedArms);
    }

    private void MeasureOwnerLandscape(string name, double label, int expectedArms)
    {
        using var pair = StraightenGateOwnerFixtures.LoadLandscape(name);
        var arms = 0;
        var fallback = 0;
        var errors = new List<double>();

        foreach (var tilt in new double[] { -3, -1.5, 0, 1.5, 3 })
        {
            if (Math.Abs(label - tilt) > 4.5) continue;

            using var frame = StraightenGateScenes.Tilt(pair.Interactive.Pixels, tilt);
            var evaluation = Measure("G3s", name, frame, tilt, label - tilt);
            arms++;
            if (evaluation.FallbackEligible) fallback++;

            if (evaluation.PredictedRotation.HasValue)
            {
                errors.Add(Math.Abs(evaluation.PredictedRotation.Value - (label - tilt)));
            }
        }

        var max = errors.Count == 0 ? (double?)null : errors.Max();
        Print("WP3-owner-membership-summary", new { name, label, arms, fallback,
            everyArmFallbackEligible = fallback == arms, predictedMaxError = max });

        Assert.True(arms > 0);
        Assert.Equal(expectedArms, arms);
    }

    [Theory]
    [InlineData("DSCF0129.RAF")]
    [InlineData("IMG_4952.JPG")]
    [InlineData("IMG_6854.HEIC")]
    public void OwnerJudgement(string name)
    {
        var label = StraightenGateOwnerFixtures.JudgementLabels[name];
        using var pair = StraightenGateOwnerFixtures.Load(name);

        var evaluation = Measure("G3v", name, pair.Interactive.Pixels, 0, label);
        Print("WP3-G3v-support", new { name, evaluation.AnswerInlierLength });
        Assert.True(evaluation.AnswerInlierLength >= StraightenGateWp3LinePredicate.AnswerFloor);
    }

    [Fact]
    public void OwnerNegativeMembership()
    {
        using var pair = StraightenGateOwnerFixtures.Load("DSCF8369.JPG");

        Measure("G4", "DSCF8369.JPG", pair.Interactive.Pixels, 0, null);
    }

    [Fact]
    public void OwnerReportSweep()
    {
        StraightenGateOwnerFixtures.OwnerFolder();
        var excluded = StraightenGateOwnerFixtures.LandscapeNames.ToHashSet();
        var names = StraightenGateOwnerFixtures.Manifest.Keys.Where(name => !excluded.Contains(name))
            .Concat(StraightenGateOwnerFixtures.LandscapeReportOnly).Distinct();

        foreach (var name in names)
        {
            using var pair = StraightenGateOwnerFixtures.LoadLandscape(name);
            Measure("report-only", name, pair.Interactive.Pixels, 0, null);
        }
    }
}
