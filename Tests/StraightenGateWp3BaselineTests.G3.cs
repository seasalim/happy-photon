using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class StraightenGateWp3BaselineTests
{
    public static IEnumerable<object[]> PublicG3Photos() =>
        HorizonDetectionFixtureTests.PublicLabels().Select(row => row.Take(2).ToArray());

    public static IEnumerable<object[]> OwnerG3Photos() =>
        HorizonDetectionFixtureTests.OwnerLabels().Select(row => row.Take(2).ToArray());

    [Theory]
    [MemberData(nameof(PublicG3Photos))]
    public void PublicG3Membership(string name, double label)
    {
        using var pair = StraightenGateFixtures.Load(name);

        MeasureG3Membership(name, pair.Interactive.Pixels, label);
    }

    [Theory]
    [MemberData(nameof(OwnerG3Photos))]
    public void OwnerG3Membership(string name, double label)
    {
        using var pair = StraightenGateOwnerFixtures.Load(name);

        MeasureG3Membership(name, pair.Interactive.Pixels, label);
    }

    // Owner rulings 2026-10-02: X-Trans half-size decodes move this RAW's rival ratios across
    // the 0.9 line between processes, so every arm gates best effort; DSCF0076.JPG stays strict.
    internal const string BestEffortPhoto = "DSCF0076.RAF";

    // Owner ruling 2026-10-02: fallback answers may exceed the untouched error by 0.75 degrees,
    // covering the orientation tier's near-level answers (up to 0.6 degrees) plus a bin of headroom.
    internal const double NoHarmMargin = .75;

    private void MeasureG3Membership(string name, MagickImage basis, double label)
    {
        var evaluations = new List<StraightenGateWp3LinePredicate.Evaluation>();
        var errors = new List<double>();

        foreach (var tilt in new double[] { -3, -1.5, -.5, 0, .5, 1.5, 3 })
        {
            if (Math.Abs(label - tilt) > 4.5) continue;

            using var frame = StraightenGateScenes.Tilt(basis, tilt);
            var evaluation = Measure("G3", name, frame, tilt, label - tilt);

            if (name == BestEffortPhoto) continue;

            evaluations.Add(evaluation);

            if (evaluation.PredictedRotation.HasValue)
            {
                errors.Add(Math.Abs(evaluation.PredictedRotation.Value - (label - tilt)));
            }
        }

        if (name == BestEffortPhoto) return;

        var weakest = evaluations.Min(e => e.AnswerInlierLength);
        var median = errors.Count == 0 ? (double?)null : FinishingGateSupport.Median(errors);
        var max = errors.Count == 0 ? (double?)null : errors.Max();
        var verticalWins = evaluations.All(e => e.AnswerFamily == "vertical");
        var valid = evaluations.All(e => !e.FallbackEligible) && weakest >= 500 &&
            max <= .35 && (name != "IMG_5569.JPG" || verticalWins);
        Print("WP3-G3-summary", new { name, label, arms = evaluations.Count,
            lineStrong = errors.Count, weakestInlierLength = weakest, predictedMedianError = median,
            predictedMaxError = max, verticalWins, valid });
        Assert.True(valid, "Frozen G3 baseline outside the envelope; owner review required");
    }
}
