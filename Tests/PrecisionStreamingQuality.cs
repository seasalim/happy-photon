namespace HappyPhoton.Tests;

internal static class PrecisionStreamingQuality
{
    public static PrecisionOutputQuality Measure(
        int candidatePixels,
        Func<int, double?> getError)
    {
        var cache = new double?[candidatePixels];
        for (var pixel = 0; pixel < candidatePixels; pixel++)
        {
            cache[pixel] = getError(pixel);
        }
        return Measure(
            candidatePixels,
            pixel => cache[pixel] is not null,
            pixel => cache[pixel]!.Value);
    }

    public static PrecisionOutputQuality Measure(
        int candidatePixels,
        Func<int, bool> isEligible,
        Func<int, double> getError)
    {
        var eligible = 0;
        var below = 0;
        var maximum = 0d;
        var sum = 0d;
        for (var pixel = 0; pixel < candidatePixels; pixel++)
        {
            if (!isEligible(pixel))
            {
                continue;
            }
            var error = getError(pixel);
            eligible++;
            below += error < 1 ? 1 : 0;
            maximum = Math.Max(maximum, error);
            sum += error;
        }
        if (eligible == 0)
        {
            return PrecisionOutputQuality.Unavailable(candidatePixels);
        }
        var stride = Math.Max(1, (int)Math.Ceiling(
            eligible / (double)PrecisionBoundaryCensus.MaximumRetainedRecords));
        var retained = new List<double>(Math.Min(
            eligible,
            PrecisionBoundaryCensus.MaximumRetainedRecords));
        var index = 0;
        for (var pixel = 0; pixel < candidatePixels; pixel++)
        {
            if (!isEligible(pixel))
            {
                continue;
            }
            if (index % stride == 0)
            {
                retained.Add(getError(pixel));
            }
            index++;
        }
        retained.Sort();
        var rank = (int)Math.Ceiling(eligible * 0.99);
        var descriptiveRank = Math.Max(
            0,
            (int)Math.Ceiling(retained.Count * 0.99) - 1);
        return new PrecisionOutputQuality(
            true,
            candidatePixels,
            eligible,
            sum / eligible,
            retained[descriptiveRank],
            maximum,
            below,
            rank,
            below < rank,
            PrecisionMetricBasis.ExactFullPopulation,
            stride == 1
                ? PrecisionMetricBasis.ExactFullPopulation
                : PrecisionMetricBasis.DescriptiveSystematicSample,
            retained.Count,
            stride);
    }
}
