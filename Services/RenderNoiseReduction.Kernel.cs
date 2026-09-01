using System.Numerics;
using System.Runtime.CompilerServices;
using static HappyPhoton.Services.RenderKernelSupport;

namespace HappyPhoton.Services;

internal static partial class RenderNoiseReduction
{
    private const double Rec2020RedToGreen =
        Rec2020Luminance.Red / Rec2020Luminance.Green;
    private const double Rec2020BlueToGreen =
        Rec2020Luminance.Blue / Rec2020Luminance.Green;
    private const double Rec2020InverseGreen = 1 / Rec2020Luminance.Green;

    private static void LoadActivePlanes(
        ushort[] source,
        float[]? lumaPlane,
        float[]? chromaPlanes,
        int width,
        int rows,
        PixelLayout layout,
        RenderExecutionOptions? execution)
    {
        var planeSamples = checked(rows * width);
        ForRows(rows, execution, (start, end) =>
        {
            for (var y = start; y < end; y++)
            {
                var pixel = y * width * layout.Channels;
                var target = y * width;
                for (var x = 0; x < width; x++, target++)
                {
                    var red = source[pixel + layout.Red];
                    var green = source[pixel + layout.Green];
                    var blue = source[pixel + layout.Blue];
                    var luma = GetLuma(red, green, blue);
                    if (lumaPlane is not null)
                    {
                        lumaPlane[target] = luma;
                    }
                    if (chromaPlanes is not null)
                    {
                        chromaPlanes[target] = blue - luma;
                        chromaPlanes[planeSamples + target] = red - luma;
                    }
                    pixel += layout.Channels;
                }
            }
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyLumaAdjustment(
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
    private static void ApplyChromaAdjustment(
        ushort[] output,
        int pixel,
        float cbAdjustment,
        float crAdjustment,
        PixelLayout layout)
    {
        var red = output[pixel + layout.Red];
        var green = output[pixel + layout.Green];
        var blue = output[pixel + layout.Blue];
        var retainedLuma =
            Rec2020Luminance.Red * red +
            Rec2020Luminance.Green * green +
            Rec2020Luminance.Blue * blue;
        var targetLuma = (ushort)Math.Round(retainedLuma);
        var greenAdjustment = (float)(
            -(Rec2020RedToGreen * crAdjustment +
              Rec2020BlueToGreen * cbAdjustment));
        var scale = 1f;
        if (red + crAdjustment is < ushort.MinValue or > ushort.MaxValue ||
            green + greenAdjustment is < ushort.MinValue or > ushort.MaxValue ||
            blue + cbAdjustment is < ushort.MinValue or > ushort.MaxValue)
        {
            scale = LimitAdjustment(red, crAdjustment, scale);
            scale = LimitAdjustment(green, greenAdjustment, scale);
            scale = LimitAdjustment(blue, cbAdjustment, scale);
        }

        var resultRed = ToQuantum(red + crAdjustment * scale);
        var resultBlue = ToQuantum(blue + cbAdjustment * scale);
        var resultGreen = ToQuantum((float)(
            (targetLuma - Rec2020Luminance.Red * resultRed -
             Rec2020Luminance.Blue * resultBlue) * Rec2020InverseGreen));

        output[pixel + layout.Red] = resultRed;
        output[pixel + layout.Green] = resultGreen;
        output[pixel + layout.Blue] = resultBlue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float LimitAdjustment(
        ushort value,
        float adjustment,
        float scale)
    {
        if (adjustment > 0)
        {
            return Math.Min(scale, (ushort.MaxValue - value) / adjustment);
        }
        if (adjustment < 0)
        {
            return Math.Min(scale, -value / adjustment);
        }
        return scale;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<float> B3(
        Vector<float> first,
        Vector<float> second,
        Vector<float> center,
        Vector<float> fourth,
        Vector<float> fifth) =>
        (first + 4 * second + 6 * center + 4 * fourth + fifth) *
        (1f / 16);

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
}
