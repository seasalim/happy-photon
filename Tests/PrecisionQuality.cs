namespace HappyPhoton.Tests;

internal static partial class PrecisionBoundaryCensus
{
    internal static PrecisionOutputQuality AnalyzeQuality(
        double[] actual,
        double[] reference,
        double[] sweep,
        int width,
        int height,
        bool[] clipped) =>
        AnalyzeQualityCore(
            actual, reference, width, height, clipped,
            pixel => IsSyntheticRampEligible(reference, sweep, width, pixel));

    internal static PrecisionOutputQuality AnalyzePhotographicQuality(
        double[] actual,
        double[] reference,
        int width,
        int height,
        bool[] clipped) =>
        AnalyzeQualityCore(
            actual, reference, width, height, clipped,
            pixel => IsOraclePresentUseful(reference, pixel));

    private static PrecisionOutputQuality AnalyzeQualityCore(
        double[] actual,
        double[] reference,
        int width,
        int height,
        bool[] clipped,
        Func<int, bool> isEligible)
    {
        var eligible = 0;
        var oracleEligible = 0;
        var clippedOracleEligible = 0;
        var countBelow = 0;
        var sum = 0d;
        var maximum = 0d;
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            if (!isEligible(pixel))
            {
                continue;
            }
            oracleEligible++;
            if (clipped[pixel])
            {
                clippedOracleEligible++;
                continue;
            }
            var offset = pixel * 3;
            var error = PrecisionDeltaE.FromSrgb(
                actual[offset], actual[offset + 1], actual[offset + 2],
                reference[offset], reference[offset + 1], reference[offset + 2]);
            eligible++;
            countBelow += error < 1.0 ? 1 : 0;
            sum += error;
            maximum = Math.Max(maximum, error);
        }
        if (eligible == 0)
        {
            var fullyClipped = oracleEligible > 0 &&
                    clippedOracleEligible == oracleEligible ||
                Enumerable.Range(0, width * height).All(pixel =>
                    clipped[pixel] || HasStorageEdge(actual, pixel));
            return fullyClipped
                ? PrecisionOutputQuality.FullyClipped(checked(width * height))
                : PrecisionOutputQuality.Unavailable(checked(width * height));
        }
        var stride = Math.Max(1, (int)Math.Ceiling(
            eligible / (double)MaximumRetainedRecords));
        var errors = new List<double>(Math.Min(eligible, MaximumRetainedRecords));
        var eligibleIndex = 0;
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            if (clipped[pixel] || !isEligible(pixel))
            {
                continue;
            }
            if (eligibleIndex % stride == 0)
            {
                var offset = pixel * 3;
                errors.Add(PrecisionDeltaE.FromSrgb(
                    actual[offset], actual[offset + 1], actual[offset + 2],
                    reference[offset], reference[offset + 1], reference[offset + 2]));
            }
            eligibleIndex++;
        }
        errors.Sort();
        var p99 = Math.Max(0, (int)Math.Ceiling(errors.Count * 0.99) - 1);
        var materialityRank = (int)Math.Ceiling(eligible * 0.99);
        return new PrecisionOutputQuality(
            true, checked(width * height), eligible,
            sum / eligible, errors[p99], maximum,
            countBelow, materialityRank, countBelow < materialityRank,
            PrecisionMetricBasis.ExactFullPopulation,
            stride == 1
                ? PrecisionMetricBasis.ExactFullPopulation
                : PrecisionMetricBasis.DescriptiveSystematicSample,
            errors.Count, stride);
    }

    private static bool HasStorageEdge(double[] actual, int pixel)
    {
        var offset = pixel * 3;
        return actual[offset] is <= 0 or >= 1 ||
            actual[offset + 1] is <= 0 or >= 1 ||
            actual[offset + 2] is <= 0 or >= 1;
    }

    internal static bool IsSyntheticRampEligible(
        double[] reference,
        double[] sweep,
        int width,
        int pixel)
    {
        var offset = pixel * 3;
        if (!PrecisionCensusLogic.IsUseful(reference[offset]) ||
            !PrecisionCensusLogic.IsUseful(reference[offset + 1]) ||
            !PrecisionCensusLogic.IsUseful(reference[offset + 2]))
        {
            return false;
        }
        var x = pixel % width;
        var neighbor = x + 1 < width ? pixel + 1 : pixel - 1;
        if (neighbor < 0 || sweep[neighbor] == sweep[pixel])
        {
            return false;
        }
        var neighborOffset = neighbor * 3;
        return Math.Abs(reference[offset] - reference[neighborOffset]) > 1e-12 ||
            Math.Abs(reference[offset + 1] - reference[neighborOffset + 1]) > 1e-12 ||
            Math.Abs(reference[offset + 2] - reference[neighborOffset + 2]) > 1e-12;
    }

    internal static bool IsOraclePresentUseful(double[] reference, int pixel)
    {
        var offset = pixel * 3;
        return double.IsFinite(reference[offset]) &&
            double.IsFinite(reference[offset + 1]) &&
            double.IsFinite(reference[offset + 2]) &&
            PrecisionCensusLogic.IsUseful(reference[offset]) &&
            PrecisionCensusLogic.IsUseful(reference[offset + 1]) &&
            PrecisionCensusLogic.IsUseful(reference[offset + 2]);
    }
}
