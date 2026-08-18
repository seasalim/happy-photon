namespace HappyPhoton.Tests;

internal static partial class PrecisionBoundaryCensus
{
    private static PrecisionBoundaryAggregate AggregateBoundary(
        PrecisionBoundaryOracle oracle,
        int width,
        int height,
        int[] stored,
        double[]? reference,
        PrecisionRecovery[]? recovery,
        int channel)
    {
        if (oracle != PrecisionBoundaryOracle.Analytic || reference == null)
        {
            return new PrecisionBoundaryAggregate(
                PrecisionMetricState.Inapplicable,
                PrecisionMetricState.Inapplicable,
                PrecisionMetricBasis.NotApplicable,
                stored.Length / 3, 0, 0, 0, 0, 0, null, null);
        }

        var negative = 0;
        var above = 0;
        var recoverable = 0;
        var indeterminate = 0;
        var maximumNegative = 0d;
        var maximumAbove = 0d;
        var longest = 0;
        for (var y = 0; y < height; y++)
        {
            var current = 0;
            for (var x = 0; x < width; x++)
            {
                var index = (y * width + x) * 3 + channel;
                var value = reference[index];
                var clip = PrecisionCensusLogic.ClassifyClip(value);
                if (clip == PrecisionClipDirection.Negative)
                {
                    negative++;
                    maximumNegative = Math.Max(maximumNegative, -value);
                }
                else if (clip == PrecisionClipDirection.AboveWhite)
                {
                    above++;
                    maximumAbove = Math.Max(maximumAbove, value - 1);
                }
                var resolved = recovery?[index] ??
                    (clip == PrecisionClipDirection.None
                        ? PrecisionRecovery.NotApplicable
                        : PrecisionRecovery.Indeterminate);
                recoverable += resolved == PrecisionRecovery.ReturnsUseful ? 1 : 0;
                indeterminate += resolved == PrecisionRecovery.Indeterminate ? 1 : 0;
                current = resolved == PrecisionRecovery.ReturnsUseful ? current + 1 : 0;
                longest = Math.Max(longest, current);
            }
        }
        return new PrecisionBoundaryAggregate(
            PrecisionMetricState.Available,
            PrecisionMetricState.Available,
            PrecisionMetricBasis.ExactFullPopulation,
            stored.Length / 3,
            negative,
            above,
            recoverable,
            indeterminate,
            longest,
            maximumNegative,
            maximumAbove);
    }

    private static PrecisionStoredChange AnalyzeStoredChange(
        PrecisionBoundaryOracle oracle,
        bool executed,
        ushort[] input,
        int[] stored,
        int storedMaximum)
    {
        if (oracle != PrecisionBoundaryOracle.NativeOperator || !executed)
        {
            return new PrecisionStoredChange(
                PrecisionMetricState.Inapplicable,
                PrecisionMetricBasis.NotApplicable,
                0, 0, 0, false);
        }
        var compared = Math.Min(input.Length, stored.Length);
        var changed = 0;
        var maximum = 0;
        for (var index = 0; index < compared; index++)
        {
            var inputCode = storedMaximum == ushort.MaxValue
                ? input[index]
                : (int)Math.Round(input[index] / (double)ushort.MaxValue * storedMaximum);
            var difference = Math.Abs(inputCode - stored[index]);
            changed += difference == 0 ? 0 : 1;
            maximum = Math.Max(maximum, difference);
        }
        return new PrecisionStoredChange(
            PrecisionMetricState.Available,
            PrecisionMetricBasis.ExactFullPopulation,
            compared,
            changed,
            maximum,
            input.Length != stored.Length);
    }
}
