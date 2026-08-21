using System.Diagnostics;
using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

internal static class PreviewExposureEstimator
{
    internal const int ComparisonMaxDimension = 48;
    internal const int MinimumPreviewDimension = 64;
    internal const double MaxMetadataDisagreementEv = 0.5;
    private const int TransferLutLength = ToneLut.Length;
    private const double MinimumLuminance = 1e-5;
    private static readonly Lazy<AgxCrossing> DefaultCrossing =
        new(() => new AgxCrossing(new AgxToneParameters(
            ExposureEv: 0,
            SourceExposureEv: 0,
            Contrast: 0,
            Highlights: 0,
            Shadows: 0,
            new CurveData())));
    private static readonly Lazy<ushort[]> SrgbDecodeLut =
        new(CreateSrgbDecodeLut);

    internal static double Estimate(
        byte[]? previewBytes,
        MagickImage basePixels,
        double fallbackEv,
        string filePath)
    {
        ArgumentNullException.ThrowIfNull(basePixels);
        var stopwatch = Stopwatch.StartNew();
        long decodeElapsed = 0;
        long normalizeElapsed = 0;
        long estimateElapsed = 0;
        uint previewWidth = 0;
        uint previewHeight = 0;
        try
        {
            if (previewBytes == null || previewBytes.Length == 0)
            {
                return fallbackEv;
            }

            var settings = new MagickReadSettings();
            if (IsJpeg(previewBytes))
            {
                BitmapConversionService.ApplyJpegSizeHint(settings, 128);
            }

            using var preview = new MagickImage(previewBytes, settings);
            previewWidth = preview.Width;
            previewHeight = preview.Height;
            decodeElapsed = stopwatch.ElapsedMilliseconds;
            NormalizePreview(preview);
            normalizeElapsed = stopwatch.ElapsedMilliseconds - decodeElapsed;
            if (preview.Width < MinimumPreviewDimension ||
                preview.Height < MinimumPreviewDimension)
            {
                return fallbackEv;
            }

            var previewEstimate = EstimatePrepared(
                basePixels,
                preview,
                previewIsSrgb: true);
            var result = SelectBias(previewEstimate, fallbackEv);
            if (previewEstimate is { } measured && result != measured)
            {
                ImageServiceHelpers.LogDebug(
                    nameof(PreviewExposureEstimator),
                    $"Preview bias {measured:F3} EV clamped to {result:F3} EV " +
                    $"near metadata bias {fallbackEv:F3} EV",
                    filePath);
            }
            estimateElapsed = stopwatch.ElapsedMilliseconds -
                decodeElapsed - normalizeElapsed;
            return result;
        }
        catch (Exception exception)
        {
            ImageServiceHelpers.LogDebug(
                nameof(PreviewExposureEstimator),
                $"Estimate failed: {exception.Message}",
                filePath);
            return fallbackEv;
        }
        finally
        {
            ImageServiceHelpers.LogPerformance(
                nameof(PreviewExposureEstimator),
                nameof(Estimate),
                stopwatch.ElapsedMilliseconds,
                filePath,
                $"decode={decodeElapsed};normalize={normalizeElapsed};" +
                $"solve={estimateElapsed};" +
                $"preview={previewWidth}x{previewHeight}");
        }
    }

    internal static double? EstimatePrepared(
        MagickImage baseLinear,
        MagickImage previewLinear) =>
        EstimatePrepared(baseLinear, previewLinear, previewIsSrgb: false);

    private static double? EstimatePrepared(
        MagickImage baseLinear,
        MagickImage preview,
        bool previewIsSrgb)
    {
        ArgumentNullException.ThrowIfNull(baseLinear);
        ArgumentNullException.ThrowIfNull(preview);
        var stopwatch = Stopwatch.StartNew();
        if (preview.Width < MinimumPreviewDimension ||
            preview.Height < MinimumPreviewDimension)
        {
            return null;
        }

        var baseArea = GetAlignedBaseArea(baseLinear, preview);
        var baseSamples = ReadReducedRgb(
            baseLinear,
            baseArea,
            decodeSrgb: false);
        var previewSamples = ReadReducedRgb(
            preview,
            SampleArea.Full(preview),
            decodeSrgb: previewIsSrgb);
        var sampleElapsed = stopwatch.ElapsedMilliseconds;
        var rawMedian = MedianLuminance(baseSamples);
        var previewMedian = MedianLuminance(previewSamples);
        if (!double.IsFinite(rawMedian) || !double.IsFinite(previewMedian) ||
            rawMedian < MinimumLuminance || previewMedian < MinimumLuminance)
        {
            return null;
        }

        var result = Solve(baseSamples, previewMedian);
        ImageServiceHelpers.LogPerformance(
            nameof(PreviewExposureEstimator),
            nameof(EstimatePrepared),
            stopwatch.ElapsedMilliseconds,
            extra: $"sample={sampleElapsed};" +
                $"solver={stopwatch.ElapsedMilliseconds - sampleElapsed}");
        return result;
    }

    internal static double? Solve(
        ReadOnlySpan<ushort> baseRgb,
        double previewMedian)
    {
        ValidateRgb(baseRgb);
        var rawMedian = MedianWorkingLuminance(baseRgb);
        if (!double.IsFinite(rawMedian) || !double.IsFinite(previewMedian) ||
            rawMedian < MinimumLuminance || previewMedian < MinimumLuminance)
        {
            return null;
        }

        var luminances = new double[baseRgb.Length / 3];
        var lowResponse = DefaultRenderMedian(
            baseRgb,
            -RawExposureBias.MaxAbsEv,
            luminances);
        var highResponse = DefaultRenderMedian(
            baseRgb,
            RawExposureBias.MaxAbsEv,
            luminances);
        if (previewMedian <= lowResponse)
        {
            return -RawExposureBias.MaxAbsEv;
        }

        if (previewMedian >= highResponse)
        {
            return RawExposureBias.MaxAbsEv;
        }

        var low = -RawExposureBias.MaxAbsEv;
        var high = RawExposureBias.MaxAbsEv;
        for (var iteration = 0; iteration < 40; iteration++)
        {
            var candidate = (low + high) / 2;
            var response = DefaultRenderMedian(
                baseRgb,
                candidate,
                luminances);
            if (Math.Abs(response - previewMedian) < 1e-6)
            {
                return candidate;
            }

            if (response < previewMedian)
            {
                low = candidate;
            }
            else
            {
                high = candidate;
            }
        }

        return (low + high) / 2;
    }

    internal static double SelectBias(
        double? previewEstimate,
        double metadataFallback)
    {
        if (previewEstimate is not { } measured || !double.IsFinite(measured))
        {
            return metadataFallback;
        }

        // Clamp instead of discarding: decodes of the same file vary by a few
        // hundredths of an EV (native demosaic is thread-nondeterministic), so
        // a hard accept/reject threshold flips the result by the full
        // disagreement for files that measure near the boundary.
        return metadataFallback == 0
            ? measured
            : Math.Clamp(
                measured,
                metadataFallback - MaxMetadataDisagreementEv,
                metadataFallback + MaxMetadataDisagreementEv);
    }

    internal static double DefaultRenderMedian(
        ReadOnlySpan<ushort> baseRgb,
        double exposureEv)
    {
        ValidateRgb(baseRgb);
        return DefaultRenderMedian(
            baseRgb,
            exposureEv,
            new double[baseRgb.Length / 3]);
    }

    internal static double MedianLuminance(ReadOnlySpan<ushort> linearRgb)
    {
        ValidateRgb(linearRgb);
        var luminances = new double[linearRgb.Length / 3];
        for (var pixel = 0; pixel < luminances.Length; pixel++)
        {
            var offset = pixel * 3;
            luminances[pixel] = Luminance(
                linearRgb[offset] / (double)ushort.MaxValue,
                linearRgb[offset + 1] / (double)ushort.MaxValue,
                linearRgb[offset + 2] / (double)ushort.MaxValue);
        }

        return Median(luminances);
    }

    private static double DefaultRenderMedian(
        ReadOnlySpan<ushort> baseRgb,
        double exposureEv,
        double[] luminances)
    {
        var crossing = DefaultCrossing.Value;
        for (var pixel = 0; pixel < luminances.Length; pixel++)
        {
            var offset = pixel * 3;
            var encoded = crossing.TransformAnalyticAtExposure(
                new AgxRgb(
                    baseRgb[offset] / (double)ushort.MaxValue,
                    baseRgb[offset + 1] / (double)ushort.MaxValue,
                    baseRgb[offset + 2] / (double)ushort.MaxValue),
                exposureEv);
            var displayRec2020 = (
                R: ToneLut.SrgbDecode(encoded.Red),
                G: ToneLut.SrgbDecode(encoded.Green),
                B: ToneLut.SrgbDecode(encoded.Blue));
            var display = Rec2020ToSrgb(displayRec2020);
            luminances[pixel] = Luminance(
                Math.Clamp(display.R, 0, 1),
                Math.Clamp(display.G, 0, 1),
                Math.Clamp(display.B, 0, 1));
        }

        return Median(luminances);
    }

    private static double MedianWorkingLuminance(ReadOnlySpan<ushort> workingRgb)
    {
        var luminances = new double[workingRgb.Length / 3];
        for (var pixel = 0; pixel < luminances.Length; pixel++)
        {
            var offset = pixel * 3;
            var display = WorkingToDisplay(
                workingRgb[offset],
                workingRgb[offset + 1],
                workingRgb[offset + 2]);
            luminances[pixel] = Luminance(
                Math.Max(display.R, 0),
                Math.Max(display.G, 0),
                Math.Max(display.B, 0));
        }

        return Median(luminances);
    }

    private static (double R, double G, double B) WorkingToDisplay(
        ushort red,
        ushort green,
        ushort blue)
    {
        var r = red / (double)ushort.MaxValue;
        var g = green / (double)ushort.MaxValue;
        var b = blue / (double)ushort.MaxValue;
        var matrix = RgbColorSpaceMatrices.LinearRec2020ToLinearSrgb;
        return (
            matrix[0, 0] * r + matrix[0, 1] * g + matrix[0, 2] * b,
            matrix[1, 0] * r + matrix[1, 1] * g + matrix[1, 2] * b,
            matrix[2, 0] * r + matrix[2, 1] * g + matrix[2, 2] * b);
    }

    private static (double R, double G, double B) Rec2020ToSrgb(
        (double R, double G, double B) value)
    {
        var matrix = RgbColorSpaceMatrices.LinearRec2020ToLinearSrgb;
        return (
            matrix[0, 0] * value.R + matrix[0, 1] * value.G +
                matrix[0, 2] * value.B,
            matrix[1, 0] * value.R + matrix[1, 1] * value.G +
                matrix[1, 2] * value.B,
            matrix[2, 0] * value.R + matrix[2, 1] * value.G +
                matrix[2, 2] * value.B);
    }

    private static void NormalizePreview(MagickImage preview)
    {
        if (preview.GetColorProfile() is { } profile)
        {
            preview.TransformColorSpace(profile, ColorProfiles.SRGB);
        }
        else
        {
            preview.ColorSpace = ColorSpace.sRGB;
        }

        preview.Depth = 16;
        preview.Strip();
    }

    internal static SampleArea GetAlignedBaseArea(
        MagickImage baseImage,
        MagickImage preview)
    {
        var difference = CropGeometry.RelativeAspectRatioDifference(
            preview.Width,
            preview.Height,
            baseImage.Width,
            baseImage.Height);
        if (difference is null or <= 0.02)
        {
            return SampleArea.Full(baseImage);
        }

        var crop = CropGeometry.CenterCropToAspect(
            baseImage.Width,
            baseImage.Height,
            preview.Width,
            preview.Height);
        if (crop == null)
        {
            return SampleArea.Full(baseImage);
        }

        return new SampleArea(
            crop.Value.X,
            crop.Value.Y,
            checked((int)crop.Value.Width),
            checked((int)crop.Value.Height));
    }

    private static ushort[] ReadReducedRgb(
        MagickImage source,
        SampleArea area,
        bool decodeSrgb)
    {
        var scale = Math.Min(
            1,
            ComparisonMaxDimension / (double)Math.Max(area.Width, area.Height));
        var width = Math.Max(1, (int)Math.Round(area.Width * scale));
        var height = Math.Max(1, (int)Math.Round(area.Height * scale));
        var result = new ushort[width * height * 3];
        using var pixels = source.GetPixelsUnsafe();
        for (var y = 0; y < height; y++)
        {
            var sourceY = area.Y + Math.Min(
                area.Height - 1,
                (int)((y + 0.5) * area.Height / height));
            for (var x = 0; x < width; x++)
            {
                var sourceX = area.X + Math.Min(
                    area.Width - 1,
                    (int)((x + 0.5) * area.Width / width));
                var pixel = pixels.GetPixel(sourceX, sourceY);
                var offset = (y * width + x) * 3;
                result[offset] = DecodeSample(pixel[0], decodeSrgb);
                result[offset + 1] = DecodeSample(pixel[1], decodeSrgb);
                result[offset + 2] = DecodeSample(pixel[2], decodeSrgb);
            }
        }

        return result;
    }

    private static ushort DecodeSample(ushort sample, bool decodeSrgb)
    {
        if (!decodeSrgb)
        {
            return sample;
        }

        var lut = SrgbDecodeLut.Value;
        var position = sample / (double)ushort.MaxValue * (lut.Length - 1);
        var lower = (int)position;
        var upper = Math.Min(lower + 1, lut.Length - 1);
        var fraction = position - lower;
        return (ushort)Math.Round(
            lut[lower] * (1 - fraction) + lut[upper] * fraction);
    }

    private static ushort[] CreateSrgbDecodeLut()
    {
        var result = new ushort[TransferLutLength];
        for (var index = 0; index < result.Length; index++)
        {
            var display = index / (double)(result.Length - 1);
            result[index] = (ushort)Math.Round(
                ToneLut.SrgbDecode(display) * ushort.MaxValue);
        }

        return result;
    }

    private static double Luminance(double red, double green, double blue) =>
        0.2126 * red + 0.7152 * green + 0.0722 * blue;

    private static double Median(double[] values)
    {
        Array.Sort(values);
        var middle = values.Length / 2;
        return values.Length % 2 != 0
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2;
    }

    private static void ValidateRgb(ReadOnlySpan<ushort> samples)
    {
        if (samples.Length == 0 || samples.Length % 3 != 0)
        {
            throw new ArgumentException(
                "RGB samples must contain one or more complete pixels.",
                nameof(samples));
        }
    }

    private static bool IsJpeg(ReadOnlySpan<byte> bytes) =>
        bytes.Length > 2 && bytes[0] == 0xff && bytes[1] == 0xd8;

    internal readonly record struct SampleArea(
        int X,
        int Y,
        int Width,
        int Height)
    {
        public static SampleArea Full(MagickImage image) =>
            new(0, 0, checked((int)image.Width), checked((int)image.Height));
    }
}
