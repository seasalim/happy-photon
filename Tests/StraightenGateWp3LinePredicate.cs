using HappyPhoton.Services;

namespace HappyPhoton.Tests;

// Frozen baseline family-choice evaluation; qualification is shared with production.
internal static class StraightenGateWp3LinePredicate
{
    internal const double MinimumSupport = HorizonDetection.MinimumBaseSupport;

    internal const double AnswerFloor = HorizonDetection.AnswerFloor;

    internal const double RelativeEvidence = HorizonDetection.MinimumRelativeEvidence;

    internal const double MaximumSpread = HorizonDetection.MaximumInlierSpread;

    internal const double MaximumRival = HorizonDetection.MaximumCompetitorRatio;

    internal const double Agreement = HorizonDetection.AgreementTolerance;

    internal const double RivalGap = HorizonDetection.RivalGap;

    internal sealed record Evaluation(double EvidenceFloor, bool HorizontalCandidate, bool VerticalCandidate,
        bool Horizontal, bool Vertical, double? HorizontalRival, double? VerticalRival,
        bool FallbackEligible, string Classification, string? AnswerFamily,
        double AnswerInlierLength, double? PredictedRotation);

    internal static Evaluation Evaluate(HorizonDetection.Diagnostics diagnostics)
    {
        var horizontal = diagnostics.Horizontal;
        var vertical = diagnostics.Vertical;
        var floor = Math.Max(MinimumSupport,
            RelativeEvidence * Math.Max(horizontal.InlierLength, vertical.InlierLength));
        var hc = Qualifies(horizontal, floor);
        var vc = Qualifies(vertical, floor);
        var h = hc && horizontal.InlierLength >= AnswerFloor;
        var v = vc && vertical.InlierLength >= AnswerFloor;
        var hr = Rival(horizontal);
        var vr = Rival(vertical);
        double? rotation = null;
        string? answerFamily = null;
        var answerLength = 0d;

        if (h || v)
        {
            var selected = h && !v ? horizontal : v && !h ? vertical :
                horizontal.InlierLength >= vertical.InlierLength ? horizontal : vertical;
            var tilt = selected.Mean;
            answerFamily = selected == horizontal ? "horizontal" : "vertical";
            answerLength = selected.InlierLength;

            if (h && v)
            {
                if (Math.Abs(horizontal.Mean - vertical.Mean) <= Agreement)
                {
                    tilt = (horizontal.Mean * horizontal.InlierLength + vertical.Mean * vertical.InlierLength) /
                        (horizontal.InlierLength + vertical.InlierLength);
                    answerFamily = "both";
                }
                else if (Math.Abs(hr!.Value - vr!.Value) >= RivalGap)
                {
                    tilt = hr < vr ? horizontal.Mean : vertical.Mean;
                    answerFamily = hr < vr ? "horizontal" : "vertical";
                    answerLength = hr < vr ? horizontal.InlierLength : vertical.InlierLength;
                }
            }

            rotation = Math.Clamp(-tilt, -5, 5);
        }

        return new(floor, hc, vc, h, v, hr, vr, !h && !v,
            h || v ? "line-strong" : "fallback", answerFamily, answerLength, rotation);
    }

    private static bool Qualifies(HorizonDetection.FamilyDiagnostics family, double floor) =>
        HorizonDetection.Qualifies(family, floor);

    private static double? Rival(HorizonDetection.FamilyDiagnostics family) =>
        family.InlierLength > 0 ? family.CompetitorLength / family.InlierLength : null;
}
