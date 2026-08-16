#:package Magick.NET-Q16-AnyCPU@14.15.0
#:property PublishAot=false
#:property SelfContained=false

using ImageMagick;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: dotnet run --file scripts/report-golden-deltas.cs -- <before> <after>");
    return 1;
}

foreach (var beforePath in Directory.GetFiles(Path.GetFullPath(args[0]), "*.png"))
{
    var name = Path.GetFileName(beforePath);
    var afterPath = Path.Combine(Path.GetFullPath(args[1]), name);
    if (!File.Exists(afterPath) || File.ReadAllBytes(beforePath).SequenceEqual(File.ReadAllBytes(afterPath)))
        continue;
    using var before = new MagickImage(beforePath);
    using var after = new MagickImage(afterPath);
    var deltas = Compare(before, after);
    Console.WriteLine($"{name}: mean ΔE {deltas.Mean:F3}; p99 ΔE {deltas.P99:F3}");
}
return 0;

static (double Mean, double P99) Compare(MagickImage before, MagickImage after)
{
    if (before.Width != after.Width || before.Height != after.Height)
        throw new InvalidOperationException("Golden dimensions changed.");
    Normalize(before);
    Normalize(after);
    using var beforePixels = before.GetPixels();
    using var afterPixels = after.GetPixels();
    var left = beforePixels.ToByteArray(PixelMapping.RGB)!;
    var right = afterPixels.ToByteArray(PixelMapping.RGB)!;
    var values = new double[checked((int)(before.Width * before.Height))];
    for (var pixel = 0; pixel < values.Length; pixel++)
    {
        var offset = pixel * 3;
        var first = Lab(left, offset);
        var second = Lab(right, offset);
        values[pixel] = Math.Sqrt(
            Math.Pow(first.L - second.L, 2) +
            Math.Pow(first.A - second.A, 2) +
            Math.Pow(first.B - second.B, 2));
    }
    Array.Sort(values);
    return (values.Average(), values[Math.Max(0, (int)Math.Ceiling(values.Length * .99) - 1)]);
}

static void Normalize(MagickImage image)
{
    if (image.GetColorProfile() is { } profile)
        image.TransformColorSpace(profile, ColorProfiles.SRGB);
    else if (image.ColorSpace != ColorSpace.sRGB)
        image.ColorSpace = ColorSpace.sRGB;
}

static (double L, double A, double B) Lab(byte[] pixels, int index)
{
    var r = Linear(pixels[index] / 255d);
    var g = Linear(pixels[index + 1] / 255d);
    var b = Linear(pixels[index + 2] / 255d);
    var x = Transform((.4124564 * r + .3575761 * g + .1804375 * b) / .95047);
    var y = Transform(.2126729 * r + .7151522 * g + .0721750 * b);
    var z = Transform((.0193339 * r + .1191920 * g + .9503041 * b) / 1.08883);
    return (116 * y - 16, 500 * (x - y), 200 * (y - z));
}

static double Linear(double value) => value <= .04045
    ? value / 12.92
    : Math.Pow((value + .055) / 1.055, 2.4);

static double Transform(double value) => value > 216d / 24389d
    ? Math.Cbrt(value)
    : ((24389d / 27d) * value + 16) / 116;
