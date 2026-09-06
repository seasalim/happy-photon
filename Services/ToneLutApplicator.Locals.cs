using ImageMagick;

namespace HappyPhoton.Services;

internal static partial class ToneLutApplicator
{
    internal static void ApplyLocals(MagickImage image, double[,] matrix, ToneLuts luts,
        ToneParams tone, RenderLocals locals, RenderExecutionOptions? execution)
    {
        execution?.ThrowIfCancellationRequested();
        using var pixels = image.GetPixels();
        var values = pixels.GetArea(0, 0, image.Width, image.Height)!;
        var layout = RenderKernelSupport.GetLayout(pixels);
        var count = checked((int)(image.Width * image.Height));
        var workers = execution?.CapWorkers(WorkerCount(count)) ?? WorkerCount(count);
        var hasColor = locals.HasColor;
        Parallel.For(0, workers, execution?.ParallelOptions ?? new ParallelOptions(), worker =>
        {
            var (start, end) = ChunkRange(count, worker, workers);
            for (var pixel = start; pixel < end; pixel++)
            {
                if ((pixel & 8191) == 0) execution?.ThrowIfCancellationRequested();
                var gain = hasColor ? 1 : locals.Gain(pixel);
                var offset = pixel * layout.Channels;
                var r = values[offset + layout.Red] / 65535.0;
                var g = values[offset + layout.Green] / 65535.0;
                var b = values[offset + layout.Blue] / 65535.0;
                var ir = Transform(matrix, 0, r, g, b);
                var ig = Transform(matrix, 1, r, g, b);
                var ib = Transform(matrix, 2, r, g, b);
                var adjusted = hasColor ? locals.ApplyColor(pixel, ref ir, ref ig, ref ib) : gain != 1;
                if (hasColor && adjusted) { ir = Math.Max(0, ir); ig = Math.Max(0, ig); ib = Math.Max(0, ib); }
                values[offset + layout.Red] = ToQuantum(!adjusted ? Interpolate(luts.Red, ir) :
                    ToneLut.Evaluate(tone, ir * gain, tone.CurveRed));
                values[offset + layout.Green] = ToQuantum(!adjusted ? Interpolate(luts.Green, ig) :
                    ToneLut.Evaluate(tone, ig * gain, tone.CurveGreen));
                values[offset + layout.Blue] = ToQuantum(!adjusted ? Interpolate(luts.Blue, ib) :
                    ToneLut.Evaluate(tone, ib * gain, tone.CurveBlue));
            }
        });
        execution?.ThrowIfCancellationRequested();
        pixels.SetArea(0, 0, image.Width, image.Height, values);
    }
}
