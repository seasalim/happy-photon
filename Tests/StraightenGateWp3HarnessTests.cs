using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class StraightenGateWp3HarnessTests
{
    [Fact]
    public void SharedLinePredicateKeepsFrozenBaselineConstants()
    {
        Assert.Equal(200, HorizonDetection.MinimumBaseSupport);
        Assert.Equal(450, HorizonDetection.AnswerFloor);
        Assert.Equal(.2, HorizonDetection.MinimumRelativeEvidence);
        Assert.Equal(.2, HorizonDetection.MaximumInlierSpread);
        Assert.Equal(.9, HorizonDetection.MaximumCompetitorRatio);
        Assert.Equal(.25, HorizonDetection.AgreementTolerance);
        Assert.Equal(.3, HorizonDetection.RivalGap);
    }

    [Fact]
    public void SoftRivalBoundaryIsStrictAndIndependentOfMergedConfidence()
    {
        var family = new HorizonDetection.FamilyDiagnostics(2, 1, 1, 2, 1000, 500, 1, .2, 450);
        var diagnostics = new HorizonDetection.Diagnostics(0, 0, 0, 0, 0, 0, 0,
            family, default, 0, 0, default);

        Assert.True(StraightenGateWp3LinePredicate.Evaluate(diagnostics).FallbackEligible);

        var below = diagnostics with { Horizontal = family with { CompetitorLength = 449.99 } };
        var evaluation = StraightenGateWp3LinePredicate.Evaluate(below);
        Assert.False(evaluation.FallbackEligible);
        Assert.Equal(-1, evaluation.PredictedRotation);

        var spreadFails = below with { Horizontal = below.Horizontal with { InlierSpread = .20001 } };
        Assert.True(StraightenGateWp3LinePredicate.Evaluate(spreadFails).FallbackEligible);
    }

    [Fact]
    public void DisagreeingFamiliesUseRivalGapThenEvidenceStrength()
    {
        var h = new HorizonDetection.FamilyDiagnostics(2, 1, 1, 0, 800, 800, 1, 0, 400);
        var v = new HorizonDetection.FamilyDiagnostics(2, 2, 2, 0, 500, 500, 1, 0, 0);
        var diagnostics = new HorizonDetection.Diagnostics(0, 0, 0, 0, 0, 0, 0, h, v, 0, 0, default);

        Assert.Equal(-2, StraightenGateWp3LinePredicate.Evaluate(diagnostics).PredictedRotation);

        var similar = diagnostics with { Vertical = v with { CompetitorLength = 150 } };
        Assert.Equal(-1, StraightenGateWp3LinePredicate.Evaluate(similar).PredictedRotation);
    }

    [Theory]
    [InlineData(199.99, false, false)]
    [InlineData(200, true, false)]
    [InlineData(449.99, true, false)]
    [InlineData(450, true, true)]
    public void CandidateAndAnswerFloorsRemainDistinct(double length, bool candidate, bool answers)
    {
        var family = new HorizonDetection.FamilyDiagnostics(1, 1, 1, 0, length, length, 1, 0, 0);
        var diagnostics = new HorizonDetection.Diagnostics(0, 0, 0, 0, 0, 0, 0,
            family, default, 0, 0, default);

        var evaluation = StraightenGateWp3LinePredicate.Evaluate(diagnostics);
        Assert.Equal(candidate, evaluation.HorizontalCandidate);
        Assert.Equal(answers, evaluation.Horizontal);
        Assert.Equal(!answers, evaluation.FallbackEligible);
    }

    [Fact]
    public void OwnerLabelsReadBasenamesIncludingSpacesAndSignedValues()
    {
        var labels = StraightenGateOwnerFixtures.ParseLandscapeLabels(
            ["folder with spaces/DSCF0423.JPG +1.8", "DSCF7825.JPG -0.9", "DSCF2263.RAF +1.8"]);
        Assert.Equal(1.8, labels["DSCF0423.JPG"]);
        Assert.Equal(-.9, labels["DSCF7825.JPG"]);
        Assert.Equal(1.8, labels["DSCF2263.RAF"]);
        Assert.Equal(3, labels.Count);
    }

    [Fact]
    public void ApprovedOwnerLabelsMatchThePinnedMap()
    {
        var labels = StraightenGateOwnerFixtures.ParseLandscapeLabels(
        [
            "DSCF0423.JPG +1.8", "DSCF2263.RAF +1.8", "DSCF6915.JPG +4.2",
            "DSCF7257.RAF +1.8", "DSCF7806.JPG +2.1", "DSCF7825.JPG -0.9",
            "DSCF8355.JPG -1.5", "DSCF8477.JPG -1.0", "DSCF8495.JPG -1.6"
        ]);

        StraightenGateOwnerFixtures.VerifyLandscapeLabels(labels);
        Assert.Equal(StraightenGateOwnerFixtures.LandscapeNames.Order(), labels.Keys.Order());
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("renamed")]
    [InlineData("empty")]
    public void ChangedOwnerLabelsCannotAlterTheWorkload(string change)
    {
        var labels = new Dictionary<string, double>(StraightenGateOwnerFixtures.LandscapeLabels);

        switch (change)
        {
            case "changed":
                labels["DSCF2263.RAF"] = 1.1;
                break;

            case "missing":
                labels.Remove("DSCF6915.JPG");
                break;

            case "extra":
                labels.Add("extra.JPG", 0);
                break;

            case "renamed":
                labels.Remove("DSCF6915.JPG");
                labels.Add("renamed.JPG", 4.2);
                break;

            case "empty":
                labels.Clear();
                break;
        }

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => StraightenGateOwnerFixtures.VerifyLandscapeLabels(labels));
    }
}
