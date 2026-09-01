using System.Numerics;
using static HappyPhoton.Services.RenderKernelSupport;

namespace HappyPhoton.Services;

internal static partial class RenderNoiseReduction
{
    private static void ProcessLumaPass(
        ushort[] pixels,
        float[] current,
        float[] horizontal,
        float[] adjustment,
        int width,
        int height,
        int sourceStart,
        int sourceEnd,
        int bandStart,
        int outputRows,
        PixelLayout pixelLayout,
        WaveletScale[] scales,
        RenderExecutionOptions? execution)
    {
        adjustment.AsSpan(0, checked(outputRows * width)).Clear();
        var sourceRows = sourceEnd - sourceStart;
        var validStart = 0;
        var validEnd = sourceRows;
        for (var scaleIndex = 0; scaleIndex < scales.Length; scaleIndex++)
        {
            var scale = scales[scaleIndex];
            execution?.ThrowIfCancellationRequested();
            BlurLumaHorizontal(
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
            BlurLumaVerticalAndAccumulate(
                current,
                horizontal,
                adjustment,
                width,
                validStart,
                validEnd,
                nextValidStart,
                nextValidEnd,
                bandStart - sourceStart,
                outputRows,
                scale,
                pixels,
                pixelLayout,
                scaleIndex == scales.Length - 1,
                execution);
            validStart = nextValidStart;
            validEnd = nextValidEnd;
        }
    }

    private static void BlurLumaHorizontal(
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
                BlurHorizontalRow(
                    source,
                    destination,
                    y * width,
                    width,
                    dilation);
            }
        });
    }

    private static void BlurHorizontalRow(
        float[] source,
        float[] destination,
        int row,
        int width,
        int dilation)
    {
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

        var vectorEnd = interiorEnd - Vector<float>.Count;
        var vectorX = interiorStart;
        for (; vectorX <= vectorEnd; vectorX += Vector<float>.Count)
        {
            B3(
                new Vector<float>(source, row + vectorX - 2 * dilation),
                new Vector<float>(source, row + vectorX - dilation),
                new Vector<float>(source, row + vectorX),
                new Vector<float>(source, row + vectorX + dilation),
                new Vector<float>(source, row + vectorX + 2 * dilation))
                .CopyTo(destination, row + vectorX);
        }
        for (var x = vectorX; x < interiorEnd; x++)
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

    private static void BlurLumaVerticalAndAccumulate(
        float[] previous,
        float[] horizontal,
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
            BlurLumaVerticalRows(horizontal, previous, width, sourceStartRow,
                sourceEndRow, workerStart, Math.Min(workerEnd, coreStartRow), scale);
            BlurLumaVerticalCore(previous, horizontal, adjustment, width,
                sourceStartRow, sourceEndRow, Math.Max(workerStart, coreStartRow),
                Math.Min(workerEnd, coreEndRow), coreStartRow, scale, pixels,
                layout, applyAdjustment);
            BlurLumaVerticalRows(horizontal, previous, width, sourceStartRow,
                sourceEndRow, Math.Max(workerStart, coreEndRow), workerEnd, scale);
        });
    }

    private static void BlurLumaVerticalRows(
        float[] horizontal,
        float[] destination,
        int width,
        int sourceStartRow,
        int sourceEndRow,
        int startRow,
        int endRow,
        WaveletScale scale)
    {
        for (var y = startRow; y < endRow; y++)
        {
            var target = y * width;
            var row0 = Math.Max(sourceStartRow, y - 2 * scale.Dilation) * width;
            var row1 = Math.Max(sourceStartRow, y - scale.Dilation) * width;
            var row3 = Math.Min(sourceEndRow - 1, y + scale.Dilation) * width;
            var row4 = Math.Min(sourceEndRow - 1, y + 2 * scale.Dilation) * width;
            var x = 0;
            var vectorEnd = width - Vector<float>.Count;
            for (; x <= vectorEnd; x += Vector<float>.Count)
            {
                B3(
                    new Vector<float>(horizontal, row0 + x),
                    new Vector<float>(horizontal, row1 + x),
                    new Vector<float>(horizontal, target + x),
                    new Vector<float>(horizontal, row3 + x),
                    new Vector<float>(horizontal, row4 + x))
                    .CopyTo(destination, target + x);
            }
            for (; x < width; x++)
            {
                destination[target + x] = B3(horizontal, row0 + x, row1 + x,
                    target + x, row3 + x, row4 + x);
            }
        }
    }

    private static void BlurLumaVerticalCore(
        float[] previous,
        float[] horizontal,
        float[] adjustment,
        int width,
        int sourceStartRow,
        int sourceEndRow,
        int startRow,
        int endRow,
        int coreStartRow,
        WaveletScale scale,
        ushort[] pixels,
        PixelLayout layout,
        bool applyAdjustment)
    {
        var minimum = new Vector<float>(-scale.Threshold);
        var maximum = new Vector<float>(scale.Threshold);
        for (var y = startRow; y < endRow; y++)
        {
            var target = y * width;
            var row0 = Math.Max(sourceStartRow, y - 2 * scale.Dilation) * width;
            var row1 = Math.Max(sourceStartRow, y - scale.Dilation) * width;
            var row3 = Math.Min(sourceEndRow - 1, y + scale.Dilation) * width;
            var row4 = Math.Min(sourceEndRow - 1, y + 2 * scale.Dilation) * width;
            var adjustmentIndex = (y - coreStartRow) * width;
            var x = 0;
            var vectorEnd = width - Vector<float>.Count;
            for (; x <= vectorEnd; x += Vector<float>.Count)
            {
                var blurred = B3(
                    new Vector<float>(horizontal, row0 + x),
                    new Vector<float>(horizontal, row1 + x),
                    new Vector<float>(horizontal, target + x),
                    new Vector<float>(horizontal, row3 + x),
                    new Vector<float>(horizontal, row4 + x));
                var detail = new Vector<float>(previous, target + x) - blurred;
                var retained = Vector.Min(Vector.Max(detail, minimum), maximum);
                (new Vector<float>(adjustment, adjustmentIndex + x) - retained)
                    .CopyTo(adjustment, adjustmentIndex + x);
                blurred.CopyTo(previous, target + x);
            }
            for (; x < width; x++)
            {
                var blurred = B3(horizontal, row0 + x, row1 + x,
                    target + x, row3 + x, row4 + x);
                var detail = previous[target + x] - blurred;
                adjustment[adjustmentIndex + x] -= Math.Clamp(
                    detail, -scale.Threshold, scale.Threshold);
                previous[target + x] = blurred;
            }
            if (applyAdjustment)
            {
                var pixel = target * layout.Channels;
                for (x = 0; x < width; x++, pixel += layout.Channels)
                {
                    ApplyLumaAdjustment(
                        pixels,
                        pixel,
                        adjustment[adjustmentIndex + x],
                        layout);
                }
            }
        }
    }
}
