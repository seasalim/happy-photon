using System.Buffers;
using System.Diagnostics;
using HappyPhoton.Models;
using ImageMagick;
using static HappyPhoton.Services.RenderKernelSupport;

namespace HappyPhoton.Services;

internal static class RenderSharpening
{
    private const double MinimumEffectiveSigma = 0.3;
    private const double CaptureNativeSigma = 0.75;
    private const double CaptureThreshold = 0.01;
    private const double OutputSigma = 0.5;
    private const double OutputAmount = 0.3;
    private const double OutputThreshold = 0.005;
    private const int MaximumOutputLongEdge = 2560;

    public static void ApplyCapture(
        MagickImage image,
        BaseImageInfo info,
        DetailSettings detail) =>
        ApplyCapture(
            image,
            info,
            detail,
            RenderDetail.DefaultBandPixelLimit);

    internal static void ApplyCapture(
        MagickImage image,
        BaseImageInfo info,
        DetailSettings detail,
        int bandPixelLimit)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            bandPixelLimit);

        var value = detail.ResolveCaptureSharpen(
            info.IsRawSource);
        var amount = Math.Clamp(value, 0, 100) / 100.0;
        var sigma = RenderDetail.CalculateEffectiveSigma(
            image,
            info,
            CaptureNativeSigma);
        if (amount <= 0 || sigma < MinimumEffectiveSigma)
        {
            return;
        }

        ApplyLuminance(
            image,
            sigma,
            amount,
            CaptureThreshold,
            bandPixelLimit);
    }

    public static void ApplyOutput(
        MagickImage image,
        bool enabled,
        bool wasResized) =>
        ApplyOutput(
            image,
            enabled,
            wasResized,
            RenderDetail.DefaultBandPixelLimit);

    internal static void ApplyOutput(
        MagickImage image,
        bool enabled,
        bool wasResized,
        int bandPixelLimit)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            bandPixelLimit);
        if (!enabled ||
            !wasResized ||
            Math.Max(image.Width, image.Height) > MaximumOutputLongEdge)
        {
            return;
        }

        ApplyLuminance(
            image,
            OutputSigma,
            OutputAmount,
            OutputThreshold,
            bandPixelLimit);
    }

    private static void ApplyLuminance(
        MagickImage image,
        double sigma,
        double amount,
        double threshold,
        int bandPixelLimit)
    {
        var stopwatch = Stopwatch.StartNew();
        using var pixels = image.GetPixels();
        var layout = GetLayout(pixels);
        var width = checked((int)image.Width);
        var height = checked((int)image.Height);
        var kernel = CreateGaussianKernel(sigma);
        var radius = kernel.Length / 2;
        var bandRows = Math.Min(
            height,
            Math.Max(1, bandPixelLimit / width));
        var horizontal = ArrayPool<float>.Shared.Rent(
            checked((bandRows + radius * 2) * width));
        var bandCount = 0;
        try
        {
            for (var bandStart = 0; bandStart < height;)
            {
                var outputRows = Math.Min(
                    bandRows,
                    height - bandStart);
                var bandEnd = bandStart + outputRows;
                var values = pixels.GetArea(
                    0,
                    bandStart,
                    image.Width,
                    checked((uint)outputRows)) ??
                    throw new InvalidOperationException(
                        "Unable to access Q16 pixels.");
                BlurHorizontalRows(
                    values,
                    horizontal,
                    width,
                    outputRows,
                    radius,
                    layout,
                    kernel,
                    radius);
                if (bandStart == 0)
                {
                    FillRows(
                        horizontal,
                        width,
                        sourceRow: radius,
                        destinationRow: 0,
                        count: radius);
                }

                var followingRows = Math.Min(
                    radius,
                    height - bandEnd);
                if (followingRows > 0)
                {
                    var following = pixels.GetArea(
                        0,
                        bandEnd,
                        image.Width,
                        checked((uint)followingRows)) ??
                        throw new InvalidOperationException(
                            "Unable to access Q16 pixels.");
                    BlurHorizontalRows(
                        following,
                        horizontal,
                        width,
                        followingRows,
                        radius + outputRows,
                        layout,
                        kernel,
                        radius);
                }

                FillRows(
                    horizontal,
                    width,
                    sourceRow: radius + outputRows +
                        followingRows - 1,
                    destinationRow: radius + outputRows +
                        followingRows,
                    count: radius - followingRows);
                SharpenVerticalBand(
                    values,
                    horizontal,
                    width,
                    outputRows,
                    layout,
                    kernel,
                    radius,
                    (float)amount,
                    (float)(threshold * ushort.MaxValue));
                if (bandEnd < height)
                {
                    horizontal.AsSpan(
                        outputRows * width,
                        radius * width).CopyTo(horizontal);
                }

                pixels.SetArea(
                    0,
                    bandStart,
                    image.Width,
                    checked((uint)outputRows),
                    values);
                bandStart = bandEnd;
                bandCount++;
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(horizontal);
        }

        ImageServiceHelpers.LogPerformance(
            nameof(RenderSharpening),
            nameof(ApplyLuminance),
            stopwatch.ElapsedMilliseconds,
            $"size={image.Width}x{image.Height}",
            $"sigma={sigma:F3};radius={radius};" +
            $"bands={bandCount};bandRows={bandRows}");
    }

    private static void BlurHorizontalRows(
        ushort[] source,
        float[] destination,
        int width,
        int rows,
        int destinationRowOffset,
        PixelLayout layout,
        float[] kernel,
        int radius)
    {
        var workers = Math.Min(Environment.ProcessorCount, rows);
        var rowsPerWorker = (rows + workers - 1) / workers;
        Parallel.For(0, workers, worker =>
        {
            var start = worker * rowsPerWorker;
            var end = Math.Min(rows, start + rowsPerWorker);
            for (var y = start; y < end; y++)
            {
                var sourceRow = y * width * layout.Channels;
                var targetRow =
                    (y + destinationRowOffset) * width;
                for (var x = 0; x < width; x++)
                {
                    float luma = 0;
                    for (var offset = -radius; offset <= radius; offset++)
                    {
                        var pixel = sourceRow +
                            Math.Clamp(x + offset, 0, width - 1) *
                            layout.Channels;
                        luma += GetLuma(
                            source[pixel + layout.Red],
                            source[pixel + layout.Green],
                            source[pixel + layout.Blue]) *
                            kernel[offset + radius];
                    }

                    destination[targetRow + x] = luma;
                }
            }
        });
    }

    private static void FillRows(
        float[] values,
        int width,
        int sourceRow,
        int destinationRow,
        int count)
    {
        for (var row = 0; row < count; row++)
        {
            values.AsSpan(sourceRow * width, width).CopyTo(
                values.AsSpan(
                    (destinationRow + row) * width,
                    width));
        }
    }

    private static void SharpenVerticalBand(
        ushort[] values,
        float[] horizontal,
        int width,
        int rows,
        PixelLayout layout,
        float[] kernel,
        int radius,
        float amount,
        float threshold)
    {
        var workers = Math.Min(Environment.ProcessorCount, rows);
        var rowsPerWorker = (rows + workers - 1) / workers;
        Parallel.For(0, workers, worker =>
        {
            var start = worker * rowsPerWorker;
            var end = Math.Min(rows, start + rowsPerWorker);
            for (var y = start; y < end; y++)
            {
                var pixel = y * width * layout.Channels;
                for (var x = 0; x < width; x++)
                {
                    float blurred = 0;
                    for (var offset = -radius; offset <= radius; offset++)
                    {
                        var sourceRow =
                            (y + radius + offset) * width;
                        blurred += horizontal[sourceRow + x] *
                            kernel[offset + radius];
                    }

                    var luma = GetLuma(
                        values[pixel + layout.Red],
                        values[pixel + layout.Green],
                        values[pixel + layout.Blue]);
                    var difference = luma - blurred;
                    if (Math.Abs(difference) >= threshold)
                    {
                        var adjustment = difference * amount;
                        values[pixel + layout.Red] = ToQuantum(
                            values[pixel + layout.Red] + adjustment);
                        values[pixel + layout.Green] = ToQuantum(
                            values[pixel + layout.Green] + adjustment);
                        values[pixel + layout.Blue] = ToQuantum(
                            values[pixel + layout.Blue] + adjustment);
                    }

                    pixel += layout.Channels;
                }
            }
        });
    }

    private static float[] CreateGaussianKernel(double sigma)
    {
        var radius = Math.Max(1, (int)Math.Ceiling(sigma * 3));
        var kernel = new float[radius * 2 + 1];
        double total = 0;
        for (var offset = -radius; offset <= radius; offset++)
        {
            var weight = Math.Exp(
                -(offset * offset) / (2 * sigma * sigma));
            kernel[offset + radius] = (float)weight;
            total += weight;
        }

        for (var index = 0; index < kernel.Length; index++)
        {
            kernel[index] = (float)(kernel[index] / total);
        }

        return kernel;
    }

}
