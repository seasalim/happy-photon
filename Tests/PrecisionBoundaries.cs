using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;

namespace HappyPhoton.Tests;

internal static partial class PrecisionBoundaryCensus
{
    internal const int MaximumRetainedRecords = 1_000_000;

    public static PrecisionCensusCapture Capture(
        PrecisionFixture fixture,
        EditSettings settings,
        int? maxDimension)
    {
        var boundaries = new List<PrecisionBoundaryCapture>();
        var failures = new List<string>();
        var baseStored = ReadRgb16(fixture.Base.Pixels);
        boundaries.Add(CreateBoundary(
            "base", PrecisionBoundaryScope.Ingress,
            PrecisionBoundaryOracle.Analytic, true,
            fixture.Width, fixture.Height, [], baseStored,
            fixture.ExpectedLinearRgb, null));

        using var reconstructed = (MagickImage)fixture.Base.Pixels.Clone();
        var geometryInput = baseStored;
        var geometryExecuted = GeometryExecutes(settings);
        RenderGeometry.Apply(reconstructed, settings);
        var geometryStored = ReadRgb16(reconstructed);
        boundaries.Add(CreateBoundary(
            "post-geometry", PrecisionBoundaryScope.WorkingStorage,
            geometryExecuted
                ? PrecisionBoundaryOracle.NativeOperator
                : PrecisionBoundaryOracle.NotExecuted,
            geometryExecuted, (int)reconstructed.Width, (int)reconstructed.Height,
            geometryInput, geometryStored, null, null));

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
        var toneParameters = CreateToneParameters(
            fixture.Base.Info, settings, fold);
        var recovery = DetermineMatrixRecovery(
            matrixReference, toneParameters, maxDimension == null);
        boundaries.Add(CreateBoundary(
            "post-chromatic-matrix", PrecisionBoundaryScope.WorkingStorage,
            PrecisionBoundaryOracle.Analytic, true,
            (int)reconstructed.Width, (int)reconstructed.Height,
            matrixInput, matrixStored, matrixReference, recovery));

        var toneInput = matrixStored;
        var toneReference = toneInput
            .Select(value => PrecisionOracle.EvaluateTone(
                value / (double)ushort.MaxValue,
                toneParameters))
            .ToArray();
        ToneLutApplicator.Apply(reconstructed, ToneLut.Compose(toneParameters));
        RenderColorEncoding.RetagAsSrgb(reconstructed);
        var toneStored = ReadRgb16(reconstructed);
        boundaries.Add(CreateBoundary(
            "post-tone", PrecisionBoundaryScope.WorkingStorage,
            PrecisionBoundaryOracle.Analytic, true,
            (int)reconstructed.Width, (int)reconstructed.Height,
            toneInput, toneStored, toneReference, null));

        RequireInactivePostToneStages(settings);
        RenderSharpening.ApplyCapture(
            reconstructed, fixture.Base.Info, settings.Detail);
        RenderDetail.Apply(reconstructed, fixture.Base.Info, settings.Detail);

        var resizeInput = toneStored;
        var resizeExecuted = maxDimension is { } maximum &&
            (reconstructed.Width > (uint)maximum ||
             reconstructed.Height > (uint)maximum);
        if (maxDimension is { } requestedMaximum)
        {
            RenderColorEncoding.ResizeInLinearLight(
                reconstructed, requestedMaximum);
        }
        var resizeStored = ReadRgb16(reconstructed);
        boundaries.Add(CreateBoundary(
            "post-resize", PrecisionBoundaryScope.WorkingStorage,
            resizeExecuted
                ? PrecisionBoundaryOracle.NativeOperator
                : PrecisionBoundaryOracle.NotExecuted,
            resizeExecuted, (int)reconstructed.Width, (int)reconstructed.Height,
            resizeInput, resizeStored, null, null));

        using var pipeline = new RenderPipeline().Render(new RenderRequest(
            fixture.Base,
            settings,
            RenderIntent.Preview,
            maxDimension,
            new RenderOptions(false, false)));
        var pipelineRgb = ReadRgb16(pipeline.Image);
        AddEqualityFailure(
            failures, resizeStored, pipelineRgb,
            "stage reconstruction differs from RenderPipeline");

        var quantized = ReadRgb8(pipeline.Image);
        var quantizerReference = pipelineRgb
            .Select(value => Math.Round(
                value / 257d,
                MidpointRounding.AwayFromZero) / byte.MaxValue)
            .ToArray();
        var quantizerCodes = quantized.Select(value => (int)value).ToArray();
        boundaries.Add(CreateBoundary(
            "final-quantizer", PrecisionBoundaryScope.Output,
            PrecisionBoundaryOracle.Analytic, true,
            (int)pipeline.Image.Width, (int)pipeline.Image.Height,
            pipelineRgb, quantizerCodes, byte.MaxValue,
            quantizerReference, null));
        AddQuantizerFailures(failures, quantized, quantizerReference);

        var candidatePixels = checked(fixture.Width * fixture.Height);
        var ingressQuality = PrecisionOutputQuality.Unavailable(candidatePixels);
        var workingQuality = PrecisionOutputQuality.Unavailable(candidatePixels);
        if (!geometryExecuted && !resizeExecuted)
        {
            var workingReference = PropagateAnalytic(
                Normalize(baseStored), normalized.Matrix, toneParameters);
            var ingressReference = PropagateAnalytic(
                fixture.ExpectedLinearRgb, normalized.Matrix, toneParameters);
            var workingClipped = ClippedPixels(
                ApplyMatrix(baseStored, normalized.Matrix));
            var ingressClipped = ClippedPixels(
                ApplyMatrix(fixture.ExpectedLinearRgb, normalized.Matrix));
            workingQuality = AnalyzeQuality(
                Normalize(toneStored), workingReference,
                fixture.SweepParameters, fixture.Width, fixture.Height,
                workingClipped);
            ingressQuality = AnalyzeQuality(
                workingReference, ingressReference,
                fixture.SweepParameters, fixture.Width, fixture.Height,
                workingClipped.Zip(ingressClipped, (left, right) => left || right)
                    .ToArray());
        }

        return new PrecisionCensusCapture(
            boundaries,
            ingressQuality,
            workingQuality,
            fold,
            MaximumPositiveRowSum(normalized.Matrix),
            failures);
    }

    private static PrecisionBoundaryCapture CreateBoundary(
        string name,
        PrecisionBoundaryScope scope,
        PrecisionBoundaryOracle oracle,
        bool executed,
        int width,
        int height,
        ushort[] input,
        ushort[] stored,
        double[]? reference,
        PrecisionRecovery[]? recovery) =>
        CreateBoundary(
            name, scope, oracle, executed, width, height, input,
            stored.Select(value => (int)value).ToArray(), ushort.MaxValue,
            reference, recovery);

    private static PrecisionBoundaryCapture CreateBoundary(
        string name,
        PrecisionBoundaryScope scope,
        PrecisionBoundaryOracle oracle,
        bool executed,
        int width,
        int height,
        ushort[] input,
        int[] stored,
        int storedMaximum,
        double[]? reference,
        PrecisionRecovery[]? recovery)
    {
        if (stored.Length != checked(width * height * 3) ||
            reference != null && reference.Length != stored.Length ||
            recovery != null && recovery.Length != stored.Length)
        {
            throw new InvalidOperationException(
                $"Boundary {name} has inconsistent sample dimensions.");
        }

        var stride = Math.Max(1, (int)Math.Ceiling(
            stored.Length / (double)MaximumRetainedRecords));
        var samples = new List<PrecisionBoundarySample>(
            Math.Min(stored.Length, MaximumRetainedRecords));
        for (var index = 0; index < stored.Length; index++)
        {
            var pixel = index / 3;
            var value = reference?[index];
            PrecisionClipDirection? clip = value is { } known
                ? PrecisionCensusLogic.ClassifyClip(known)
                : null;
            if (index % stride != 0)
            {
                continue;
            }
            samples.Add(new PrecisionBoundarySample(
                pixel % width,
                pixel / width,
                index % 3,
                value,
                stored[index],
                storedMaximum,
                clip,
                value is null
                    ? null
                    : recovery?[index] ?? PrecisionRecovery.NotApplicable));
        }
        var aggregates = Enumerable.Range(0, 3)
            .Select(channel => AggregateBoundary(
                oracle, width, height, stored, reference, recovery, channel))
            .ToArray();
        var storedChange = AnalyzeStoredChange(
            oracle, executed, input, stored, storedMaximum);
        return new PrecisionBoundaryCapture(
            name, scope, oracle, executed, width, height, input, samples,
            aggregates, storedChange, stride);
    }

    private static PrecisionRecovery[] DetermineMatrixRecovery(
        double[] references,
        ToneParams tone,
        bool remainingStagesAreAnalytic)
    {
        var result = new PrecisionRecovery[references.Length];
        for (var index = 0; index < result.Length; index++)
        {
            var clip = PrecisionCensusLogic.ClassifyClip(references[index]);
            double? final = clip == PrecisionClipDirection.AboveWhite &&
                remainingStagesAreAnalytic
                ? QuantizeFinal(PrecisionOracle.EvaluateTone(
                    references[index], tone))
                : null;
            result[index] = PrecisionCensusLogic.DetermineRecovery(
                clip, remainingStagesAreAnalytic, final);
        }
        return result;
    }

    private static double QuantizeFinal(double value) =>
        Math.Round(
            Math.Clamp(value, 0, 1) * byte.MaxValue,
            MidpointRounding.AwayFromZero) / byte.MaxValue;

    private static ToneParams CreateToneParameters(
        BaseImageInfo info,
        EditSettings settings,
        double fold) =>
        new(
            settings.Exposure + info.SourceExposureBiasEv,
            fold,
            settings.Brightness,
            settings.Contrast,
            settings.Shadows,
            settings.Highlights,
            settings.BaseLook ?? info.IsRawSource,
            settings.Curve);

    private static double[] ApplyMatrix(ushort[] rgb, double[,] matrix) =>
        ApplyMatrix(Normalize(rgb), matrix);

    private static double MaximumPositiveRowSum(double[,] matrix) =>
        Enumerable.Range(0, 3).Max(row =>
            Enumerable.Range(0, 3).Sum(column =>
                Math.Max(matrix[row, column], 0)));

    private static double[] ApplyMatrix(double[] rgb, double[,] matrix)
    {
        var result = new double[rgb.Length];
        for (var pixel = 0; pixel < rgb.Length / 3; pixel++)
        {
            var offset = pixel * 3;
            for (var row = 0; row < 3; row++)
            {
                result[offset + row] =
                    matrix[row, 0] * rgb[offset] +
                    matrix[row, 1] * rgb[offset + 1] +
                    matrix[row, 2] * rgb[offset + 2];
            }
        }
        return result;
    }

    private static double[] PropagateAnalytic(
        double[] rgb,
        double[,] matrix,
        ToneParams tone)
    {
        var transformed = ApplyMatrix(rgb, matrix);
        var result = new double[transformed.Length];
        for (var pixel = 0; pixel < transformed.Length / 3; pixel++)
        {
            var offset = pixel * 3;
            if (transformed[offset] < 0 ||
                transformed[offset + 1] < 0 ||
                transformed[offset + 2] < 0)
            {
                result[offset] = double.NaN;
                result[offset + 1] = double.NaN;
                result[offset + 2] = double.NaN;
                continue;
            }
            result[offset] = PrecisionOracle.EvaluateTone(transformed[offset], tone);
            result[offset + 1] = PrecisionOracle.EvaluateTone(transformed[offset + 1], tone);
            result[offset + 2] = PrecisionOracle.EvaluateTone(transformed[offset + 2], tone);
        }
        return result;
    }

    private static bool[] ClippedPixels(double[] matrixReference)
    {
        var result = new bool[matrixReference.Length / 3];
        for (var pixel = 0; pixel < result.Length; pixel++)
        {
            var offset = pixel * 3;
            result[pixel] = matrixReference[offset] < 0 || matrixReference[offset] > 1 ||
                matrixReference[offset + 1] < 0 || matrixReference[offset + 1] > 1 ||
                matrixReference[offset + 2] < 0 || matrixReference[offset + 2] > 1;
        }
        return result;
    }

    private static bool GeometryExecutes(EditSettings settings) =>
        settings.Rotation != 0 || settings.HorizonRotation != 0 ||
        settings.Crop is { IsFullImage: false };

    private static void RequireInactivePostToneStages(EditSettings settings)
    {
        var chromaFactor = (100 + settings.Saturation) / 100.0 *
            (100 + settings.Vibrance * 0.5) / 100.0;
        if (chromaFactor != 1 || settings.Detail.CaptureSharpen != 0 ||
            settings.Detail.ChromaNr != 0 ||
            settings.Detail.NoiseReduction != FbddMode.Off)
        {
            throw new InvalidOperationException(
                "Slice A1 requires chroma and detail stages to be inactive.");
        }
    }

    private static double[] Normalize(ushort[] values) =>
        values.Select(value => value / (double)ushort.MaxValue).ToArray();

    private static ushort[] ReadRgb16(MagickImage image) =>
        image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGB) ??
        throw new InvalidOperationException("Unable to read Q16 RGB pixels.");

    private static byte[] ReadRgb8(MagickImage image) =>
        image.GetPixelsUnsafe().ToByteArray(PixelMapping.RGB) ??
        throw new InvalidOperationException("Unable to read RGB8 pixels.");

    private static void AddEqualityFailure(
        List<string> failures,
        ushort[] expected,
        ushort[] actual,
        string message)
    {
        if (!expected.AsSpan().SequenceEqual(actual))
        {
            var first = Enumerable.Range(0, Math.Min(expected.Length, actual.Length))
                .FirstOrDefault(index => expected[index] != actual[index]);
            failures.Add($"{message}; first difference at channel sample {first}.");
        }
    }

    private static void AddQuantizerFailures(
        List<string> failures,
        byte[] actual,
        double[] reference)
    {
        for (var index = 0; index < actual.Length; index++)
        {
            var expected = (byte)Math.Round(
                reference[index] * byte.MaxValue,
                MidpointRounding.AwayFromZero);
            if (actual[index] != expected)
            {
                failures.Add(
                    $"final quantizer differs at channel sample {index}: " +
                    $"expected {expected}, observed {actual[index]}.");
                return;
            }
        }
    }
}
