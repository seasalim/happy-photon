#:package Magick.NET-Q16-AnyCPU@15.1.0
#:property PublishAot=false
#:property SelfContained=false

using ImageMagick;
using System.Text.Json;

if (args.Length == 2 && args[0] == "--self-test")
{
    return SelfTest(args[1]);
}

if (args.Length == 4 && args[0] == "--enforce")
{
    return Enforce(args[1], args[2], args[3]);
}

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: dotnet run --file scripts/report-golden-deltas.cs -- [--enforce] <before> <after> [manifest.json] | --self-test <new-scratch-directory>");
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

static int Enforce(string baseDirectory, string candidateDirectory, string manifestPath)
{
    using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
    var root = manifest.RootElement;
    if (root.GetProperty("SchemaVersion").GetInt32() != 1)
        throw new InvalidDataException("Unsupported G1 manifest version.");

    var failed = root.GetProperty("Errors").GetArrayLength() != 0;
    var outputs = root.GetProperty("Outputs").EnumerateArray().ToArray();
    if (outputs.Length == 0)
        throw new InvalidDataException("G1 manifest has no expected outputs.");

    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var output in outputs)
    {
        var file = output.GetProperty("File").GetString()!;
        var kind = output.GetProperty("Kind").GetString();
        var depth = output.GetProperty("NativeDepth").GetInt32();
        if (Path.GetFileName(file) != file || !seen.Add(file) ||
            kind is not ("decode" or "export") || depth is not (8 or 16))
            throw new InvalidDataException("Invalid G1 output entry.");

        var beforePath = Path.Combine(baseDirectory, file);
        var afterPath = Path.Combine(candidateDirectory, file);
        if (!File.Exists(beforePath) || !File.Exists(afterPath))
        {
            Console.WriteLine($"FAIL {file}: missing base={!File.Exists(beforePath)}, candidate={!File.Exists(afterPath)}");
            failed = true;
            continue;
        }

        using var before = new MagickImage(beforePath);
        using var after = new MagickImage(afterPath);
        if (before.Width != after.Width || before.Height != after.Height)
        {
            Console.WriteLine($"FAIL {file}: dimensions differ");
            failed = true;
            continue;
        }

        using var beforePixels = before.GetPixels();
        using var afterPixels = after.GetPixels();
        var left = beforePixels.ToShortArray(PixelMapping.RGBA)!;
        var right = afterPixels.ToShortArray(PixelMapping.RGBA)!;
        long differingPixels = 0;
        var maximum = 0;

        for (var offset = 0; offset < left.Length; offset += 4)
        {
            var pixelMaximum = 0;

            for (var channel = 0; channel < 4; channel++)
            {
                pixelMaximum = Math.Max(pixelMaximum, Math.Abs((int)left[offset + channel] - right[offset + channel]));
            }

            if (pixelMaximum != 0) differingPixels++;
            maximum = Math.Max(maximum, pixelMaximum);
        }

        var limit = kind == "decode" ? 0 : depth == 8 ? 257 : 1;
        var pass = maximum <= limit;
        failed |= !pass;
        Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {file}: differingPixels={differingPixels}; " +
            $"max16={maximum}; max8Equivalent={maximum / 257d:F6}; nativeDepth={depth}; limit16={limit}");
    }

    return failed ? 1 : 0;
}

static int SelfTest(string directory)
{
    if (Directory.Exists(directory))
        throw new InvalidOperationException("Self-test requires a new scratch directory.");

    var baseline = Path.Combine(directory, "base");
    var candidate = Path.Combine(directory, "candidate");
    Directory.CreateDirectory(baseline);
    Directory.CreateDirectory(candidate);
    var manifestPath = Path.Combine(directory, "manifest.json");
    var cases = new[]
    {
        (Kind: "decode", Depth: 16, Delta: 0, Expected: 0),
        (Kind: "decode", Depth: 16, Delta: 1, Expected: 1),
        (Kind: "export", Depth: 16, Delta: 1, Expected: 0),
        (Kind: "export", Depth: 16, Delta: 2, Expected: 1),
        (Kind: "export", Depth: 8, Delta: 257, Expected: 0),
        (Kind: "export", Depth: 8, Delta: 258, Expected: 1)
    };

    foreach (var test in cases)
    {
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            Errors = Array.Empty<string>(),
            Outputs = new[] { new { File = "pixel.png", Kind = test.Kind, NativeDepth = test.Depth } }
        }));
        WritePixel(Path.Combine(baseline, "pixel.png"), 10000);
        WritePixel(Path.Combine(candidate, "pixel.png"), (ushort)(10000 + test.Delta));
        if (Enforce(baseline, candidate, manifestPath) != test.Expected)
            throw new InvalidOperationException($"Comparator boundary failed: {test}");
    }

    File.Delete(Path.Combine(candidate, "pixel.png"));
    if (Enforce(baseline, candidate, manifestPath) != 1)
        throw new InvalidOperationException("Missing candidate passed.");

    WritePixel(Path.Combine(candidate, "pixel.png"), 10000);
    File.Delete(Path.Combine(baseline, "pixel.png"));
    if (Enforce(baseline, candidate, manifestPath) != 1)
        throw new InvalidOperationException("Missing base passed.");

    Console.WriteLine("Comparator self-test: 8 checks passed.");
    return 0;
}

static void WritePixel(string path, ushort red)
{
    using var image = new MagickImage(new MagickColor(red, (ushort)12000, (ushort)14000), 1, 1);
    image.Write(path, new ImageMagick.Formats.PngWriteDefines
    {
        BitDepth = 16,
        ColorType = ColorType.TrueColorAlpha
    });
}
