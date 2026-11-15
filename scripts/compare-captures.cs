#:package Magick.NET-Q16-AnyCPU@15.1.0
#:property PublishAot=false
#:property SelfContained=false

using System.Text.Json;
using ImageMagick;

if (args.Length is not (2 or 3))
{
    Console.Error.WriteLine("Usage: dotnet run --file scripts/compare-captures.cs -- <before> <after> [masks.json]");
    Console.Error.WriteLine("masks.json maps a scene file name to [x, y, width, height] rectangles to ignore.");
    return 2;
}

var masks = args.Length == 3
    ? JsonSerializer.Deserialize<Dictionary<string, int[][]>>(File.ReadAllText(args[2]))!
    : [];

var beforeDirectory = Path.GetFullPath(args[0]);
var afterDirectory = Path.GetFullPath(args[1]);
var beforeNames = Directory.GetFiles(beforeDirectory, "*.png").Select(Path.GetFileName).Order().ToArray();
var afterNames = Directory.GetFiles(afterDirectory, "*.png").Select(Path.GetFileName).Order().ToArray();
if (beforeNames.Length == 0)
{
    Console.WriteLine($"No captures in {beforeDirectory}.");
    return 1;
}

var failed = !beforeNames.SequenceEqual(afterNames);
if (failed)
{
    Console.WriteLine($"Scene sets differ: before [{string.Join(", ", beforeNames)}]; after [{string.Join(", ", afterNames)}]");
}

foreach (var name in beforeNames.Intersect(afterNames))
{
    using var before = new MagickImage(Path.Combine(beforeDirectory, name!));
    using var after = new MagickImage(Path.Combine(afterDirectory, name!));
    if (before.Width != after.Width || before.Height != after.Height)
    {
        Console.WriteLine($"{name}: size {before.Width}x{before.Height} -> {after.Width}x{after.Height}");
        failed = true;
        continue;
    }

    var sceneMasks = masks.GetValueOrDefault(name!, []);
    var masked = sceneMasks.Sum(mask => (long)mask[2] * mask[3]);
    var differing = CountDifferingPixels(before, after, sceneMasks);
    var maskNote = masked > 0 ? $" ({masked} masked)" : "";
    Console.WriteLine($"{name}: {differing} differing pixels of {before.Width * before.Height}{maskNote}");
    failed |= differing != 0;
}

return failed ? 1 : 0;

static long CountDifferingPixels(MagickImage before, MagickImage after, int[][] masks)
{
    var width = (int)before.Width;
    using var beforePixels = before.GetPixels();
    using var afterPixels = after.GetPixels();
    var left = beforePixels.ToByteArray(PixelMapping.RGBA)!;
    var right = afterPixels.ToByteArray(PixelMapping.RGBA)!;
    long differing = 0;

    for (var offset = 0; offset < left.Length; offset += 4)
    {
        var pixel = offset / 4;
        var (x, y) = (pixel % width, pixel / width);
        if (masks.Any(mask => x >= mask[0] && x < mask[0] + mask[2] && y >= mask[1] && y < mask[1] + mask[3]))
        {
            continue;
        }

        if (left[offset] != right[offset] || left[offset + 1] != right[offset + 1] ||
            left[offset + 2] != right[offset + 2] || left[offset + 3] != right[offset + 3])
        {
            differing++;
        }
    }

    return differing;
}
