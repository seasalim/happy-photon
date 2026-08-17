using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HappyPhoton.Tests;

internal static class PrecisionReport
{
    public static void AppendCensusCase(
        StringBuilder report,
        string caseName,
        string toneVector,
        int? maxDimension,
        PrecisionFixture fixture,
        PrecisionCensusCapture capture)
    {
        report.Append("CENSUS_CASE name=").Append(caseName)
            .Append(" fixture=").Append(fixture.Name)
            .Append(" tone=").Append(toneVector)
            .Append(" maxDimension=").Append(maxDimension?.ToString(
                CultureInfo.InvariantCulture) ?? "none")
            .Append(" width=").Append(fixture.Width)
            .Append(" height=").Append(fixture.Height)
            .Append(" fold=").Append(Format(capture.Fold))
            .Append(" normalizedCubeMaximum=")
            .Append(Format(capture.NormalizedCubeMaximum))
            .Append(" foldPreventsAboveWhite=")
            .Append(Boolean(capture.NormalizedCubeMaximum <= 1 + 1e-12))
            .AppendLine();
        if (fixture.Population is { } population)
        {
            report.Append("CENSUS_POPULATION case=").Append(caseName)
                .Append(" kind=").Append(population.Kind)
                .Append(" rowSemantics=").Append(population.RowSemantics)
                .Append(" intensity=").Append(population.Intensity)
                .AppendLine();
        }
        foreach (var boundary in capture.Boundaries)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                AppendBoundary(report, caseName, boundary, channel);
            }
        }
        AppendQuality(report, caseName, "ingress", capture.IngressQuality);
        AppendQuality(
            report, caseName, "working-storage", capture.WorkingStorageQuality);
        report.Append("CENSUS_GATE name=").Append(caseName)
            .Append(" reconstruction=")
            .Append(capture.GateFailures.Count == 0 ? "pass" : "fail")
            .Append(" failures=").Append(capture.GateFailures.Count)
            .AppendLine();
    }

    public static void AppendCase(
        StringBuilder report,
        PrecisionCaseMetrics result,
        PrecisionParityMetrics nativeParity)
    {
        foreach (var checkpoint in result.Checkpoints)
        {
            foreach (var row in checkpoint.Rows)
            {
                AppendRow(report, result, checkpoint.Checkpoint, row);
            }
            if (checkpoint.Checkpoint.IsByteOutput)
            {
                report.Append("BLOCK fixture=").Append(result.Fixture)
                    .Append(" vector=").Append(result.Vector)
                    .Append(" checkpoint=").Append(checkpoint.Checkpoint.Number)
                    .Append(" name=").Append(checkpoint.Checkpoint.Name)
                    .Append(" p99BlockMean=").Append(Format(checkpoint.BlockMeanP99))
                    .Append(" finalBanding=").Append(Boolean(checkpoint.FinalOutputBanding))
                    .AppendLine();
            }
            report.Append("THRESHOLD fixture=").Append(result.Fixture)
                .Append(" vector=").Append(result.Vector)
                .Append(" checkpoint=").Append(checkpoint.Checkpoint.Number)
                .Append(" name=").Append(checkpoint.Checkpoint.Name)
                .Append(" preOutputBanding=").Append(Boolean(checkpoint.PreOutputBanding))
                .Append(" finalOutputBanding=").Append(Boolean(checkpoint.FinalOutputBanding))
                .AppendLine();
        }

        AppendParity(report, result, "native", nativeParity);
        foreach (var row in result.Dither.Checkpoint.Rows)
        {
            AppendRow(report, result, result.Dither.Checkpoint.Checkpoint, row);
        }
        report.Append("DITHER fixture=").Append(result.Fixture)
            .Append(" vector=").Append(result.Vector)
            .Append(" nativeBlockP99=").Append(Format(result.Dither.NativeBlockMeanP99))
            .Append(" ditherBlockP99=").Append(Format(result.Dither.Checkpoint.BlockMeanP99))
            .Append(" reduction=").Append(Format(result.Dither.BlockMeanReduction))
            .Append(" pointP99=").Append(Format(result.Dither.PointErrorP99))
            .Append(" previewExportMatch=").Append(Boolean(result.Dither.PreviewExportMatch))
            .Append(" viable=").Append(Boolean(result.Dither.Viable))
            .AppendLine();
        AppendParity(report, result, "ordered-dither", result.Dither.Parity);
    }

    private static void AppendParity(
        StringBuilder report,
        PrecisionCaseMetrics result,
        string output,
        PrecisionParityMetrics parity)
    {
        report.Append("PARITY fixture=").Append(result.Fixture)
            .Append(" vector=").Append(result.Vector)
            .Append(" output=").Append(output)
            .Append(" channelSamples=").Append(parity.TotalChannelSamples)
            .Append(" differing=").Append(parity.DifferingChannelSamples)
            .Append(" differingFraction=").Append(Format(parity.DifferingFraction))
            .Append(" pngMinusPreviewMin=").Append(parity.PngMinusPreviewMinimum)
            .Append(" pngMinusPreviewMean=").Append(Format(parity.PngMinusPreviewMean))
            .Append(" pngMinusPreviewMax=").Append(parity.PngMinusPreviewMaximum)
            .Append(" nearestVsTowardZero=").Append(Boolean(parity.NearestVersusTowardZero))
            .Append(" directMatch=").Append(Boolean(parity.DirectMappingMatch))
            .Append(" directWriteMatch=").Append(Boolean(parity.DirectWriteMatch))
            .Append(" profileWriteMatch=").Append(Boolean(parity.ProfileWriteMatch))
            .Append(" depthWriteMatch=").Append(Boolean(parity.DepthWriteMatch))
            .Append(" firstDivergence=").Append(parity.FirstDivergence)
            .AppendLine();
    }

    private static void AppendBoundary(
        StringBuilder report,
        string caseName,
        PrecisionBoundaryCapture boundary,
        int channel)
    {
        var samples = boundary.Samples
            .Where(sample => sample.Channel == channel)
            .ToArray();
        var negative = samples.Count(sample =>
            sample.Clip == PrecisionClipDirection.Negative);
        var above = samples.Count(sample =>
            sample.Clip == PrecisionClipDirection.AboveWhite);
        var recoverable = samples.Count(sample =>
            sample.Recovery == PrecisionRecovery.ReturnsUseful);
        var indeterminate = samples.Count(sample =>
            sample.Recovery == PrecisionRecovery.Indeterminate);
        var longestRecoverable = Enumerable.Range(0, boundary.Height)
            .Select(row => PrecisionCensusLogic.LongestContiguousRun(
                samples.Where(sample => sample.Y == row)
                    .OrderBy(sample => sample.X)
                    .Select(sample =>
                        sample.Recovery == PrecisionRecovery.ReturnsUseful)
                    .ToArray()))
            .DefaultIfEmpty(0)
            .Max();
        var maximumNegative = samples
            .Where(sample => sample.UnclampedReference < 0)
            .Select(sample => -sample.UnclampedReference!.Value)
            .DefaultIfEmpty(0)
            .Max();
        var maximumAbove = samples
            .Where(sample => sample.UnclampedReference > 1)
            .Select(sample => sample.UnclampedReference!.Value - 1)
            .DefaultIfEmpty(0)
            .Max();
        report.Append("CENSUS_BOUNDARY case=").Append(caseName)
            .Append(" name=").Append(boundary.Name)
            .Append(" channel=").Append("RGB"[channel])
            .Append(" scope=").Append(Token(boundary.Scope))
            .Append(" oracle=").Append(Token(boundary.Oracle))
            .Append(" executed=").Append(Boolean(boundary.Executed))
            .Append(" inputQ16Samples=").Append(boundary.InputStoredQ16.Length)
            .Append(" sampleRecords=").Append(samples.Length)
            .Append(" storedMaximum=").Append(samples[0].StoredMaximum)
            .Append(" negativeClips=").Append(negative)
            .Append(" aboveWhiteClips=").Append(above)
            .Append(" recoverable=").Append(recoverable)
            .Append(" indeterminate=").Append(indeterminate)
            .Append(" longestRecoverableRun=").Append(longestRecoverable)
            .Append(" maxNegativeExcursion=").Append(Format(maximumNegative))
            .Append(" maxAboveWhiteExcursion=").Append(Format(maximumAbove))
            .Append(" sampleDigest=").Append(Digest(samples))
            .AppendLine();
    }

    private static void AppendQuality(
        StringBuilder report,
        string caseName,
        string scope,
        PrecisionOutputQuality quality)
    {
        report.Append("CENSUS_QUALITY case=").Append(caseName)
            .Append(" scope=").Append(scope)
            .Append(" available=").Append(Boolean(quality.Available))
            .Append(" eligiblePixels=").Append(quality.EligiblePixels)
            .Append(" candidatePixels=").Append(quality.CandidatePixels)
            .Append(" eligibleFraction=").Append(Format(quality.EligibleFraction))
            .Append(" meanDeltaE00=").Append(Format(quality.MeanDeltaE00))
            .Append(" p99DeltaE00=").Append(Format(quality.P99DeltaE00))
            .Append(" maxDeltaE00=").Append(Format(quality.MaximumDeltaE00))
            .AppendLine();
    }

    private static string Digest(IReadOnlyList<PrecisionBoundarySample> samples)
    {
        var canonical = new StringBuilder(samples.Count * 32);
        foreach (var sample in samples)
        {
            canonical.Append(sample.X).Append(',').Append(sample.Y).Append(',')
                .Append(sample.Channel).Append(',')
                .Append(sample.UnclampedReference?.ToString(
                    "R", CultureInfo.InvariantCulture) ?? "none")
                .Append(',').Append(sample.StoredCode).Append(',')
                .Append(sample.StoredMaximum).Append(',')
                .Append((int)sample.Clip).Append(',')
                .Append((int)sample.Recovery).Append(';');
        }
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    private static string Token<T>(T value) where T : struct, Enum =>
        value.ToString().ToLowerInvariant();

    private static void AppendRow(
        StringBuilder report,
        PrecisionCaseMetrics result,
        PrecisionCheckpoint checkpoint,
        PrecisionMetricRow row)
    {
        report.Append("METRIC fixture=").Append(result.Fixture)
            .Append(" vector=").Append(result.Vector)
            .Append(" checkpoint=").Append(checkpoint.Number)
            .Append(" name=").Append(checkpoint.Name)
            .Append(" row=").Append(row.Row)
            .Append(" useful=").Append(row.UsefulSamples)
            .Append(" actualUnique=").Append(row.ActualUnique)
            .Append(" referenceUnique=").Append(row.ReferenceUnique)
            .Append(" coverage=").Append(Format(row.UniqueCoverage))
            .Append(" longestRun=").Append(row.LongestIdenticalRun)
            .Append(" maxStep=").Append(Format(row.MaximumStep))
            .Append(" maxStepExcess=").Append(Format(row.MaximumStepExcess))
            .Append(" p99AbsError=").Append(Format(row.P99AbsoluteError))
            .Append(" longestMissingCodes=").Append(row.LongestMissingCodes)
            .Append(" signedMeanError=").Append(Format(row.SignedMeanError))
            .Append(" signedMinError=").Append(Format(row.SignedMinimumError))
            .Append(" signedMaxError=").Append(Format(row.SignedMaximumError))
            .Append(" preOutputBanding=").Append(Boolean(row.PreOutputBanding))
            .AppendLine();
    }

    private static string Format(double value) =>
        value.ToString("F9", CultureInfo.InvariantCulture);

    private static string Format(double? value) =>
        value is { } available ? Format(available) : "null";

    private static string Boolean(bool value) => value ? "true" : "false";
}
