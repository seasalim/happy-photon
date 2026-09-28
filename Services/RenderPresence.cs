using HappyPhoton.Models;
using ImageMagick;
using static HappyPhoton.Services.RenderKernelSupport;

namespace HappyPhoton.Services;

internal static class RenderPresence
{
    private static readonly RenderScratchSlot<float> HorizontalScratch = new();

    internal static void Apply(MagickImage image, BaseImageInfo info, EditSettings settings,
        int? bandPixelLimit = null, RenderExecutionOptions? execution = null, LocalsFrame? frame = null)
    {
        if (settings.Texture == 0 && settings.Clarity == 0) return;

        execution?.ThrowIfCancellationRequested();
        using var pixels = image.GetPixels();
        var width = checked((int)image.Width);
        var height = checked((int)image.Height);
        var layout = GetLayout(pixels);
        var rgb = pixels.GetArea(0, 0, image.Width, image.Height)!;
        double Luma(int i) => (Rec2020Luminance.Red * rgb[i * layout.Channels + layout.Red] +
            Rec2020Luminance.Green * rgb[i * layout.Channels + layout.Green] +
            Rec2020Luminance.Blue * rgb[i * layout.Channels + layout.Blue]) / 65535;
        var texture = Math.Clamp(settings.Texture, -100, 100) / 100d;
        var clarity = Math.Clamp(settings.Clarity, -100, 100) / 100d;
        var bandRows = Math.Max(1, (bandPixelLimit ?? width * 32) / width);
        var taps = TextureKernel(Math.Max(width, height) /
            (double)Math.Max(1, Math.Max(info.FullWidth, info.FullHeight)));
        var radius = taps.Length / 2;
        PresenceGrid? a = null;
        PresenceGrid? b = null;

        if (clarity != 0)
        {
            var grid = PresenceGrid.Reduce(width, height, Luma, execution, frame);
            (a, b) = grid.GuidedBase(execution);
        }

        // Original luma remains in Q16; only the horizontal smooth needs a full float plane.
        var horizontal = texture != 0 ? HorizontalScratch.Take(checked(width * height)) : null;

        try
        {
            if (horizontal is not null)
            {
                Rows(height, bandRows, execution, y =>
                {
                    for (var x = 0; x < width; x++)
                    {
                        double sum = 0;

                        for (var k = -radius; k <= radius; k++)
                        {
                            sum += taps[k + radius] * Luma(y * width + Math.Clamp(x + k, 0, width - 1));
                        }

                        horizontal[y * width + x] = (float)sum;
                    }
                });
            }

            Rows(height, bandRows, execution, y =>
            {
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    var light = Luma(i);
                    double delta = 0;

                    if (horizontal is not null)
                    {
                        double smooth = 0;

                        for (var k = -radius; k <= radius; k++)
                        {
                            smooth += taps[k + radius] * horizontal[Math.Clamp(y + k, 0, height - 1) * width + x];
                        }

                        var detail = light - smooth;
                        delta += texture * Math.CopySign(Math.Max(0, Math.Abs(detail) - .01), detail);
                    }

                    if (b is not null)
                    {
                        var u = (x + .5) / width;
                        var v = (y + .5) / height;
                        var smooth = b.Sample(u, v) + a!.Sample(u, v) * light;
                        delta += clarity * 4 * light * (1 - light) * Math.Clamp(light - smooth, -.04, .04);
                    }

                    var offset = i * layout.Channels;
                    var r = rgb[offset + layout.Red];
                    var g = rgb[offset + layout.Green];
                    var blue = rgb[offset + layout.Blue];
                    delta = Math.Clamp(delta * 65535, -Math.Min(r, Math.Min(g, blue)),
                        65535 - Math.Max(r, Math.Max(g, blue)));
                    rgb[offset + layout.Red] = ToQuantum((float)(r + delta));
                    rgb[offset + layout.Green] = ToQuantum((float)(g + delta));
                    rgb[offset + layout.Blue] = ToQuantum((float)(blue + delta));
                }
            });
            execution?.ThrowIfCancellationRequested();
            pixels.SetArea(0, 0, image.Width, image.Height, rgb);
        }
        finally
        {
            if (horizontal is not null) HorizontalScratch.Return(horizontal);
        }
    }

    internal static void Rows(int height, int bandRows, RenderExecutionOptions? execution, Action<int> row)
    {
        if (execution is null)
        {
            Parallel.For(0, (height - 1) / bandRows + 1, band =>
            {
                for (var y = band * bandRows; y < Math.Min(height, (band + 1) * bandRows); y++)
                {
                    row(y);
                }
            });

            return;
        }

        for (var start = 0; start < height; start += bandRows)
        {
            execution?.ThrowIfCancellationRequested();
            var workers = execution?.CapWorkers(Environment.ProcessorCount) ?? Environment.ProcessorCount;
            Parallel.For(start, Math.Min(height, start + bandRows), new ParallelOptions
            {
                MaxDegreeOfParallelism = workers,
                CancellationToken = execution?.CancellationToken ?? default
            }, row);
        }
    }

    private static double[] TextureKernel(double scale)
    {
        double[] weights = [1];
        double[] b3 = [1d / 16, 4d / 16, 6d / 16, 4d / 16, 1d / 16];

        for (var octave = 0; octave < 3; octave++)
        {
            var step = 1 << octave;
            var next = new double[weights.Length + 4 * step];

            for (var i = 0; i < weights.Length; i++)
            for (var k = 0; k < 5; k++)
            {
                next[i + k * step] += weights[i] * b3[k];
            }

            weights = next;
        }

        var radius = (int)Math.Ceiling(14 * scale);
        var result = new double[radius * 2 + 1];

        for (var i = 0; i < weights.Length; i++)
        {
            var p = (i - 14) * scale + radius;
            var low = (int)Math.Floor(p);
            var fraction = p - low;
            result[low] += weights[i] * (1 - fraction);

            if (low + 1 < result.Length) result[low + 1] += weights[i] * fraction;
        }

        return result;
    }
}
