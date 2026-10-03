using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;

namespace HappyPhoton.Tests;

internal static class StraightenGateScenes
{
    internal static readonly double[] Tilts = [-5, -4.5, -3, -1.5, -.5, -.2, 0, .2, .5, 1.5, 3, 4.5, 5];

    internal static readonly string[] Names = ["horizon", "facade", "interior"];

    // Sixteen high-resolution pixel centers per output pixel: exactly a 4x raster
    // followed by a 4x4 box reduction, without retaining the large raster.
    // Samples are linear Rec.2020, quantized only after box averaging.
    internal static MagickImage Create(string name, bool portrait)
    {
        if (name == "skyline") return StraightenGateSkylineScene.Create(portrait);

        if (name == "facade-blurred")
        {
            // Blur the linear Q16 base at its original resolution, before any tilt.
            using var sharp = Create("facade", portrait);
            var blurred = new MagickImage(sharp);
            blurred.GaussianBlur(0, 1.5);

            return blurred;
        }

        var width = portrait ? 1067 : 1600;
        var height = portrait ? 1600 : 1067;
        var values = new ushort[width * height * 3];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sum = 0d;

                for (var sy = 0; sy < 4; sy++)
                {
                    for (var sx = 0; sx < 4; sx++)
                    {
                        sum += Sample(name, (x + (sx + .5) / 4) / width,
                            (y + (sy + .5) / 4) / height);
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

    private static double Sample(string name, double x, double y)
    {
        // Named edge: sea horizon, facade cornice, or rear-wall/floor junction.
        // Other structures stay outside its central search ROI.
        var upper = y < .5;

        if (name == "horizon")
        {
            return upper ? .65 + .05 * y : .12 + .03 * y;
        }

        if (name == "facade")
        {
            var window = (y is > .12 and < .36 or > .68 and < .88) &&
                (x is > .12 and < .25 or > .36 and < .49 or > .60 and < .73 or > .84 and < .94);

            return window ? .08 : upper ? .70 : .24;
        }

        if (name == "interior")
        {
            if (y < .12 || x < .08 || x > .92) return .32;
            if (y is > .18 and < .38 && x is > .64 and < .86) return .10;
            if (y > .72 && x > .20 + .2 * y && x < .70 + .15 * y) return .20;

            return upper ? .72 : .10;
        }

        throw new ArgumentOutOfRangeException(nameof(name));
    }

    internal static MagickImage Tilt(MagickImage image, double tilt) =>
        RenderGeometry.Apply(image, new EditSettings { HorizonRotation = tilt }, out _);

    internal static StraightenGateRegion CentralEdge(MagickImage image, bool vertical = false)
    {
        var width = (int)image.Width;
        var height = (int)image.Height;

        return vertical
            ? new("central edge after quarter turn", true, height * .18, height * .82,
                (width - 1) * .5, (width - 1) * .5, width * .075)
            : new("central horizontal edge", false, width * .18, width * .82,
                (height - 1) * .5, (height - 1) * .5, height * .075);
    }
}


