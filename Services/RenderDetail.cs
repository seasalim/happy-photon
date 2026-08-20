using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using HappyPhoton.Models;
using ImageMagick;
using static HappyPhoton.Services.RenderKernelSupport;

namespace HappyPhoton.Services;

internal static class RenderDetail
{
    private const double MinimumEffectiveSigma = 0.3;
    private const double MaximumChromaSigma = 2.0;
    internal const int DefaultBandPixelLimit = 8_000_000;

    public static void Apply(
        MagickImage image,
        BaseImageInfo info,
        DetailSettings settings) =>
        Apply(image, info, settings, DefaultBandPixelLimit);

    internal static void Apply(
        MagickImage image,
        BaseImageInfo info,
        DetailSettings settings,
        int bandPixelLimit)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bandPixelLimit);

        var nativeSigma = Math.Clamp(settings.ChromaNr, 0, 100) /
            100.0 * MaximumChromaSigma;
        var sigma = CalculateEffectiveSigma(image, info, nativeSigma);
        if (sigma < MinimumEffectiveSigma)
        {
            return;
        }

        var blur = CreateBoxBlur(sigma);
        ApplyBanded(image, blur, bandPixelLimit);
    }

    internal static double CalculateEffectiveSigma(
        MagickImage image,
        BaseImageInfo info,
        double nativeSigma)
    {
        var nativeLongEdge = Math.Max(info.FullWidth, info.FullHeight);
        if (nativeSigma <= 0 || nativeLongEdge <= 0)
        {
            return 0;
        }

        var renderLongEdge = Math.Max(image.Width, image.Height);
        return nativeSigma * renderLongEdge / nativeLongEdge;
    }

    private static void ApplyBanded(
        MagickImage image,
        BoxBlurParameters blur,
        int bandPixelLimit)
    {
        var stopwatch = Stopwatch.StartNew();
        using var sourcePixels = image.GetPixels();
        var layout = GetLayout(sourcePixels);
        var width = checked((int)image.Width);
        var height = checked((int)image.Height);
        var bandRows = Math.Min(
            height,
            Math.Max(blur.Radius, bandPixelLimit / width));
        var horizontalRows = Math.Min(
            height + blur.Radius,
            checked(bandRows + blur.Radius * 2 + 1));
        var verticalCb = new float[width];
        var verticalCr = new float[width];
        var horizontal = ArrayPool<float>.Shared.Rent(
            checked(horizontalRows * width * 2));
        var bandCount = 0;
        try
        {
            for (var bandStart = 0; bandStart < height;)
            {
                var outputRows = Math.Min(bandRows, height - bandStart);
                var bandEnd = bandStart + outputRows;
                var values = sourcePixels.GetArea(
                    0,
                    bandStart,
                    image.Width,
                    checked((uint)outputRows)) ??
                    throw new InvalidOperationException(
                        "Unable to access Q16 pixels.");
                var horizontalRowOffset = bandStart == 0
                    ? 0
                    : blur.Radius;
                BlurHorizontalRows(
                    values,
                    horizontal,
                    width,
                    outputRows,
                    horizontalRowOffset,
                    layout,
                    blur.Radius);
                var upperRows = Math.Min(
                    blur.Radius + 1,
                    height - bandEnd);
                if (upperRows > 0)
                {
                    var upper = sourcePixels.GetArea(
                        0,
                        bandEnd,
                        image.Width,
                        checked((uint)upperRows)) ??
                        throw new InvalidOperationException(
                            "Unable to access Q16 pixels.");
                    BlurHorizontalRows(
                        upper,
                        horizontal,
                        width,
                        upperRows,
                        horizontalRowOffset + outputRows,
                        layout,
                        blur.Radius);
                }
                var horizontalBaseRow = bandStart == 0
                    ? 0
                    : bandStart - blur.Radius;
                if (blur.Radius == 1)
                {
                    BlurVerticalRadiusOneBand(
                        values,
                        horizontal,
                        width,
                        height,
                        bandStart,
                        outputRows,
                        horizontalBaseRow,
                        layout,
                        blur.Strength);
                }
                else
                {
                    BlurVerticalBand(
                        values,
                        horizontal,
                        verticalCb,
                        verticalCr,
                        width,
                        height,
                        bandStart,
                        outputRows,
                        horizontalBaseRow,
                        layout,
                        blur);
                }

                sourcePixels.SetArea(
                    0,
                    bandStart,
                    image.Width,
                    checked((uint)outputRows),
                    values);
                if (bandEnd < height)
                {
                    var carryStart = bandEnd - blur.Radius -
                        horizontalBaseRow;
                    horizontal.AsSpan(
                        carryStart * width * 2,
                        blur.Radius * width * 2).CopyTo(horizontal);
                }
                bandStart = bandEnd;
                bandCount++;
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(horizontal);
        }

        ImageServiceHelpers.LogPerformance(
            nameof(RenderDetail),
            nameof(ApplyBanded),
            stopwatch.ElapsedMilliseconds,
            $"size={image.Width}x{image.Height}",
            $"channels={layout.Channels};radius={blur.Radius};" +
            $"bands={bandCount};bandRows={bandRows}");
    }

    private static void BlurHorizontalRows(
        ushort[] values,
        float[] horizontal,
        int width,
        int rows,
        int destinationRowOffset,
        PixelLayout layout,
        int radius)
    {
        var workers = Math.Min(Environment.ProcessorCount, rows);
        var rowsPerWorker = (rows + workers - 1) / workers;
        Parallel.For(0, workers, ProcessWorker);

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        void ProcessWorker(int worker)
        {
            var start = worker * rowsPerWorker;
            var end = Math.Min(rows, start + rowsPerWorker);
            for (var row = start; row < end; row++)
            {
                BlurHorizontalBox(
                    values.AsSpan(row * width * layout.Channels),
                    horizontal.AsSpan(
                        (row + destinationRowOffset) * width * 2,
                        width * 2),
                    width,
                    layout,
                    radius);
            }
        }
    }

    private static void BlurVerticalBand(
        ushort[] output,
        float[] source,
        float[] verticalCb,
        float[] verticalCr,
        int width,
        int height,
        int bandStart,
        int outputRows,
        int sourceBaseRow,
        PixelLayout layout,
        BoxBlurParameters blur)
    {
        var window = blur.Radius * 2 + 1;
        var workers = Math.Min(Environment.ProcessorCount, width);
        var columnsPerWorker = (width + workers - 1) / workers;
        Parallel.For(0, workers, ProcessWorker);

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        void ProcessWorker(int worker)
        {
            var start = worker * columnsPerWorker;
            var end = Math.Min(width, start + columnsPerWorker);
            if (bandStart == 0)
            {
                for (var x = start; x < end; x++)
                {
                    float cb = 0;
                    float cr = 0;
                    for (var offset = -blur.Radius;
                         offset <= blur.Radius;
                         offset++)
                    {
                        var y = Math.Clamp(offset, 0, height - 1);
                        var pixel = (y * width + x) * 2;
                        cb += source[pixel];
                        cr += source[pixel + 1];
                    }
                    verticalCb[x] = cb;
                    verticalCr[x] = cr;
                }
            }

            for (var outputRow = 0; outputRow < outputRows; outputRow++)
            {
                var y = bandStart + outputRow;
                var remove = y > blur.Radius
                    ? y - blur.Radius
                    : 0;
                var add = y + blur.Radius + 1 < height
                    ? y + blur.Radius + 1
                    : height - 1;
                var removePixel =
                    ((remove - sourceBaseRow) * width + start) * 2;
                var addPixel =
                    ((add - sourceBaseRow) * width + start) * 2;
                var outputPixel =
                    (outputRow * width + start) * layout.Channels;
                for (var x = start; x < end; x++)
                {
                    var cb = verticalCb[x];
                    var cr = verticalCr[x];
                    ReconstructPixel(
                        output,
                        outputPixel,
                        layout,
                        cb / window,
                        cr / window,
                        blur.Strength);
                    if (y < height - 1)
                    {
                        cb += source[addPixel] - source[removePixel];
                        cr += source[addPixel + 1] -
                            source[removePixel + 1];
                        verticalCb[x] = cb;
                        verticalCr[x] = cr;
                    }
                    removePixel += 2;
                    addPixel += 2;
                    outputPixel += layout.Channels;
                }
            }
        }
    }

    private static void BlurVerticalRadiusOneBand(
        ushort[] output,
        float[] source,
        int width,
        int height,
        int bandStart,
        int outputRows,
        int sourceBaseRow,
        PixelLayout layout,
        float strength)
    {
        var workers = Math.Min(Environment.ProcessorCount, outputRows);
        var rowsPerWorker = (outputRows + workers - 1) / workers;
        Parallel.For(0, workers, worker =>
        {
            var start = worker * rowsPerWorker;
            var end = Math.Min(outputRows, start + rowsPerWorker);
            for (var outputRow = start; outputRow < end; outputRow++)
            {
                var y = bandStart + outputRow;
                var previousRow =
                    (Math.Max(0, y - 1) - sourceBaseRow) * width * 2;
                var currentRow = (y - sourceBaseRow) * width * 2;
                var nextRow =
                    (Math.Min(height - 1, y + 1) - sourceBaseRow) *
                    width * 2;
                var outputPixel = outputRow * width * layout.Channels;
                for (var x = 0; x < width; x++)
                {
                    var offset = x * 2;
                    var cb = (
                        source[previousRow + offset] +
                        source[currentRow + offset] +
                        source[nextRow + offset]) / 3;
                    var cr = (
                        source[previousRow + offset + 1] +
                        source[currentRow + offset + 1] +
                        source[nextRow + offset + 1]) / 3;
                    ReconstructPixel(
                        output,
                        outputPixel,
                        layout,
                        cb,
                        cr,
                        strength);
                    outputPixel += layout.Channels;
                }
            }
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void BlurHorizontalBox(
        ReadOnlySpan<ushort> source,
        Span<float> destination,
        int width,
        PixelLayout layout,
        int radius)
    {
        var window = radius * 2 + 1;
        float cb = 0;
        float cr = 0;
        for (var offset = -radius; offset <= radius; offset++)
        {
            AddChroma(
                source,
                Math.Clamp(offset, 0, width - 1),
                layout,
                1,
                ref cb,
                ref cr);
        }
        for (var x = 0; x < width; x++)
        {
            var target = x * 2;
            destination[target] = (float)(cb / window);
            destination[target + 1] = (float)(cr / window);
            AddChroma(
                source,
                x > radius ? x - radius : 0,
                layout,
                -1,
                ref cb,
                ref cr);
            AddChroma(
                source,
                x + radius + 1 < width
                    ? x + radius + 1
                    : width - 1,
                layout,
                1,
                ref cb,
                ref cr);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void AddChroma(
        ReadOnlySpan<ushort> source,
        int x,
        PixelLayout layout,
        int direction,
        ref float cb,
        ref float cr)
    {
        var pixel = x * layout.Channels;
        var r = source[pixel + layout.Red];
        var g = source[pixel + layout.Green];
        var b = source[pixel + layout.Blue];
        var luma = GetLuma(r, g, b);
        cb += (b - luma) * direction;
        cr += (r - luma) * direction;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ReconstructPixel(
        Span<ushort> output,
        int pixel,
        PixelLayout layout,
        float cb,
        float cr,
        float strength)
    {
        var luma = GetLuma(
            output[pixel + layout.Red],
            output[pixel + layout.Green],
            output[pixel + layout.Blue]);
        cb = (output[pixel + layout.Blue] - luma) * (1 - strength) +
             cb * strength;
        cr = (output[pixel + layout.Red] - luma) * (1 - strength) +
             cr * strength;
        var r = luma + cr;
        var b = luma + cb;
        var g = (float)((luma - Rec2020Luminance.Red * r -
            Rec2020Luminance.Blue * b) / Rec2020Luminance.Green);
        output[pixel + layout.Red] = ToQuantum(r);
        output[pixel + layout.Green] = ToQuantum(g);
        output[pixel + layout.Blue] = ToQuantum(b);
    }

    private static BoxBlurParameters CreateBoxBlur(double sigma)
    {
        var radius = Math.Max(
            1,
            (int)Math.Round((Math.Sqrt(1 + 12 * sigma * sigma) - 1) / 2));
        var variance = radius * (radius + 1) / 3.0;
        return new BoxBlurParameters(
            radius,
            (float)Math.Min(1, sigma * sigma / variance));
    }

    private readonly record struct BoxBlurParameters(
        int Radius,
        float Strength);
}
