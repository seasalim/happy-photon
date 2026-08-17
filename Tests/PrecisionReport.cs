using System.Globalization;
using System.Text;

namespace HappyPhoton.Tests;

internal static class PrecisionReport
{
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

    private static string Boolean(bool value) => value ? "true" : "false";
}
