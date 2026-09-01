using System.Numerics;
using static HappyPhoton.Services.RenderKernelSupport;

namespace HappyPhoton.Services;

internal static partial class RenderNoiseReduction
{
    private static void ProcessChromaPass(
        ushort[] pixels, float[] current, float[] horizontal,
        float[] adjustment, int width, int height, int sourceStart,
        int sourceEnd, int bandStart, int outputRows, PixelLayout pixelLayout,
        WaveletScale[] scales, RenderExecutionOptions? execution)
    {
        var outputSamples = checked(outputRows * width);
        adjustment.AsSpan(0, checked(outputSamples * 2)).Clear();
        var sourceRows = sourceEnd - sourceStart;
        var currentWidth = width;
        var currentRows = sourceRows;
        var planeSamples = checked(currentRows * currentWidth);
        var validStart = 0;
        var validEnd = currentRows;
        var scaleFactor = 1;
        var previous = current;
        var workspace = horizontal;
        var coarseWidth = 0;
        var coarseRows = 0;
        var coarseSamples = 0;
        var coarseAdjustmentOffset = 0;
        for (var scaleIndex = 0; scaleIndex < scales.Length; scaleIndex++)
        {
            var scale = scales[scaleIndex];
            execution?.ThrowIfCancellationRequested();
            if (scaleIndex > 0)
            {
                var nextWidth = (currentWidth + 1) / 2;
                var nextRows = (currentRows + 1) / 2;
                DownsampleChroma(previous, workspace, currentWidth, currentRows,
                    planeSamples, nextWidth, nextRows, execution);
                (previous, workspace) = (workspace, previous);
                currentWidth = nextWidth;
                currentRows = nextRows;
                planeSamples = checked(currentWidth * currentRows);
                validStart = sourceStart == 0 ? 0 : (validStart + 1) / 2;
                validEnd = sourceEnd == height
                    ? currentRows
                    : validEnd / 2;
                scaleFactor *= 2;
                if (scaleIndex == 1)
                {
                    coarseWidth = currentWidth;
                    coarseRows = currentRows;
                    coarseSamples = planeSamples;
                    coarseAdjustmentOffset = checked(planeSamples * 2);
                    current.AsSpan(coarseAdjustmentOffset,
                        checked(coarseSamples * 2)).Clear();
                }
            }

            var protectEdges = scaleIndex == 0;
            if (protectEdges)
                BlurFinestChromaHorizontal(previous, workspace, adjustment,
                    width, validStart, validEnd, bandStart - sourceStart,
                    outputRows, planeSamples, scale, execution);
            else
                BlurChromaHorizontal(previous, workspace, currentWidth,
                    validStart, validEnd, planeSamples, execution);
            var nextValidStart = sourceStart == 0
                ? 0
                : validStart + 2;
            var nextValidEnd = sourceEnd == height
                ? currentRows
                : validEnd - 2;
            if (protectEdges)
                AccumulateFinestChroma(previous, workspace, adjustment,
                    currentWidth, validStart, validEnd, nextValidStart,
                    nextValidEnd, bandStart - sourceStart, outputRows,
                    planeSamples, outputSamples, scale, execution);
            else
                AccumulateDecimatedChroma(previous, workspace, current,
                    currentWidth, validStart, validEnd, nextValidStart,
                    nextValidEnd, planeSamples, coarseAdjustmentOffset,
                    coarseWidth, coarseRows, coarseSamples, scale,
                    scaleFactor / 2, execution);
            validStart = nextValidStart;
            validEnd = nextValidEnd;
        }
        ApplyChromaRows(pixels, adjustment, width, bandStart - sourceStart,
            outputRows, outputSamples, current, coarseAdjustmentOffset,
            coarseWidth, coarseSamples, pixelLayout, execution);
    }

    private static void DownsampleChroma(
        float[] source, float[] destination, int sourceWidth, int sourceRows,
        int sourceSamples, int targetWidth, int targetRows,
        RenderExecutionOptions? execution) =>
        ForRows(targetRows, execution, (start, end) =>
        {
            for (var plane = 0; plane < 2; plane++)
            for (var y = start; y < end; y++)
            {
                var y0 = y * 2;
                var sourceOffset = plane * sourceSamples;
                var targetOffset = plane * targetWidth * targetRows +
                    y * targetWidth;
                for (var x = 0; x < targetWidth; x++)
                {
                    var x0 = x * 2;
                    destination[targetOffset + x] = source[
                        sourceOffset + y0 * sourceWidth + x0];
                }
            }
        });

    private static void BlurChromaHorizontal(
        float[] source, float[] destination, int width, int startRow,
        int endRow, int planeSamples,
        RenderExecutionOptions? execution)
    {
        ForRows(endRow - startRow, execution, (start, end) =>
        {
            for (var y = start + startRow; y < end + startRow; y++)
            {
                var row = y * width;
                BlurHorizontalRow(source, destination, row, width, 1);
                BlurHorizontalRow(source, destination, planeSamples + row,
                    width, 1);
            }
        });
    }

    private static void AccumulateFinestChroma(
        float[] previous, float[] horizontal, float[] adjustment, int width,
        int sourceStartRow, int sourceEndRow, int startRow, int endRow,
        int coreStartRow, int coreRows, int planeSamples, int outputSamples,
        WaveletScale scale,
        RenderExecutionOptions? execution)
    {
        ForRows(endRow - startRow, execution, (start, end) =>
        {
            for (var y = start + startRow; y < end + startRow; y++)
            {
                var target = y * width;
                var row0 = Math.Max(sourceStartRow, y - 2) * width;
                var row1 = Math.Max(sourceStartRow, y - 1) * width;
                var row3 = Math.Min(sourceEndRow - 1, y + 1) * width;
                var row4 = Math.Min(sourceEndRow - 1, y + 2) * width;
                var inCore = y >= coreStartRow &&
                    y < coreStartRow + coreRows;
                var adjustmentRow = (y - coreStartRow) * width;
                var x = 0;
                var vectorEnd = width - Vector<float>.Count;
                for (; x <= vectorEnd; x += Vector<float>.Count)
                {
                    var cbBlurred = B3(
                        new Vector<float>(horizontal, row0 + x),
                        new Vector<float>(horizontal, row1 + x),
                        new Vector<float>(horizontal, target + x),
                        new Vector<float>(horizontal, row3 + x),
                        new Vector<float>(horizontal, row4 + x));
                    var crBlurred = B3(
                        new Vector<float>(horizontal, planeSamples + row0 + x),
                        new Vector<float>(horizontal, planeSamples + row1 + x),
                        new Vector<float>(horizontal, planeSamples + target + x),
                        new Vector<float>(horizontal, planeSamples + row3 + x),
                        new Vector<float>(horizontal, planeSamples + row4 + x));
                    if (inCore)
                    {
                        var threshold = new Vector<float>(adjustment,
                            adjustmentRow + x);
                        var cbDetail =
                            new Vector<float>(previous, target + x) - cbBlurred;
                        var crDetail = new Vector<float>(previous,
                            planeSamples + target + x) - crBlurred;
                        (-Vector.Min(Vector.Max(cbDetail, -threshold), threshold))
                            .CopyTo(adjustment, adjustmentRow + x);
                        (-Vector.Min(Vector.Max(crDetail, -threshold), threshold))
                            .CopyTo(adjustment,
                                outputSamples + adjustmentRow + x);
                    }
                    cbBlurred.CopyTo(previous, target + x);
                    crBlurred.CopyTo(previous, planeSamples + target + x);
                }
                for (; x < width; x++)
                {
                    var cbBlurred = B3(horizontal, row0 + x, row1 + x,
                        target + x, row3 + x, row4 + x);
                    var crBlurred = B3(horizontal, planeSamples + row0 + x,
                        planeSamples + row1 + x,
                        planeSamples + target + x,
                        planeSamples + row3 + x,
                        planeSamples + row4 + x);
                    if (inCore)
                    {
                        var threshold = adjustment[adjustmentRow + x];
                        adjustment[adjustmentRow + x] = -Math.Clamp(
                            previous[target + x] - cbBlurred,
                            -threshold, threshold);
                        adjustment[outputSamples + adjustmentRow + x] =
                            -Math.Clamp(
                                previous[planeSamples + target + x] - crBlurred,
                                -threshold, threshold);
                    }
                    previous[target + x] = cbBlurred;
                    previous[planeSamples + target + x] = crBlurred;
                }
            }
        });
    }

    private static void AccumulateDecimatedChroma(
        float[] previous, float[] horizontal, float[] adjustment, int width,
        int sourceStartRow, int sourceEndRow, int startRow, int endRow,
        int planeSamples, int adjustmentOffset, int outputWidth,
        int outputRows, int outputSamples, WaveletScale scale,
        int scaleFactor, RenderExecutionOptions? execution) =>
        ForRows(endRow - startRow, execution, (start, end) =>
        {
            for (var y = start + startRow; y < end + startRow; y++)
            for (var plane = 0; plane < 2; plane++)
            {
                var planeOffset = plane * planeSamples;
                var target = planeOffset + y * width;
                var row0 = planeOffset +
                    Math.Max(sourceStartRow, y - 2) * width;
                var row1 = planeOffset +
                    Math.Max(sourceStartRow, y - 1) * width;
                var row3 = planeOffset +
                    Math.Min(sourceEndRow - 1, y + 1) * width;
                var row4 = planeOffset +
                    Math.Min(sourceEndRow - 1, y + 2) * width;
                var outputY0 = y * scaleFactor;
                var outputY1 = Math.Min(outputRows, outputY0 + scaleFactor);
                var x = 0;
                if (scaleFactor == 1)
                {
                    var minimum = new Vector<float>(-scale.Threshold);
                    var maximum = new Vector<float>(scale.Threshold);
                    var output = adjustmentOffset + plane * outputSamples +
                        y * outputWidth;
                    var vectorEnd = width - Vector<float>.Count;
                    for (; x <= vectorEnd; x += Vector<float>.Count)
                    {
                        var blurred = B3(
                            new Vector<float>(horizontal, row0 + x),
                            new Vector<float>(horizontal, row1 + x),
                            new Vector<float>(horizontal, target + x),
                            new Vector<float>(horizontal, row3 + x),
                            new Vector<float>(horizontal, row4 + x));
                        var retained = Vector.Min(Vector.Max(
                            new Vector<float>(previous, target + x) - blurred,
                            minimum), maximum);
                        (new Vector<float>(adjustment, output + x) - retained)
                            .CopyTo(adjustment, output + x);
                        blurred.CopyTo(previous, target + x);
                    }
                }
                for (; x < width; x++)
                {
                    var blurred = B3(horizontal, row0 + x, row1 + x,
                        target + x, row3 + x, row4 + x);
                    var retained = Math.Clamp(previous[target + x] - blurred,
                        -scale.Threshold, scale.Threshold);
                    previous[target + x] = blurred;
                    var outputX0 = x * scaleFactor;
                    var outputX1 = Math.Min(outputWidth,
                        outputX0 + scaleFactor);
                    for (var outputY = outputY0;
                         outputY < outputY1;
                         outputY++)
                    {
                        var output = adjustmentOffset + plane * outputSamples +
                            outputY * outputWidth;
                        for (var outputX = outputX0;
                             outputX < outputX1;
                             outputX++)
                            adjustment[output + outputX] -= retained;
                    }
                }
            }
        });

    private static void ApplyChromaRows(
        ushort[] pixels, float[] adjustment, int width, int coreStartRow,
        int coreRows, int outputSamples, float[] coarseAdjustment,
        int coarseAdjustmentOffset, int coarseWidth, int coarseSamples,
        PixelLayout layout,
        RenderExecutionOptions? execution)
    {
        if (coarseWidth == 0)
        {
            ForRows(coreRows, execution, (start, end) =>
            {
                for (var y = start; y < end; y++)
                {
                    var pixel = (coreStartRow + y) * width * layout.Channels;
                    var row = y * width;
                    for (var x = 0; x < width; x++, pixel += layout.Channels)
                        ApplyChromaAdjustment(pixels, pixel,
                            adjustment[row + x],
                            adjustment[outputSamples + row + x], layout);
                }
            });
            return;
        }

        ForRows(coreRows, execution, (start, end) =>
        {
            for (var y = start; y < end; y++)
            {
                var pixel = (coreStartRow + y) * width * layout.Channels;
                var row = y * width;
                var coarseRow = (coreStartRow + y) / 2 * coarseWidth;
                var x = 0;
                var coarseIndex = coarseRow;
                for (; x + 1 < width;
                     x += 2, coarseIndex++, pixel += layout.Channels * 2)
                {
                    var cb = coarseAdjustment[
                        coarseAdjustmentOffset + coarseIndex];
                    var cr = coarseAdjustment[
                        coarseAdjustmentOffset + coarseSamples + coarseIndex];
                    ApplyChromaAdjustment(pixels, pixel,
                        adjustment[row + x] + cb,
                        adjustment[outputSamples + row + x] + cr, layout);
                    ApplyChromaAdjustment(pixels, pixel + layout.Channels,
                        adjustment[row + x + 1] + cb,
                        adjustment[outputSamples + row + x + 1] + cr, layout);
                }
                if (x < width)
                    ApplyChromaAdjustment(pixels, pixel,
                        adjustment[row + x] + coarseAdjustment[
                            coarseAdjustmentOffset + coarseIndex],
                        adjustment[outputSamples + row + x] + coarseAdjustment[
                            coarseAdjustmentOffset + coarseSamples + coarseIndex],
                        layout);
            }
        });
    }
}
