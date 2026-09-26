using HappyPhoton.Services;
using ImageMagick;

namespace HappyPhoton.Tests;

internal static class OpsPresencePrototype
{
    internal static void Apply(MagickImage image, BaseImageInfo info, OpsArm arm, OpsClarity candidate,
        OpsAmountField? field = null, int workers = 1, int bandRows = 32, CancellationToken cancellation = default)
    {
        // Deliberately before any image access: disposed-image tests pin the bypass.
        if (arm.Texture == 0 && arm.Clarity == 0 && field == null) return;
        using var pixels = image.GetPixels();
        var w = (int)image.Width; var h = (int)image.Height;
        var layout = RenderKernelSupport.GetLayout(pixels);
        var rgb = pixels.GetArea(0, 0, image.Width, image.Height)!;
        double Luma(int i) => OpsWorkloads.Luma(rgb[i * layout.Channels + layout.Red],
            rgb[i * layout.Channels + layout.Green], rgb[i * layout.Channels + layout.Blue]) / 65535;
        var texture = arm.Texture != 0 || field != null;
        var clarity = arm.Clarity != 0 || field != null;
        var taps = TextureKernel(Math.Max(w, h) / (double)Math.Max(info.FullWidth, info.FullHeight));
        var radius = taps.Length / 2;
        OpsGrid? a = null, b = null;
        if (clarity)
        {
            var grid = OpsGrid.Reduce(w, h, OpsWorkloads.PyramidEdge, Luma, workers, cancellation);
            (a, b) = ClarityBase(grid, candidate);
        }
        // The only working-size float plane holds the horizontal smooth. The
        // unchanged Q16 working array supplies original luma, so no row ring or
        // second luma plane is needed, even with a one-row band at full resolution.
        var horizontal = texture ? new float[w * h] : [];
        var options = new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = cancellation };
        if (texture) Parallel.For(0, (h + bandRows - 1) / bandRows, options, band =>
        {
            cancellation.ThrowIfCancellationRequested();
            for (var y = band * bandRows; y < Math.Min(h, (band + 1) * bandRows); y++)
            for (var x = 0; x < w; x++)
            {
                double sum = 0;
                for (var k = -radius; k <= radius; k++) sum += taps[k + radius] * Luma(y * w + Math.Clamp(x + k, 0, w - 1));
                horizontal[y * w + x] = (float)sum;
            }
        });
        Parallel.For(0, (h + bandRows - 1) / bandRows, options, band =>
        {
            cancellation.ThrowIfCancellationRequested();
            for (var y = band * bandRows; y < Math.Min(h, (band + 1) * bandRows); y++)
            for (var x = 0; x < w; x++)
            {
                var i = y * w + x; var light = Luma(i); double delta = 0;
                if (texture)
                {
                    double smooth = 0;
                    for (var k = -radius; k <= radius; k++) smooth += taps[k + radius] * horizontal[Math.Clamp(y + k, 0, h - 1) * w + x];
                    var detail = light - smooth;
                    delta += (arm.Texture / 100 + (field?.Texture[i] ?? 0) / 10000d) *
                        Math.CopySign(Math.Max(0, Math.Abs(detail) - OpsWorkloads.TextureThreshold), detail);
                }
                if (clarity)
                {
                    var u = (x + .5) / w; var v = (y + .5) / h;
                    var smooth = b!.Sample(u, v) + (a?.Sample(u, v) ?? 0) * light;
                    var detail = Math.Clamp(light - smooth, -OpsWorkloads.HaloLimit, OpsWorkloads.HaloLimit);
                    delta += (arm.Clarity / 100 + (field?.Clarity[i] ?? 0) / 10000d) * 4 * light * (1 - light) * detail;
                }
                var offset = i * layout.Channels;
                var r = rgb[offset + layout.Red]; var g = rgb[offset + layout.Green]; var blue = rgb[offset + layout.Blue];
                delta = Math.Clamp(delta * 65535, -Math.Min(r, Math.Min(g, blue)), 65535 - Math.Max(r, Math.Max(g, blue)));
                rgb[offset + layout.Red] = (ushort)Math.Round(r + delta, MidpointRounding.AwayFromZero);
                rgb[offset + layout.Green] = (ushort)Math.Round(g + delta, MidpointRounding.AwayFromZero);
                rgb[offset + layout.Blue] = (ushort)Math.Round(blue + delta, MidpointRounding.AwayFromZero);
            }
        });
        cancellation.ThrowIfCancellationRequested();
        pixels.SetArea(0, 0, image.Width, image.Height, rgb);
    }

    internal static double[] TextureKernel(double scale)
    {
        // Collapse the three linear B3 à trous smooths before thresholding their summed band.
        double[] weights = [1];
        for (var octave = 0; octave < 3; octave++)
        {
            var step = 1 << octave; var next = new double[weights.Length + 4 * step];
            double[] b3 = [1d / 16, 4d / 16, 6d / 16, 4d / 16, 1d / 16];
            for (var i = 0; i < weights.Length; i++) for (var k = 0; k < 5; k++) next[i + k * step] += weights[i] * b3[k];
            weights = next;
        }
        var radius = (int)Math.Ceiling(14 * scale); var result = new double[radius * 2 + 1];
        for (var i = 0; i < weights.Length; i++)
        {
            var p = (i - 14) * scale + radius; var low = (int)Math.Floor(p); var f = p - low;
            result[low] += weights[i] * (1 - f);
            if (low + 1 < result.Length) result[low + 1] += weights[i] * f;
        }
        return result;
    }

    internal static (OpsGrid? A, OpsGrid B) ClarityBase(OpsGrid grid, OpsClarity candidate)
    {
        var sigma = OpsWorkloads.ClaritySigma * Math.Max(grid.Width, grid.Height);
        if (candidate == OpsClarity.Gaussian) return (null, grid.Blur(OpsGrid.Gaussian(sigma)));
        var radius = Math.Max(1, (int)Math.Ceiling(sigma * Math.Sqrt(3)));
        var box = Enumerable.Repeat(1d / (2 * radius + 1), 2 * radius + 1).ToArray();
        var mean = grid.Blur(box);
        var square = new OpsGrid(grid.Width, grid.Height, grid.Values.Select(v => v * v).ToArray()).Blur(box);
        var a = new double[grid.Values.Length]; var b = new double[a.Length];
        for (var i = 0; i < a.Length; i++)
        {
            var variance = Math.Max(0, square.Values[i] - mean.Values[i] * mean.Values[i]);
            a[i] = variance / (variance + OpsWorkloads.GuidedEpsilon);
            b[i] = mean.Values[i] * (1 - a[i]);
        }
        return (new OpsGrid(grid.Width, grid.Height, a).Blur(box), new OpsGrid(grid.Width, grid.Height, b).Blur(box));
    }
}
