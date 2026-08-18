using System.Text;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PrecisionEvidenceReportTests
{
    [Fact]
    public void ZeroQualityDenominatorWithExactClipsIsInapplicable()
    {
        var boundary = new PrecisionBoundaryCapture(
            "post-matrix",
            PrecisionBoundaryScope.WorkingStorage,
            PrecisionBoundaryOracle.Analytic,
            Executed: true,
            Width: 1,
            Height: 1,
            InputStoredQ16: [],
            Samples: [],
            Aggregates:
            [
                new PrecisionBoundaryAggregate(
                    PrecisionMetricState.Available,
                    PrecisionMetricState.Available,
                    PrecisionMetricBasis.ExactFullPopulation,
                    ChannelSamples: 3,
                    NegativeClips: 1,
                    AboveWhiteClips: 0,
                    Recoverable: 0,
                    Indeterminate: 1,
                    LongestRecoverableRun: 0,
                    MaximumNegativeExcursion: 0.1,
                    MaximumAboveWhiteExcursion: null)
            ],
            new PrecisionStoredChange(
                PrecisionMetricState.Inapplicable,
                PrecisionMetricBasis.NotApplicable,
                ComparedSamples: 0,
                ChangedSamples: 0,
                MaximumCodeChange: 0,
                DimensionsChanged: false),
            RetentionStride: 1);
        var payload = new StringBuilder();

        PrecisionEvidenceReport.AppendBoundary(
            payload,
            "case",
            "population",
            boundary,
            PrecisionOutputQuality.Unavailable(candidatePixels: 1));

        Assert.Contains(
            "qualityState=inapplicable " +
            "qualityReason=fully-clipped-no-unclipped-pixels",
            payload.ToString());
        Assert.Contains("clipState=available", payload.ToString());
    }
}
