using System.Numerics;

namespace HappyPhoton.Services;

internal static partial class RenderNoiseReduction
{
    private static void BlurFinestChromaHorizontal(
        float[] source, float[] blurred, float[] thresholds, int width,
        int startRow, int endRow, int coreStartRow, int coreRows,
        int planeSamples, WaveletScale scale,
        RenderExecutionOptions? execution)
    {
        var edgeLimit = new Vector<float>(scale.Threshold * scale.Threshold * 16);
        var normal = new Vector<float>(scale.Threshold);
        var protectedValue = new Vector<float>(scale.Threshold * 0.35f);
        ForRows(endRow - startRow, execution, (start, end) =>
        {
            for (var y = start + startRow; y < end + startRow; y++)
            {
                var row = y * width;
                var up = Math.Max(startRow, y - 1) * width;
                var down = Math.Min(endRow - 1, y + 1) * width;
                var inCore = y >= coreStartRow && y < coreStartRow + coreRows;
                var thresholdRow = (y - coreStartRow) * width;
                var interiorStart = Math.Min(width, 2);
                var interiorEnd = Math.Max(interiorStart, width - 2);
                var x = 0;
                for (; x < interiorStart; x++)
                    BlurFinestChromaSample(source, blurred, thresholds, width,
                        row, up, down, x, planeSamples,
                        thresholdRow, scale.Threshold, edgeLimit[0], inCore);
                var vectorEnd = interiorEnd - Vector<float>.Count;
                for (; x <= vectorEnd; x += Vector<float>.Count)
                {
                    B3(new(source, row + x - 2), new(source, row + x - 1),
                        new(source, row + x), new(source, row + x + 1),
                        new(source, row + x + 2)).CopyTo(blurred, row + x);
                    var chromaRow = planeSamples + row;
                    B3(new(source, chromaRow + x - 2),
                        new(source, chromaRow + x - 1),
                        new(source, chromaRow + x),
                        new(source, chromaRow + x + 1),
                        new(source, chromaRow + x + 2))
                        .CopyTo(blurred, chromaRow + x);
                    if (!inCore)
                        continue;
                    var horizontalCb = new Vector<float>(source, row + x + 1) -
                        new Vector<float>(source, row + x - 1);
                    var horizontalCr = new Vector<float>(source,
                        chromaRow + x + 1) - new Vector<float>(source,
                        chromaRow + x - 1);
                    var verticalCb = new Vector<float>(source, down + x) -
                        new Vector<float>(source, up + x);
                    var verticalCr = new Vector<float>(source,
                        planeSamples + down + x) - new Vector<float>(source,
                        planeSamples + up + x);
                    var value = Vector.ConditionalSelect(Vector.GreaterThan(
                        Vector.Max(horizontalCb * horizontalCb +
                            horizontalCr * horizontalCr,
                            verticalCb * verticalCb + verticalCr * verticalCr),
                        edgeLimit), protectedValue, normal);
                    value.CopyTo(thresholds, thresholdRow + x);
                }
                for (; x < width; x++)
                    BlurFinestChromaSample(source, blurred, thresholds, width,
                        row, up, down, x, planeSamples,
                        thresholdRow, scale.Threshold, edgeLimit[0], inCore);
            }
        });
    }

    private static void BlurFinestChromaSample(
        float[] source, float[] blurred, float[] thresholds, int width,
        int row, int up, int down, int x, int planeSamples, int thresholdRow,
        float threshold, float edgeLimit, bool inCore)
    {
        var x0 = Math.Max(0, x - 2);
        var x1 = Math.Max(0, x - 1);
        var x3 = Math.Min(width - 1, x + 1);
        var x4 = Math.Min(width - 1, x + 2);
        blurred[row + x] = B3(source, row + x0, row + x1, row + x,
            row + x3, row + x4);
        var chromaRow = planeSamples + row;
        blurred[chromaRow + x] = B3(source, chromaRow + x0, chromaRow + x1,
            chromaRow + x, chromaRow + x3, chromaRow + x4);
        if (!inCore)
            return;
        var horizontalCb = source[row + x3] - source[row + x1];
        var horizontalCr = source[chromaRow + x3] - source[chromaRow + x1];
        var verticalCb = source[down + x] - source[up + x];
        var verticalCr = source[planeSamples + down + x] -
            source[planeSamples + up + x];
        var value = Math.Max(horizontalCb * horizontalCb + horizontalCr * horizontalCr,
            verticalCb * verticalCb + verticalCr * verticalCr) > edgeLimit
            ? threshold * 0.35f : threshold;
        thresholds[thresholdRow + x] = value;
    }
}
