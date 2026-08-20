using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;

namespace HappyPhoton.Tests;

internal sealed class PrecisionCapture : IDisposable
{
    public PrecisionCapture(
        IReadOnlyList<PrecisionCheckpoint> checkpoints,
        MagickImage rendered,
        PrecisionParityMetrics parity,
        IReadOnlyList<string> gateFailures)
    {
        Checkpoints = checkpoints;
        Rendered = rendered;
        Parity = parity;
        GateFailures = gateFailures;
    }

    public IReadOnlyList<PrecisionCheckpoint> Checkpoints { get; }
    public MagickImage Rendered { get; }
    public PrecisionParityMetrics Parity { get; }
    public bool PreviewExportMatch => Parity.Match;
    public IReadOnlyList<string> GateFailures { get; }

    public void Dispose() => Rendered.Dispose();
}

internal sealed record PrecisionCheckpoint(
    int Number,
    string Name,
    double[] Actual,
    double[] Reference,
    double[] UsefulReference,
    bool IsByteOutput);

internal sealed record PrecisionQuantizedOutput(
    double[] Red,
    byte[] Rgb);

internal sealed record PrecisionQuantizedPair(
    PrecisionQuantizedOutput Preview,
    PrecisionQuantizedOutput Png,
    PrecisionParityMetrics Parity);

internal sealed record PrecisionParityMetrics(
    int TotalChannelSamples,
    int DifferingChannelSamples,
    double DifferingFraction,
    int PngMinusPreviewMinimum,
    double PngMinusPreviewMean,
    int PngMinusPreviewMaximum,
    bool NearestVersusTowardZero,
    bool DirectMappingMatch,
    bool DirectWriteMatch,
    bool ProfileWriteMatch,
    bool DepthWriteMatch,
    string FirstDivergence)
{
    public bool Match => DifferingChannelSamples == 0;
}

internal static class PrecisionOracle
{
    public static PrecisionCapture Capture(
        PrecisionFixture fixture,
        EditSettings settings,
        string pngPath)
    {
        var baseRgb = ReadRgb16(fixture.Base.Pixels);
        var gateFailures = new List<string>();
        if (fixture.LoadedFromTiff)
        {
            gateFailures.AddRange(ValidateTiffBase(fixture, baseRgb));
        }

        var expectedLinear = ExpandRows(
            fixture.ExpectedLinear,
            fixture.Width,
            fixture.Height);
        var baseSamples = ReadRedNormalized(baseRgb);

        using var reconstructed = (MagickImage)fixture.Base.Pixels.Clone();
        RenderGeometry.Apply(reconstructed, settings);
        var fold = RenderChromaticStage.Apply(
            reconstructed,
            fixture.Base.Info,
            settings);
        var matrixRgb = ReadRgb16(reconstructed);
        var matrixSamples = ReadRedNormalized(matrixRgb);
        var normalized = RenderChromaticStage.CreateNormalizedMatrix(
            fixture.Base.Info,
            settings);
        var neutralScale = normalized.Matrix[0, 0] +
            normalized.Matrix[0, 1] + normalized.Matrix[0, 2];
        var parameters = CreateToneParameters(settings, fold);
        var expectedTone = expectedLinear
            .Select(value => EvaluateTone(value * neutralScale, parameters))
            .ToArray();
        var baseTone = matrixSamples
            .Select(value => EvaluateTone(value, parameters))
            .ToArray();
        var lut = ToneLut.Compose(parameters);
        ToneLutApplicator.Apply(reconstructed, lut);
        RenderColorEncoding.RetagAsSrgb(reconstructed);
        RenderSharpening.ApplyCapture(
            reconstructed,
            fixture.Base.Info,
            settings.Detail);
        RenderDetail.Apply(
            reconstructed,
            fixture.Base.Info,
            settings.Detail);
        var toneStageRgb = ReadRgb16(reconstructed);
        RenderColorEncoding.ConvertEncodedRec2020ToTarget(
            reconstructed,
            OutputColorSpace.Srgb);

        using var result = new RenderPipeline().Render(new RenderRequest(
            fixture.Base,
            settings,
            RenderIntent.Preview,
            MaxDimension: null,
            new RenderOptions(
                ComputeStats: false,
                ComputeOverlayMasks: false)));
        var reconstructedRgb = ReadRgb16(reconstructed);
        var pipelineRgb = ReadRgb16(result.Image);
        RequireEqual(
            reconstructedRgb,
            pipelineRgb,
            "Stage reconstruction differs from the full RenderPipeline.");

        var output = Quantize(result.Image, pngPath);

        var rendered = (MagickImage)result.Image.Clone();
        return new PrecisionCapture(
        [
            new PrecisionCheckpoint(
                1, "expected-linear", expectedLinear, expectedLinear, expectedTone, false),
            new PrecisionCheckpoint(
                2, "base-q16", baseSamples, expectedLinear, expectedTone, false),
            new PrecisionCheckpoint(
                3, "expected-continuous-tone", expectedTone, expectedTone, expectedTone, false),
            new PrecisionCheckpoint(
                4, "base-continuous-tone", baseTone, expectedTone, expectedTone, false),
            new PrecisionCheckpoint(
                5, "tone-stage-q16", ReadRedNormalized(toneStageRgb), baseTone, baseTone, false),
            new PrecisionCheckpoint(
                6, "render-pipeline-q16", ReadRedNormalized(pipelineRgb), baseTone, baseTone, false),
            new PrecisionCheckpoint(
                7, "preview-bgra8", output.Preview.Red, baseTone, baseTone, true),
            new PrecisionCheckpoint(
                8, "png8-readback", output.Png.Red, baseTone, baseTone, true)
        ], rendered, output.Parity, gateFailures);
    }

    public static PrecisionQuantizedPair Quantize(
        MagickImage image,
        string pngPath)
    {
        var preview = QuantizePreview(image);
        var q16 = ReadRgb16(image);
        var direct = ReadRgb8(image);
        var directWrite = ProbePngWrite(image, pngPath + ".direct.png", false, false);
        var profileWrite = ProbePngWrite(image, pngPath + ".profile.png", true, false);
        var depthWrite = ProbePngWrite(image, pngPath + ".depth.png", true, true);
        var png = QuantizePng(image, pngPath);
        var parity = AnalyzeParity(
            preview.Rgb,
            png.Rgb,
            q16,
            direct,
            directWrite,
            profileWrite,
            depthWrite);
        return new PrecisionQuantizedPair(
            preview,
            png,
            parity);
    }

    internal static ToneParams CreateToneParameters(
        EditSettings settings,
        double fold = 1) =>
        new(
            settings.Exposure,
            fold,
            settings.Brightness,
            settings.Contrast,
            settings.Shadows,
            settings.Highlights,
            settings.BaseLook ?? false,
            settings.Curve);

    internal static double EvaluateTone(double linear, ToneParams parameters)
    {
        var exposed = linear * ToneLut.ExposureGain(
            parameters.ExposureEv,
            parameters.Fold);
        var shouldered = ToneLut.HighlightShoulder(
            exposed,
            ToneLut.HighlightKnee(parameters.Highlights));
        var display = ToneLut.SrgbEncode(Math.Min(shouldered, 1));
        var looked = parameters.BaseLookEnabled
            ? ToneLut.BaseLook(display)
            : display;
        var brightened = ToneLut.ApplyBrightness(looked, parameters.Brightness);
        var contrasted = ToneLut.ApplyContrast(
            brightened,
            ToneLut.ContrastSlope(parameters.Contrast));
        var shadowed = ToneLut.ApplyShadows(contrasted, parameters.Shadows);
        var highlighted = ToneLut.ApplyPositiveHighlights(
            shadowed,
            parameters.Highlights);
        var curved = ToneLut.EvaluateCurve(parameters.Curve, highlighted);
        return Math.Clamp(curved, 0, 1);
    }

    private static PrecisionQuantizedOutput QuantizePreview(MagickImage image)
    {
        using var bitmap = BitmapConversionService.ConvertToBitmap(image) ??
            throw new InvalidOperationException("Preview bitmap conversion returned null.");
        var bgra = BitmapConversionService.CopyBgraPixels(bitmap);
        var rgb = new byte[checked((bgra.Length / 4) * 3)];
        var red = new double[bgra.Length / 4];
        for (var pixel = 0; pixel < red.Length; pixel++)
        {
            var bgraOffset = pixel * 4;
            var rgbOffset = pixel * 3;
            rgb[rgbOffset] = bgra[bgraOffset + 2];
            rgb[rgbOffset + 1] = bgra[bgraOffset + 1];
            rgb[rgbOffset + 2] = bgra[bgraOffset];
            red[pixel] = bgra[bgraOffset + 2] / (double)byte.MaxValue;
        }
        return new PrecisionQuantizedOutput(red, rgb);
    }

    private static PrecisionQuantizedOutput QuantizePng(
        MagickImage image,
        string path)
    {
        using (var output = (MagickImage)image.Clone())
        {
            ExportEncoder.Write(
                output,
                new ExportSettings
                {
                    Format = ExportFormat.Png,
                    OutputSharpening = false
                },
                OutputColorSpace.Srgb,
                path);
        }

        using var reopened = new MagickImage(path);
        var rgb = reopened.GetPixelsUnsafe().ToByteArray(PixelMapping.RGB) ??
            throw new InvalidOperationException("Unable to read the exported PNG pixels.");
        var red = new double[rgb.Length / 3];
        for (var pixel = 0; pixel < red.Length; pixel++)
        {
            red[pixel] = rgb[pixel * 3] / (double)byte.MaxValue;
        }
        return new PrecisionQuantizedOutput(red, rgb);
    }

    private static byte[] ProbePngWrite(
        MagickImage image,
        string path,
        bool setProfile,
        bool setDepth)
    {
        using (var probe = (MagickImage)image.Clone())
        {
            probe.Format = MagickFormat.Png;
            if (setProfile)
            {
                probe.SetProfile(ColorProfiles.SRGB);
            }
            if (setDepth)
            {
                probe.Depth = 8;
            }
            probe.Write(path);
        }
        using var reopened = new MagickImage(path);
        return ReadRgb8(reopened);
    }

    private static PrecisionParityMetrics AnalyzeParity(
        byte[] preview,
        byte[] png,
        ushort[] q16,
        byte[] direct,
        byte[] directWrite,
        byte[] profileWrite,
        byte[] depthWrite)
    {
        if (preview.Length != png.Length || preview.Length != q16.Length ||
            preview.Length != direct.Length || preview.Length != directWrite.Length ||
            preview.Length != profileWrite.Length || preview.Length != depthWrite.Length)
        {
            throw new InvalidOperationException(
                "Parity probe sample counts do not match.");
        }
        var differing = 0;
        var signedSum = 0L;
        var signedMinimum = int.MaxValue;
        var signedMaximum = int.MinValue;
        var ruleConsistent = true;
        for (var index = 0; index < preview.Length; index++)
        {
            var difference = png[index] - preview[index];
            if (difference != 0)
            {
                differing++;
            }
            signedSum += difference;
            signedMinimum = Math.Min(signedMinimum, difference);
            signedMaximum = Math.Max(signedMaximum, difference);

            var nearest = (int)Math.Round(
                q16[index] / 257d,
                MidpointRounding.AwayFromZero);
            var towardZero = (int)Math.Floor(q16[index] / 257d);
            ruleConsistent &= preview[index] == nearest && png[index] == towardZero;
        }

        var directMatch = preview.AsSpan().SequenceEqual(direct);
        var directWriteMatch = preview.AsSpan().SequenceEqual(directWrite);
        var profileWriteMatch = preview.AsSpan().SequenceEqual(profileWrite);
        var depthWriteMatch = preview.AsSpan().SequenceEqual(depthWrite);
        var pngMatch = differing == 0;
        var firstDivergence = !directMatch
            ? "direct-mapping"
            : !directWriteMatch
                ? "png-write"
                : !profileWriteMatch
                    ? "set-srgb-profile"
                    : !depthWriteMatch
                        ? "set-depth-8"
                        : !pngMatch
                            ? "png-write-defines"
                            : "none";
        return new PrecisionParityMetrics(
            preview.Length,
            differing,
            differing / (double)preview.Length,
            signedMinimum,
            signedSum / (double)preview.Length,
            signedMaximum,
            ruleConsistent,
            directMatch,
            directWriteMatch,
            profileWriteMatch,
            depthWriteMatch,
            firstDivergence);
    }

    private static ushort[] ReadRgb16(MagickImage image) =>
        image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGB) ??
        throw new InvalidOperationException("Unable to read Q16 RGB pixels.");

    private static byte[] ReadRgb8(MagickImage image) =>
        image.GetPixelsUnsafe().ToByteArray(PixelMapping.RGB) ??
        throw new InvalidOperationException("Unable to read RGB8 pixels.");

    private static double[] ReadRedNormalized(ushort[] rgb)
    {
        var result = new double[rgb.Length / 3];
        for (var pixel = 0; pixel < result.Length; pixel++)
        {
            result[pixel] = rgb[pixel * 3] / (double)ushort.MaxValue;
        }
        return result;
    }

    private static double[] ExpandRows(double[] columns, int width, int height)
    {
        var result = new double[checked(width * height)];
        for (var y = 0; y < height; y++)
        {
            columns.CopyTo(result, y * width);
        }
        return result;
    }

    private static IReadOnlyList<string> ValidateTiffBase(
        PrecisionFixture fixture,
        ushort[] rgb)
    {
        var failures = new List<string>();
        var maximumDeviation = 0;
        string? firstQuantizationFailure = null;
        for (var y = 0; y < fixture.Height; y++)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                ushort previous = 0;
                for (var x = 0; x < fixture.Width; x++)
                {
                    var actual = rgb[(y * fixture.Width + x) * 3 + channel];
                    if (x > 0 && actual < previous)
                    {
                        failures.Add(
                            $"TIFF base monotonicity gate failed at ({x},{y}), " +
                            $"channel {channel}: {actual} < {previous}.");
                        return failures;
                    }

                    var expected = (ushort)Math.Round(
                        ToneLut.SrgbDecode(
                            fixture.SourceCodes[x] / (double)ushort.MaxValue) *
                        ushort.MaxValue,
                        MidpointRounding.AwayFromZero);
                    var deviation = Math.Abs((int)actual - expected);
                    maximumDeviation = Math.Max(maximumDeviation, deviation);
                    if (deviation != 0 && firstQuantizationFailure == null)
                    {
                        firstQuantizationFailure =
                            $"TIFF base analytic quantization gate failed at ({x},{y}), " +
                            $"channel {channel}: expected {expected}, observed {actual}";
                    }
                    previous = actual;
                }
            }
        }
        if (firstQuantizationFailure != null)
        {
            failures.Add(
                $"{firstQuantizationFailure}; maximum Q16 deviation " +
                $"{maximumDeviation}.");
        }
        return failures;
    }

    private static void RequireEqual<T>(T[] expected, T[] actual, string message)
        where T : IEquatable<T>
    {
        if (expected.Length != actual.Length)
        {
            throw new InvalidOperationException(
                $"{message} Lengths: {expected.Length} and {actual.Length}.");
        }
        for (var index = 0; index < expected.Length; index++)
        {
            if (!expected[index].Equals(actual[index]))
            {
                throw new InvalidOperationException(
                    $"{message} First difference at element {index}: " +
                    $"{expected[index]} versus {actual[index]}.");
            }
        }
    }
}
