using HappyPhoton.Services;
using ImageMagick;

namespace HappyPhoton.Tests;

internal sealed record OpsDehaze(OpsGrid[] Color, OpsGrid Transmission, double[] Airlight)
{
    internal static OpsDehaze Build(MagickImage image, double[,] whiteBalance, int workers = 1)
    {
        using var pixels = image.GetPixels();
        var rgb = pixels.GetArea(0, 0, image.Width, image.Height)!;
        var layout = RenderKernelSupport.GetLayout(pixels);
        var w = (int)image.Width; var h = (int)image.Height;
        var color = Enumerable.Range(0, 3).Select(c => OpsGrid.Reduce(w, h, OpsWorkloads.LatticeEdge,
            i => (whiteBalance[c, 0] * rgb[i * layout.Channels + layout.Red] +
                whiteBalance[c, 1] * rgb[i * layout.Channels + layout.Green] +
                whiteBalance[c, 2] * rgb[i * layout.Channels + layout.Blue]) / 65535d, workers)).ToArray();
        double total = 0; var airlight = new double[3];
        for (var i = 0; i < color[0].Values.Length; i++)
        {
            var dark = Math.Max(0, Math.Min(color[0].Values[i], Math.Min(color[1].Values[i], color[2].Values[i])));
            var weight = Math.Pow(dark, 8) + 1e-12; total += weight;
            for (var c = 0; c < 3; c++) airlight[c] += weight * color[c].Values[i];
        }
        for (var c = 0; c < 3; c++) airlight[c] = Math.Max(1d / 65535, airlight[c] / total);
        var transmission = new double[color[0].Values.Length];
        for (var i = 0; i < transmission.Length; i++)
            transmission[i] = Math.Clamp(1 - OpsWorkloads.DarkStrength * Math.Min(color[0].Values[i] / airlight[0],
                Math.Min(color[1].Values[i] / airlight[1], color[2].Values[i] / airlight[2])), OpsWorkloads.TransmissionFloor, 1);
        return new(color, new(color[0].Width, color[0].Height, transmission), airlight);
    }

    // Report-only G1 intervention: sample the full analysis at preview cell centres.
    // Preserve airlight and transmission; do not re-estimate them from the preview.
    internal OpsDehaze Resample(int width, int height)
    {
        OpsGrid Grid(OpsGrid source) => source.Width == width && source.Height == height ? source :
            new(width, height, Enumerable.Range(0, width * height).Select(i =>
                source.Sample((i % width + .5) / width, (i / width + .5) / height)).ToArray());
        return new(Color.Select(Grid).ToArray(), Grid(Transmission), (double[])Airlight.Clone());
    }

    internal double Read(double u, double v, double r, double g, double b, bool refine)
    {
        if (!refine) return Transmission.Sample(u, v);
        // Joint bilateral reconstruction of the four lattice neighbours; the source
        // pixel is only a guide. Signed RGB remains untouched until the tone input.
        var x = Math.Clamp(u * Transmission.Width - .5, 0, Transmission.Width - 1);
        var y = Math.Clamp(v * Transmission.Height - .5, 0, Transmission.Height - 1);
        var ix = (int)x; var iy = (int)y; double sum = 0, weights = 0;
        var light = OpsWorkloads.Luma(r, g, b);
        for (var dy = 0; dy < 2; dy++) for (var dx = 0; dx < 2; dx++)
        {
            var i = Math.Min(iy + dy, Transmission.Height - 1) * Transmission.Width + Math.Min(ix + dx, Transmission.Width - 1);
            var difference = light - OpsWorkloads.Luma(Color[0].Values[i], Color[1].Values[i], Color[2].Values[i]);
            var weight = (dx == 0 ? 1 - (x - ix) : x - ix) * (dy == 0 ? 1 - (y - iy) : y - iy) *
                Math.Exp(-difference * difference / (2 * OpsWorkloads.GuidedEpsilon)) + 1e-30;
            sum += weight * Transmission.Values[i]; weights += weight;
        }
        return sum / weights;
    }

    internal void Correct(ref double r, ref double g, ref double b, double u, double v, double amount, bool refine)
    {
        if (amount == 0) return;
        var t = Read(u, v, r, g, b, refine);
        var strength = Math.Abs(amount) / 100;
        var factor = Math.Max(OpsWorkloads.TransmissionFloor, 1 - strength * (1 - t));
        if (amount > 0) factor = 1 / factor;
        r = Airlight[0] + (r - Airlight[0]) * factor;
        g = Airlight[1] + (g - Airlight[1]) * factor;
        b = Airlight[2] + (b - Airlight[2]) * factor;
    }
}
