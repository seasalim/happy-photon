using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;

namespace HappyPhoton.Tests;

internal sealed record PrecisionStackedCapture(
    PrecisionCensusCapture Capture,
    IReadOnlyDictionary<string, PrecisionOutputQuality> Quality,
    IReadOnlyDictionary<string, bool> StageExecuted);

internal static partial class PrecisionBoundaryCensus
{
    public static PrecisionStackedCapture CaptureStacked(
        PrecisionFixture fixture,
        EditSettings settings,
        int maxDimension)
    {
        var boundaries = new List<PrecisionBoundaryCapture>();
        var failures = new List<string>();
        var quality = new Dictionary<string, PrecisionOutputQuality>();
        var executed = new Dictionary<string, bool>();
        var baseStored = ReadRgb16(fixture.Base.Pixels);
        boundaries.Add(CreateBoundary(
            "base", PrecisionBoundaryScope.Ingress,
            PrecisionBoundaryOracle.Analytic, true,
            fixture.Width, fixture.Height, [], baseStored,
            fixture.ExpectedLinearRgb, null));

        using var reconstructed = (MagickImage)fixture.Base.Pixels.Clone();
        RenderGeometry.Apply(reconstructed, settings);
        var geometryStored = ReadRgb16(reconstructed);
        var geometry = CreateBoundary(
            "post-geometry", PrecisionBoundaryScope.WorkingStorage,
            PrecisionBoundaryOracle.NativeOperator, true,
            (int)reconstructed.Width, (int)reconstructed.Height,
            baseStored, geometryStored, null, null);
        boundaries.Add(geometry);
        executed["geometry"] = geometry.StoredChange.DimensionsChanged ||
            geometry.StoredChange.ChangedSamples > 0;

        var matrix = WhiteBalanceModel.CreateMatrix(
            settings.Wb.Kelvin!.Value,
            settings.Wb.Tint!.Value,
            fixture.Base.Info.AsShotKelvin,
            fixture.Base.Info.AsShotTint);
        var normalized = ChromaticAdaptation.NormalizeForRender(matrix);
        var matrixReference = ApplyMatrix(geometryStored, normalized.Matrix);
        var matrixInput = geometryStored;
        var fold = RenderChromaticStage.Apply(
            reconstructed, fixture.Base.Info, settings);
        var matrixStored = ReadRgb16(reconstructed);
        var matrixRecovery = DetermineMatrixRecovery(
            matrixReference,
            CreateToneParameters(fixture.Base.Info, settings, fold),
            remainingStagesAreAnalytic: false);
        boundaries.Add(CreateBoundary(
            "post-chromatic-matrix", PrecisionBoundaryScope.WorkingStorage,
            PrecisionBoundaryOracle.Analytic, true,
            (int)reconstructed.Width, (int)reconstructed.Height,
            matrixInput, matrixStored, matrixReference, matrixRecovery));
        executed["color"] = !matrixInput.AsSpan().SequenceEqual(matrixStored);
        quality["post-chromatic-matrix"] = AnalyzePhotographicQuality(
            EncodeLinear(matrixStored),
            EncodeLinear(matrixReference),
            (int)reconstructed.Width,
            (int)reconstructed.Height,
            ClippedPixels(matrixReference));

        var tone = CreateToneParameters(fixture.Base.Info, settings, fold);
        var toneReference = matrixStored.Select(value =>
            PrecisionOracle.EvaluateTone(
                value / (double)ushort.MaxValue,
                tone)).ToArray();
        var toneInput = matrixStored;
        ToneLutApplicator.Apply(reconstructed, ToneLut.Compose(tone));
        RenderColorEncoding.RetagAsSrgb(reconstructed);
        var toneStored = ReadRgb16(reconstructed);
        boundaries.Add(CreateBoundary(
            "post-tone", PrecisionBoundaryScope.WorkingStorage,
            PrecisionBoundaryOracle.Analytic, true,
            (int)reconstructed.Width, (int)reconstructed.Height,
            toneInput, toneStored, toneReference, null));
        executed["tone"] = !toneInput.AsSpan().SequenceEqual(toneStored);
        quality["post-tone"] = AnalyzePhotographicQuality(
            Normalize(toneStored),
            toneReference,
            (int)reconstructed.Width,
            (int)reconstructed.Height,
            new bool[checked((int)reconstructed.Width * (int)reconstructed.Height)]);

        var chromaInput = toneStored;
        var chromaCalled = RenderChromaStage.Apply(reconstructed, settings);
        var chromaStored = ReadRgb16(reconstructed);
        boundaries.Add(CreateBoundary(
            "post-chroma", PrecisionBoundaryScope.WorkingStorage,
            PrecisionBoundaryOracle.NativeOperator, chromaCalled,
            (int)reconstructed.Width, (int)reconstructed.Height,
            chromaInput, chromaStored, null, null));
        executed["chroma"] = chromaCalled &&
            !chromaInput.AsSpan().SequenceEqual(chromaStored);

        var sharpenInput = chromaStored;
        RenderSharpening.ApplyCapture(
            reconstructed, fixture.Base.Info, settings.Detail);
        var sharpenStored = ReadRgb16(reconstructed);
        boundaries.Add(CreateBoundary(
            "post-capture-sharpen", PrecisionBoundaryScope.WorkingStorage,
            PrecisionBoundaryOracle.NativeOperator, true,
            (int)reconstructed.Width, (int)reconstructed.Height,
            sharpenInput, sharpenStored, null, null));
        executed["capture-sharpen"] =
            !sharpenInput.AsSpan().SequenceEqual(sharpenStored);

        var detailInput = sharpenStored;
        RenderDetail.Apply(reconstructed, fixture.Base.Info, settings.Detail);
        var detailStored = ReadRgb16(reconstructed);
        boundaries.Add(CreateBoundary(
            "post-chroma-nr", PrecisionBoundaryScope.WorkingStorage,
            PrecisionBoundaryOracle.NativeOperator, true,
            (int)reconstructed.Width, (int)reconstructed.Height,
            detailInput, detailStored, null, null));
        executed["chroma-nr"] = !detailInput.AsSpan().SequenceEqual(detailStored);

        var resizeInput = detailStored;
        RenderColorEncoding.ResizeInLinearLight(reconstructed, maxDimension);
        var resizeStored = ReadRgb16(reconstructed);
        var resizeBoundary = CreateBoundary(
            "post-resize", PrecisionBoundaryScope.WorkingStorage,
            PrecisionBoundaryOracle.NativeOperator, true,
            (int)reconstructed.Width, (int)reconstructed.Height,
            resizeInput, resizeStored, null, null);
        boundaries.Add(resizeBoundary);
        executed["resize"] = resizeBoundary.StoredChange.DimensionsChanged;

        using var pipeline = new RenderPipeline().Render(new RenderRequest(
            fixture.Base,
            settings,
            RenderIntent.Preview,
            maxDimension,
            new RenderOptions(false, false)));
        var pipelineRgb = ReadRgb16(pipeline.Image);
        AddEqualityFailure(
            failures, resizeStored, pipelineRgb,
            "stacked stage reconstruction differs from RenderPipeline");
        foreach (var stage in executed.Where(pair => !pair.Value))
        {
            failures.Add($"stacked stage {stage.Key} did not execute non-identity math");
        }

        var quantized = ReadRgb8(pipeline.Image);
        var quantizerReference = pipelineRgb.Select(value => Math.Round(
            value / 257d,
            MidpointRounding.AwayFromZero) / byte.MaxValue).ToArray();
        boundaries.Add(CreateBoundary(
            "final-quantizer", PrecisionBoundaryScope.Output,
            PrecisionBoundaryOracle.Analytic, true,
            (int)pipeline.Image.Width, (int)pipeline.Image.Height,
            pipelineRgb,
            quantized.Select(value => (int)value).ToArray(),
            byte.MaxValue,
            quantizerReference,
            null));
        AddQuantizerFailures(failures, quantized, quantizerReference);

        var capture = new PrecisionCensusCapture(
            boundaries,
            PrecisionOutputQuality.Unavailable(fixture.Width * fixture.Height),
            PrecisionOutputQuality.Unavailable(fixture.Width * fixture.Height),
            fold,
            MaximumPositiveRowSum(normalized.Matrix),
            failures);
        return new PrecisionStackedCapture(capture, quality, executed);
    }

    private static double[] EncodeLinear(ushort[] values) => values
        .Select(value => EncodeLinear(value / (double)ushort.MaxValue))
        .ToArray();

    private static double[] EncodeLinear(double[] values) => values
        .Select(value => EncodeLinear(Math.Clamp(value, 0, 1)))
        .ToArray();

    private static double EncodeLinear(double value) => value <= 0.0031308
        ? 12.92 * value
        : 1.055 * Math.Pow(value, 1 / 2.4) - 0.055;
}
