using ImageMagick;

namespace HappyPhoton.Tests;

internal static class StraightenGateNegativeScenes
{
    internal static readonly string[] Names = ["flat", "noise", "diagonal45", "lines-30", "lines-20",
        "lines-10", "lines10", "lines20", "lines30", "blobs", "fbm"];

    internal static MagickImage Create(string name, bool portrait)
    {
        var width = portrait ? 1067 : 1600;
        var height = portrait ? 1600 : 1067;
        var values = new ushort[width * height * 3];
        var random = new Random(316);
        var angle = name == "diagonal45" ? 45 : name.StartsWith("lines", StringComparison.Ordinal)
            ? double.Parse(name[5..], System.Globalization.CultureInfo.InvariantCulture) : 0;
        var radians = angle * Math.PI / 180;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sample = name switch
                {
                    "flat" => .4,
                    "noise" => .5 + .12 * Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) *
                        Math.Cos(2 * Math.PI * random.NextDouble()),
                    "blobs" => Blobs(x / (double)width, y / (double)height),
                    "fbm" => Cloud(x, y),
                    _ => .5 + .35 * Math.Sin(2 * Math.PI * (y * Math.Cos(radians) - x * Math.Sin(radians)) / 48)
                };
                var value = (ushort)Math.Round(Math.Clamp(sample, 0, 1) * ushort.MaxValue);
                var i = (y * width + x) * 3;
                values[i] = value;
                values[i + 1] = value;
                values[i + 2] = value;
            }
        }

        using var basis = RenderPipelineTestSupport.CreateBase(values, height: height);

        return new MagickImage(basis.Pixels);
    }

    private static double Blobs(double x, double y)
    {
        var sum = .15;

        for (var i = 0; i < 15; i++)
        {
            var dx = x - Hash(i, 0);
            var dy = y - Hash(i, 1);
            sum += .12 * Math.Exp(-(dx * dx + dy * dy) / .006);
        }

        return sum;
    }

    private static double Cloud(int x, int y)
    {
        var sum = .1;
        var amplitude = .4;

        for (var octave = 0; octave < 6; octave++)
        {
            var scale = 256d / (1 << octave);
            var xx = x / scale;
            var yy = y / scale;
            var ix = (int)xx;
            var iy = (int)yy;
            var fx = xx - ix;
            var fy = yy - iy;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            var top = Hash(ix, iy) * (1 - fx) + Hash(ix + 1, iy) * fx;
            var bottom = Hash(ix, iy + 1) * (1 - fx) + Hash(ix + 1, iy + 1) * fx;
            sum += amplitude * (top * (1 - fy) + bottom * fy);
            amplitude *= .5;
        }

        return sum;
    }

    private static double Hash(int x, int y)
    {
        var value = unchecked((uint)(x * 374761393 + y * 668265263 + 316));
        value = (value ^ (value >> 13)) * 1274126177u;

        return (value ^ (value >> 16)) / (double)uint.MaxValue;
    }
}
