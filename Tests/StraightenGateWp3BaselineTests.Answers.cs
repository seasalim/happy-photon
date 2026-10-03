using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class StraightenGateWp3BaselineTests
{
    private void VerifyAnswer(string gate, string name, MagickImage frame, double tilt, double? target,
        HorizonDetection.Result result, HorizonDetection.Diagnostics diagnostics,
        StraightenGateWp3LinePredicate.Evaluation evaluation)
    {
        Assert.InRange(result.HorizonRotation, -5, 5);

        // This owner exception gates best effort regardless of this decode's winning tier.
        if (gate == "G3" && name == BestEffortPhoto)
        {
            Assert.True(Math.Abs(result.HorizonRotation - target!.Value) <= Math.Abs(target.Value) + NoHarmMargin);

            return;
        }

        Assert.Equal(result.Confidence, diagnostics.Confidence);
        var skyline = StraightenGateWp3SkylineOracle.Evaluate(frame);
        Print("WP3-skyline-evidence", new { gate, name, tilt, skyline.Share, skyline.Span, skyline.ContentAngle });
        var expected = evaluation.FallbackEligible
            ? skyline.Share >= .30 && skyline.Span >= .5 ? HorizonDetection.Tier.Skyline : HorizonDetection.Tier.Orientation
            : HorizonDetection.Tier.Lines;
        // A constant plane has no transition; the oracle's row-one placeholders are not evidence.
        if (name == "flat") expected = HorizonDetection.Tier.Orientation;

        Assert.Equal(expected, diagnostics.Tier);

        if (!evaluation.FallbackEligible)
        {
            Assert.Equal(evaluation.PredictedRotation!.Value, result.HorizonRotation, 10);
        }

        if (gate == "G4")
        {
            Assert.True(evaluation.FallbackEligible);
            Assert.InRange(result.HorizonRotation, -1, 1);
        }

        if (gate == "G5-membership-only")
        {
            Assert.Equal(name == "skyline" ? HorizonDetection.Tier.Skyline :
                name == "nikon-d300-colorchecker.nef" ? HorizonDetection.Tier.Lines :
                HorizonDetection.Tier.Orientation, diagnostics.Tier);
        }

        if (!target.HasValue) return;

        var error = Math.Abs(result.HorizonRotation - target.Value);

        if (gate is "G3" or "G3v")
        {
            Assert.Equal(HorizonDetection.Tier.Lines, diagnostics.Tier);
            Assert.True(error <= .35, $"{name}/{tilt}: {error}");
        }

        if (gate != "G3s") return;

        if (name == "skyline")
        {
            Assert.Equal(HorizonDetection.Tier.Skyline, diagnostics.Tier);
            Assert.True(error <= .35, $"Skyline/{tilt}: {error}");

            return;
        }

        // Baseline-pinned membership for every retained D-8 arm, including the D-9 relabel.
        var lineStrong = name is "DSCF2263.RAF" or "DSCF8495.JPG";
        Assert.Equal(!lineStrong, evaluation.FallbackEligible);

        // Owner ruling 2026-10-02: precision arms gate at 0.5 degrees only; no-harm applies to the rest.
        if (diagnostics.Tier is HorizonDetection.Tier.Lines or HorizonDetection.Tier.Skyline)
        {
            Assert.True(error <= .5, $"Precision bound: {name}/{tilt}, {error}");

            return;
        }

        Assert.True(error <= Math.Abs(target.Value) + NoHarmMargin, $"No-harm bound: {name}/{tilt}, {error}");
    }
}
