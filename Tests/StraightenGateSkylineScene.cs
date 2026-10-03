using ImageMagick;

namespace HappyPhoton.Tests;

internal static class StraightenGateSkylineScene
{
    // Revised frozen workload at the 1600px base: anti-aliased 6px cosine hills
    // with 280px wavelength. Isotropic ground waves have no preferred axis.
    private const double Amplitude = 6;

    private const double Wavelength = 280;

    internal static MagickImage Create(bool portrait)
    {
        var width = portrait ? 1067 : 1600;
        var height = portrait ? 1600 : 1067;
        var values = new ushort[width * height * 3];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sum = 0d;

                // Same 4x4 linear-light box sampling as the WP1 scenes.
                for (var sy = 0; sy < 4; sy++)
                {
                    for (var sx = 0; sx < 4; sx++)
                    {
                        var xx = x + (sx + .5) / 4 - width * .5;
                        var yy = y + (sy + .5) / 4 - height * .5;
                        var boundary = Amplitude * Math.Cos(2 * Math.PI * xx / Wavelength);
                        sum += yy < boundary ? .7 : Ground(xx, yy);
                    }
                }

                var value = (ushort)Math.Round(sum / 16 * ushort.MaxValue);
                var index = (y * width + x) * 3;
                values[index] = value;
                values[index + 1] = value;
                values[index + 2] = value;
            }
        }

        using var basis = RenderPipelineTestSupport.CreateBase(values, height: height);

        return new MagickImage(basis.Pixels);
    }

    private static double Ground(double x, double y)
    {
        var sum = .16;

        // Uniform angular samples and identical radial frequencies give isotropic
        // texture. Low contrast preserves the boundary as the strongest transition.
        for (var direction = 0; direction < 12; direction++)
        {
            var angle = direction * Math.PI / 12;
            sum += .002 * Math.Cos((x * Math.Cos(angle) + y * Math.Sin(angle)) * .12);
        }

        return sum;
    }

    internal static StraightenGateRegion Region(MagickImage image) =>
        new("mean curved skyline", false, image.Width * .08, image.Width * .92,
            (image.Height - 1) * .5, (image.Height - 1) * .5, image.Height * .18);
}
