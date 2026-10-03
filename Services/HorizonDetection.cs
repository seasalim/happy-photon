using ImageMagick;

namespace HappyPhoton.Services;

public static partial class HorizonDetection
{
    private const int LongEdge = 1024;

    private const int Border = 16;

    private const double Window = 6.65;

    private const double BinWidth = .1;

    private const double RotationLimit = 5;

    private const double DerivativeSigma = 1.5;

    private const int DerivativeRadius = 5;

    private const double FamilyTolerance = .35;

    internal const double AgreementTolerance = .25;

    // A family with less than a fifth of the stronger cluster's evidence cannot
    // overturn it alone. The inlier share is a soft confidence term, not a veto.
    internal const double MaximumInlierSpread = .2;

    // Comparable rivals are ambiguous; weaker rivals reduce confidence continuously.
    internal const double MaximumCompetitorRatio = .9;

    // Frozen baseline separation between long structure and misleading short edges.
    internal const double AnswerFloor = 450;

    // A materially cleaner family wins disagreement; otherwise retain stronger evidence.
    internal const double RivalGap = .3;

    // All in-window lines, inliers or not, scatter the answer's support: a degree of
    // scatter (three times the inlier window) halves the confidence (cross-review C-1, C-2).
    private const double AllLineSpreadScale = 1;

    internal const double MinimumRelativeEvidence = .2;

    internal enum Tier { Lines, Skyline, Orientation }

    internal readonly record struct SkylineDiagnostics(double Share, double Span, double ContentAngle);

    public readonly record struct Result(double HorizonRotation, double Confidence);

    internal readonly record struct FamilyDiagnostics(int Lines, double Peak, double Mean,
        double Spread, double Length, double InlierLength, double EffectiveLines, double InlierSpread,
        double CompetitorLength);

    internal readonly record struct Diagnostics(double RawPeak, double Spread, double AxisDisagreement,
        double SupportingLength, double InlierShare, double LineCountTerm, double Confidence,
        FamilyDiagnostics Horizontal, FamilyDiagnostics Vertical, int NegativeLines, int PositiveLines,
        StageDiagnostics Stages, Tier Tier = Tier.Lines, SkylineDiagnostics Skyline = default);

    internal record struct StageDiagnostics
    {
        public int EdgePixels { get; set; }

        public int AccumulatorPeaks { get; set; }

        public int FittedCandidates { get; set; }

        public int RejectedStraightness { get; set; }

        public int RejectedFragmentation { get; set; }

        public int RejectedSupport { get; set; }

        public int RejectedOverlap { get; set; }

        public int RejectedWindow { get; set; }
    }

    public static Result Detect(MagickImage image) => Detect(image, out _);

    internal static Result Detect(MagickImage image, out Diagnostics diagnostics,
        Action<CandidateDiagnostics>? traceCandidate = null)
    {
        ArgumentNullException.ThrowIfNull(image);

        var scale = Math.Min(1, LongEdge / (double)Math.Max(image.Width, image.Height));
        var width = (int)Math.Round(image.Width * scale);
        var height = (int)Math.Round(image.Height * scale);
        diagnostics = default;

        if (width <= Border * 2 || height <= Border * 2)
        {
            diagnostics = diagnostics with { Tier = Tier.Orientation };

            return new(0, 0);
        }

        using var luminance = new Scratch<double>(width * height);
        using var gradientX = new Scratch<double>(width * height);
        using var gradientY = new Scratch<double>(width * height);
        using var gradientMagnitude = new Scratch<double>(width * height);
        var plane = luminance.Values;
        var gx = gradientX.Values;
        var gy = gradientY.Values;
        var magnitude = gradientMagnitude.Values;
        ReadLuminance(image, width, height, plane);
        Gradients(plane, width, height, gx, gy);
        var edges = Canny(gx, gy, width, height, magnitude);
        var stages = new StageDiagnostics { EdgePixels = edges.Count };
        var lines = FindLines(edges, width, height, scale, width / (double)image.Width,
            height / (double)image.Height, ref stages, traceCandidate);
        var horizontal = Summarize(lines, false);
        var vertical = Summarize(lines, true);
        var evidenceFloor = Math.Max(MinimumBaseSupport,
            MinimumRelativeEvidence * Math.Max(horizontal.InlierLength, vertical.InlierLength));
        var hAgrees = Qualifies(horizontal, evidenceFloor) && horizontal.InlierLength >= AnswerFloor;
        var vAgrees = Qualifies(vertical, evidenceFloor) && vertical.InlierLength >= AnswerFloor;
        var disagreement = horizontal.Lines > 0 && vertical.Lines > 0
            ? Math.Abs(horizontal.Mean - vertical.Mean) : 0;
        var stronger = horizontal.InlierLength >= vertical.InlierLength ? horizontal : vertical;
        var selected = hAgrees == vAgrees ? stronger : hAgrees ? horizontal : vertical;
        var combined = hAgrees && vAgrees && disagreement <= AgreementTolerance;

        if (hAgrees && vAgrees && !combined && Math.Abs(Rival(horizontal) - Rival(vertical)) >= RivalGap)
        {
            selected = Rival(horizontal) < Rival(vertical) ? horizontal : vertical;
        }

        var length = combined ? horizontal.InlierLength + vertical.InlierLength : selected.InlierLength;
        var allLength = horizontal.Length + vertical.Length;
        var share = allLength > 0 ? length / allLength : 0;
        var spread = allLength > 0 ? Math.Sqrt((horizontal.Length * horizontal.Spread * horizontal.Spread +
            vertical.Length * vertical.Spread * vertical.Spread) / allLength) : 0;
        var effective = combined ? horizontal.EffectiveLines + vertical.EffectiveLines : selected.EffectiveLines;
        var countTerm = .75 + .25 * (1 - Math.Exp(-effective));
        var inlierSpread = combined
            ? Math.Sqrt((horizontal.InlierLength * Math.Pow(horizontal.InlierSpread, 2) +
                vertical.InlierLength * Math.Pow(vertical.InlierSpread, 2)) / length)
            : selected.InlierSpread;
        // Supporting length is primary; repetition only provides a bounded soft benefit.
        // The denominator of share retains every line, including the converging family (C-1).
        // Off-angle minorities lower confidence without moving or vetoing a coherent cluster.
        var confidence = length / Math.Max(image.Width, image.Height) * share * countTerm /
            (1 + Math.Pow(inlierSpread / MaximumInlierSpread, 2)) / (1 + Math.Pow(spread / AllLineSpreadScale, 2));

        var rival = combined ? (horizontal.CompetitorLength + vertical.CompetitorLength) / length : Rival(selected);
        confidence *= 1 - rival;

        if (combined)
        {
            confidence /= 1 + Math.Pow(disagreement / AgreementTolerance, 2);
        }

        var tilt = combined ? (horizontal.Mean * horizontal.InlierLength +
            vertical.Mean * vertical.InlierLength) / length : selected.Mean;
        diagnostics = new(-tilt, spread, disagreement, length, share, countTerm, confidence,
            horizontal, vertical, lines.Count(l => l.Tilt < 0), lines.Count(l => l.Tilt > 0), stages);

        if (hAgrees || vAgrees)
        {
            return new(Math.Clamp(-tilt, -RotationLimit, RotationLimit), confidence);
        }

        var skyline = Skyline(plane, width, height, image.Width / (double)width, image.Height / (double)height);
        var tier = skyline.Share >= SkylineShare && skyline.Span >= SkylineSpan ? Tier.Skyline : Tier.Orientation;
        var fallback = tier == Tier.Skyline ? new Result(-skyline.ContentAngle, skyline.Share) :
            Orientation(gx, gy, magnitude, width, height, image.Width / (double)width, image.Height / (double)height);
        diagnostics = diagnostics with { Tier = tier, Skyline = skyline, Confidence = fallback.Confidence };

        return fallback with { HorizonRotation = Math.Clamp(fallback.HorizonRotation, -RotationLimit, RotationLimit) };
    }

    internal static bool Qualifies(FamilyDiagnostics family, double evidenceFloor) =>
        family.InlierLength >= evidenceFloor && family.InlierSpread <= MaximumInlierSpread &&
        family.CompetitorLength < MaximumCompetitorRatio * family.InlierLength;

    private static double Rival(FamilyDiagnostics family) =>
        family.InlierLength > 0 ? family.CompetitorLength / family.InlierLength : 1;

    internal static FamilyDiagnostics Summarize(IReadOnlyList<Line> lines, bool vertical)
    {
        var family = lines.Where(l => l.Vertical == vertical).ToArray();
        if (family.Length == 0) return default;

        var peak = Peak(family);
        var inliers = family.Where(l => Math.Abs(l.Tilt - peak) <= FamilyTolerance).ToArray();
        var others = family.Where(l => Math.Abs(l.Tilt - peak) > FamilyTolerance).ToArray();
        var competitor = 0d;

        // The rival is the coherent window holding the most length, not the sharpest peak.
        foreach (var centre in others)
        {
            competitor = Math.Max(competitor, others.Where(l => Math.Abs(l.Tilt - centre.Tilt) <= FamilyTolerance)
                .Sum(l => l.Weight));
        }

        var length = family.Sum(l => l.Weight);
        var inlierLength = inliers.Sum(l => l.Weight);
        var mean = inliers.Sum(l => l.Weight * l.Tilt) / inlierLength;
        var allMean = family.Sum(l => l.Weight * l.Tilt) / length;
        var spread = Math.Sqrt(family.Sum(l => l.Weight * Math.Pow(l.Tilt - allMean, 2)) / length);
        var effective = inlierLength * inlierLength / inliers.Sum(l => l.Weight * l.Weight);
        var inlierSpread = Math.Sqrt(inliers.Sum(l => l.Weight * Math.Pow(l.Tilt - mean, 2)) / inlierLength);

        return new(family.Length, peak, mean, spread, length, inlierLength, effective, inlierSpread, competitor);
    }

    private static double Peak(Line[] lines) => lines.MaxBy(l => lines.Sum(other => other.Weight *
        Math.Exp(-.5 * Math.Pow((other.Tilt - l.Tilt) / .15, 2))))!.Tilt;

    // Seed the upper 30% of nonzero gradients. A one-fifth-contrast stretch needs
    // margin for softening to remain connected to the strong ends of one edge.
    private const double HighPercentile = .70;

    private const double LowThresholdRatio = .15;

    internal const double MinimumBaseSupport = 200;

    // A two-base-pixel bow is 1.28 working pixels at a 1600px base. Include it plus
    // subpixel edge noise, rather than fitting only the centre of a quantized rho cell.
    private const double SupportDistance = 2;

    // The bowed-edge endpoint slope is about 1.15 degrees; retain room for the
    // noisy local normal without changing the global voting window.
    private const double OrientationTolerance = 3;

    // A 2px parabolic bow contributes about .38px RMS after reduction; combined
    // with .5px edge noise it needs .63px. The .75px bound retains a small margin.
    private const double MaximumResidual = .75;

    // Support is judged on one segment, including aligned occlusions. A 600px
    // edge with a missing middle 60% still supplies 240px, above the 200px minimum.
    // Allow five percentage points for losses near the gap's softened endpoints.
    private const double MinimumCoverage = .35;

    private const double MaximumGapFraction = .65;

    private readonly record struct Edge(double X, double Y, double Tilt, bool Vertical);

    internal sealed record Line(double Tilt, bool Vertical, double Weight);

    private static double Fold(double angle) => angle - 90 * Math.Floor((angle + 45) / 90);
}
