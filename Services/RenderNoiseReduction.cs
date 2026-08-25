using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using HappyPhoton.Models;
using ImageMagick;
using static HappyPhoton.Services.RenderKernelSupport;

namespace HappyPhoton.Services;

internal static partial class RenderNoiseReduction
{
    // Four wavelet float planes make the sibling limit peak at 261.5 MiB on G2;
    // quarter-size bands measured 74.9 MiB and stay below its 150 MiB ceiling.
    private const int NoiseReductionBandPixelLimit =
        RenderDetail.DefaultBandPixelLimit / 4;

    internal static void Apply(
        MagickImage image,
        BaseImageInfo info,
        DetailSettings settings,
        int bandPixelLimit = NoiseReductionBandPixelLimit)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bandPixelLimit);
        var amount = SliderAmount(settings.LuminanceNr);
        if (amount <= 0)
        {
            return;
        }

        var scales = ResolveScales(image, info, amount);
        if (scales.Length == 0)
        {
            return;
        }

        ApplyBanded(image, scales, bandPixelLimit);
    }

    internal static void ApplyResting(
        MagickImage image,
        BaseImageInfo info,
        DetailSettings settings,
        RenderExecutionOptions execution)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(settings);
        execution.ThrowIfCancellationRequested();
        var amount = SliderAmount(settings.LuminanceNr);
        if (amount <= 0)
        {
            return;
        }

        var scales = ResolveScales(image, info, amount);
        if (scales.Length == 0)
        {
            return;
        }

        ApplyBanded(
            image,
            scales,
            NoiseReductionBandPixelLimit,
            execution);
    }

    private static float SliderAmount(int value)
        => Math.Clamp(value, 0, 100) / 100f;

    private static void ApplyBanded(
        MagickImage image,
        WaveletScale[] scales,
        int bandPixelLimit,
        RenderExecutionOptions? execution = null)
    {
        var stopwatch = Stopwatch.StartNew();
        using var pixels = image.GetPixels();
        var layout = GetLayout(pixels);
        var width = checked((int)image.Width);
        var height = checked((int)image.Height);
        var halo = scales.Sum(scale => scale.SupportRadius);
        var bandRows = Math.Min(
            height,
            Math.Max(halo, bandPixelLimit / width));
        var upperCarry = new ushort[checked(
            halo * width * layout.Channels)];
        var maximumSourceRows = Math.Min(height, bandRows + halo * 2);
        var source = ArrayPool<ushort>.Shared.Rent(checked(
            maximumSourceRows * width * layout.Channels));
        var hasUpperCarry = false;
        var bandCount = 0;
        try
        {
            for (var bandStart = 0; bandStart < height;)
            {
                execution?.ThrowIfCancellationRequested();
                var outputRows = Math.Min(bandRows, height - bandStart);
                var bandEnd = bandStart + outputRows;
                var sourceStart = Math.Max(0, bandStart - halo);
                var sourceEnd = Math.Min(height, bandEnd + halo);
                var sourceRows = sourceEnd - sourceStart;
                var sourceSampleCount = checked(
                    sourceRows * width * layout.Channels);
                pixels.GetReadOnlyArea(
                    0,
                    sourceStart,
                    image.Width,
                    checked((uint)sourceRows))
                    .CopyTo(source.AsSpan(0, sourceSampleCount));
                if (hasUpperCarry)
                {
                    upperCarry.CopyTo(source, 0);
                }
                if (bandEnd < height)
                {
                    var carryOffset = checked(
                        (bandEnd - halo - sourceStart) *
                        width * layout.Channels);
                    source.AsSpan(carryOffset, upperCarry.Length)
                        .CopyTo(upperCarry);
                    hasUpperCarry = true;
                }

                var sampleCount = checked(sourceRows * width);
                var current = ArrayPool<float>.Shared.Rent(sampleCount);
                var horizontal = ArrayPool<float>.Shared.Rent(sampleCount);
                var next = ArrayPool<float>.Shared.Rent(sampleCount);
                var outputCount = checked(outputRows * width);
                var adjustment = ArrayPool<float>.Shared.Rent(outputCount);
                try
                {
                    adjustment.AsSpan(0, outputCount).Clear();
                    LoadLuma(
                        source,
                        current,
                        width,
                        sourceRows,
                        layout,
                        execution);
                    var validStart = 0;
                    var validEnd = sourceRows;
                    for (var scaleIndex = 0;
                         scaleIndex < scales.Length;
                         scaleIndex++)
                    {
                        var scale = scales[scaleIndex];
                        execution?.ThrowIfCancellationRequested();
                        BlurHorizontal(
                            current,
                            horizontal,
                            width,
                            validStart,
                            validEnd,
                            scale.Dilation,
                            execution);
                        var nextValidStart = sourceStart == 0
                            ? 0
                            : validStart + scale.SupportRadius;
                        var nextValidEnd = sourceEnd == height
                            ? sourceRows
                            : validEnd - scale.SupportRadius;
                        BlurVerticalAndAccumulate(
                            current,
                            horizontal,
                            next,
                            adjustment,
                            width,
                            validStart,
                            validEnd,
                            nextValidStart,
                            nextValidEnd,
                            bandStart - sourceStart,
                            outputRows,
                            scale,
                            source,
                            layout,
                            scaleIndex == scales.Length - 1,
                            execution);
                        (current, next) = (next, current);
                        validStart = nextValidStart;
                        validEnd = nextValidEnd;
                    }

                    var coreSamples = checked(
                        outputCount * layout.Channels);
                    var coreOffset = checked(
                        (bandStart - sourceStart) * width * layout.Channels);
                    source.AsSpan(coreOffset, coreSamples)
                        .CopyTo(source);

                    execution?.ThrowIfCancellationRequested();
                    pixels.SetArea(
                        0,
                        bandStart,
                        image.Width,
                        checked((uint)outputRows),
                        source.AsSpan(0, coreSamples));
                }
                finally
                {
                    ArrayPool<float>.Shared.Return(current);
                    ArrayPool<float>.Shared.Return(horizontal);
                    ArrayPool<float>.Shared.Return(next);
                    ArrayPool<float>.Shared.Return(adjustment);
                }

                bandStart = bandEnd;
                bandCount++;
            }
        }
        finally
        {
            ArrayPool<ushort>.Shared.Return(source);
        }

        ImageServiceHelpers.LogPerformance(
            nameof(RenderNoiseReduction),
            nameof(ApplyBanded),
            stopwatch.ElapsedMilliseconds,
            $"size={image.Width}x{image.Height}",
            $"scales={scales.Length};halo={halo};" +
            $"bands={bandCount};bandRows={bandRows}");
    }

    private static void LoadLuma(
        ushort[] source,
        float[] destination,
        int width,
        int rows,
        PixelLayout layout,
        RenderExecutionOptions? execution)
    {
        ForRows(rows, execution, (start, end) =>
        {
            for (var y = start; y < end; y++)
            {
                var pixel = y * width * layout.Channels;
                var target = y * width;
                for (var x = 0; x < width; x++)
                {
                    destination[target++] = GetLuma(
                        source[pixel + layout.Red],
                        source[pixel + layout.Green],
                        source[pixel + layout.Blue]);
                    pixel += layout.Channels;
                }
            }
        });
    }

    private static void BlurHorizontal(
        float[] source,
        float[] destination,
        int width,
        int startRow,
        int endRow,
        int dilation,
        RenderExecutionOptions? execution)
    {
        ForRows(endRow - startRow, execution, (start, end) =>
        {
            for (var y = start + startRow; y < end + startRow; y++)
            {
                var row = y * width;
                var interiorStart = Math.Min(width, 2 * dilation);
                var interiorEnd = Math.Max(interiorStart, width - 2 * dilation);
                for (var x = 0; x < interiorStart; x++)
                {
                    destination[row + x] = B3(
                        source,
                        row + Math.Max(0, x - 2 * dilation),
                        row + Math.Max(0, x - dilation),
                        row + x,
                        row + Math.Min(width - 1, x + dilation),
                        row + Math.Min(width - 1, x + 2 * dilation));
                }
                for (var x = interiorStart; x < interiorEnd; x++)
                {
                    destination[row + x] = B3(
                        source,
                        row + x - 2 * dilation,
                        row + x - dilation,
                        row + x,
                        row + x + dilation,
                        row + x + 2 * dilation);
                }
                for (var x = interiorEnd; x < width; x++)
                {
                    destination[row + x] = B3(
                        source,
                        row + Math.Max(0, x - 2 * dilation),
                        row + Math.Max(0, x - dilation),
                        row + x,
                        row + Math.Min(width - 1, x + dilation),
                        row + Math.Min(width - 1, x + 2 * dilation));
                }
            }
        });
    }

    private static void BlurVerticalAndAccumulate(
        float[] previous,
        float[] horizontal,
        float[] next,
        float[] adjustment,
        int width,
        int sourceStartRow,
        int sourceEndRow,
        int startRow,
        int endRow,
        int coreStartRow,
        int coreRows,
        WaveletScale scale,
        ushort[] pixels,
        PixelLayout layout,
        bool applyAdjustment,
        RenderExecutionOptions? execution)
    {
        var coreEndRow = coreStartRow + coreRows;
        ForRows(endRow - startRow, execution, (start, end) =>
        {
            var workerStart = start + startRow;
            var workerEnd = end + startRow;
            BlurVerticalRows(horizontal, next, width, sourceStartRow,
                sourceEndRow, workerStart, Math.Min(workerEnd, coreStartRow), scale);
            BlurVerticalCore(previous, horizontal, next, adjustment, width,
                sourceStartRow, sourceEndRow, Math.Max(workerStart, coreStartRow),
                Math.Min(workerEnd, coreEndRow), coreStartRow, scale, pixels,
                layout, applyAdjustment);
            BlurVerticalRows(horizontal, next, width, sourceStartRow,
                sourceEndRow, Math.Max(workerStart, coreEndRow), workerEnd, scale);
        });
    }

    private static void BlurVerticalRows(
        float[] horizontal, float[] next, int width,
        int sourceStartRow, int sourceEndRow, int startRow, int endRow,
        WaveletScale scale)
    {
        for (var y = startRow; y < endRow; y++)
        {
            var target = y * width;
            var row0 = Math.Max(sourceStartRow, y - 2 * scale.Dilation) * width;
            var row1 = Math.Max(sourceStartRow, y - scale.Dilation) * width;
            var row3 = Math.Min(sourceEndRow - 1, y + scale.Dilation) * width;
            var row4 = Math.Min(sourceEndRow - 1, y + 2 * scale.Dilation) * width;
            for (var x = 0; x < width; x++)
            {
                next[target + x] = B3(horizontal, row0 + x, row1 + x,
                    target + x, row3 + x, row4 + x);
            }
        }
    }

    private static void BlurVerticalCore(
        float[] previous, float[] horizontal, float[] next, float[] adjustment,
        int width, int sourceStartRow, int sourceEndRow, int startRow, int endRow,
        int coreStartRow, WaveletScale scale, ushort[] pixels,
        PixelLayout layout, bool applyAdjustment)
    {
        for (var y = startRow; y < endRow; y++)
        {
            var target = y * width;
            var row0 = Math.Max(sourceStartRow, y - 2 * scale.Dilation) * width;
            var row1 = Math.Max(sourceStartRow, y - scale.Dilation) * width;
            var row3 = Math.Min(sourceEndRow - 1, y + scale.Dilation) * width;
            var row4 = Math.Min(sourceEndRow - 1, y + 2 * scale.Dilation) * width;
            var adjustmentIndex = (y - coreStartRow) * width;
            for (var x = 0; x < width; x++, adjustmentIndex++)
            {
                var blurred = B3(horizontal, row0 + x, row1 + x,
                    target + x, row3 + x, row4 + x);
                next[target + x] = blurred;
                var detail = previous[target + x] - blurred;
                adjustment[adjustmentIndex] -= Math.Clamp(
                    detail, -scale.Threshold, scale.Threshold);
                if (applyAdjustment)
                {
                    ApplyAdjustment(pixels, (target + x) * layout.Channels,
                        adjustment[adjustmentIndex], layout);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyAdjustment(
        ushort[] output,
        int pixel,
        float adjustment,
        PixelLayout layout)
    {
        var red = output[pixel + layout.Red];
        var green = output[pixel + layout.Green];
        var blue = output[pixel + layout.Blue];
        var delta = Math.Clamp(
            adjustment,
            -Math.Min(red, Math.Min(green, blue)),
            ushort.MaxValue - Math.Max(red, Math.Max(green, blue)));
        output[pixel + layout.Red] = ToQuantum(red + delta);
        output[pixel + layout.Green] = ToQuantum(green + delta);
        output[pixel + layout.Blue] = ToQuantum(blue + delta);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float B3(
        float[] source,
        int first,
        int second,
        int center,
        int fourth,
        int fifth) =>
        (source[first] + 4 * source[second] + 6 * source[center] +
         4 * source[fourth] + source[fifth]) * (1f / 16);

    private static void ForRows(
        int rows,
        RenderExecutionOptions? execution,
        Action<int, int> action)
    {
        if (rows <= 0)
        {
            return;
        }

        var workers = Math.Min(Environment.ProcessorCount, rows);
        if (execution is { } bounded)
        {
            workers = bounded.CapWorkers(workers);
        }
        var rowsPerWorker = (rows + workers - 1) / workers;
        Action<int> process = worker => action(
            worker * rowsPerWorker,
            Math.Min(rows, (worker + 1) * rowsPerWorker));
        if (execution is { } options)
        {
            Parallel.For(0, workers, options.ParallelOptions, process);
        }
        else
        {
            Parallel.For(0, workers, process);
        }
    }

    internal readonly record struct WaveletScale(
        int Dilation,
        float Threshold)
    {
        internal int SupportRadius => checked(Dilation * 2);
    }
}
