using System.Diagnostics;
using ImageMagick;

namespace HappyPhoton.Services;

internal static class PreviewExposureEstimator
{
    internal const int ComparisonMaxDimension = 48;
    internal const int MinimumPreviewDimension = 64;
    internal const double MaxMetadataDisagreementEv = 0.5;
    private const int TransferLutLength = 4096;
    private const double MinimumLuminance = 1e-5;
    private static readonly Lazy<double[]> DefaultTransferLut =
        new(CreateDefaultTransferLut);
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
                    $"Preview bias {measured:F3} EV rejected in favor of " +
                    $"metadata bias {fallbackEv:F3} EV",
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
        var rawMedian = MedianLuminance(baseRgb);
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

        return metadataFallback != 0 &&
            Math.Abs(measured - metadataFallback) > MaxMetadataDisagreementEv
                ? metadataFallback
                : measured;
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
        var gain = Math.Pow(2, exposureEv);
        var transfer = DefaultTransferLut.Value;
        for (var pixel = 0; pixel < luminances.Length; pixel++)
        {
            var offset = pixel * 3;
            luminances[pixel] = Luminance(
                Map(baseRgb[offset], gain, transfer),
                Map(baseRgb[offset + 1], gain, transfer),
                Map(baseRgb[offset + 2], gain, transfer));
        }

        return Median(luminances);
    }

    private static double[] CreateDefaultTransferLut()
    {
        var result = new double[TransferLutLength];
        for (var index = 0; index < result.Length; index++)
        {
            var linear = index / (double)(result.Length - 1);
            var display = ToneLut.SrgbEncode(linear);
            result[index] = ToneLut.SrgbDecode(ToneLut.BaseLook(display));
        }

        return result;
    }

    private static double Map(
        ushort sample,
        double gain,
        double[] transfer)
    {
        var exposed = Math.Min(sample / (double)ushort.MaxValue * gain, 1);
        var position = exposed * (transfer.Length - 1);
        var lower = (int)position;
        var upper = Math.Min(lower + 1, transfer.Length - 1);
        var fraction = position - lower;
        return transfer[lower] * (1 - fraction) + transfer[upper] * fraction;
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

    private static SampleArea GetAlignedBaseArea(
        MagickImage baseImage,
        MagickImage preview)
    {
        var baseRatio = AspectRatio(baseImage);
        var previewRatio = AspectRatio(preview);
        if (Math.Abs(baseRatio - previewRatio) / previewRatio <= 0.02)
        {
            return SampleArea.Full(baseImage);
        }

        uint width;
        uint height;
        if (baseImage.Width >= baseImage.Height)
        {
            width = baseRatio > previewRatio
                ? checked((uint)Math.Round(baseImage.Height * previewRatio))
                : baseImage.Width;
            height = baseRatio > previewRatio
                ? baseImage.Height
                : checked((uint)Math.Round(baseImage.Width / previewRatio));
        }
        else
        {
            height = baseRatio > previewRatio
                ? checked((uint)Math.Round(baseImage.Width * previewRatio))
                : baseImage.Height;
            width = baseRatio > previewRatio
                ? baseImage.Width
                : checked((uint)Math.Round(baseImage.Height / previewRatio));
        }

        width = Math.Clamp(width, 1u, baseImage.Width);
        height = Math.Clamp(height, 1u, baseImage.Height);
        return new SampleArea(
            checked((int)((baseImage.Width - width) / 2)),
            checked((int)((baseImage.Height - height) / 2)),
            checked((int)width),
            checked((int)height));
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

    private static double AspectRatio(MagickImage image) =>
        Math.Max(image.Width, image.Height) /
        (double)Math.Min(image.Width, image.Height);

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

    private readonly record struct SampleArea(
        int X,
        int Y,
        int Width,
        int Height)
    {
        public static SampleArea Full(MagickImage image) =>
            new(0, 0, checked((int)image.Width), checked((int)image.Height));
    }
}
