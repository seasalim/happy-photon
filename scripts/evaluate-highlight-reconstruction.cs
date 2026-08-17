#:project ../HappyPhoton.csproj
#:property PublishAot=false
#:property SelfContained=false

using System.Runtime.InteropServices;
using HappyPhoton.LibRaw.Interop;
using ImageMagick;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine(
        "Usage: dotnet run --file scripts/evaluate-highlight-reconstruction.cs -- <raw-file> [output-directory]");
    return 1;
}

var rawPath = Path.GetFullPath(args[0]);
var outputDirectory = Path.GetFullPath(
    args.Length == 2
        ? args[1]
        : Path.Combine(Path.GetTempPath(), "happy-photon-highlight-evaluation"));
Directory.CreateDirectory(outputDirectory);

var modes = new[]
{
    (Value: 0, Name: "clip"),
    (Value: 2, Name: "blend"),
    (Value: 3, Name: "rebuild-3"),
    (Value: 5, Name: "rebuild-5"),
    (Value: 9, Name: "rebuild-9")
};
var results = new List<DecodeResult>();

foreach (var mode in modes)
{
    using var context = LibRawContext.Open(rawPath);
    context.Unpack();
    context.ConfigureOutput(new LibRawOutputConfiguration
    {
        AbiVersion = LibRawOutputConfiguration.Version,
        OutputBits = 16,
        OutputColor = 1,
        GammaPower = 1,
        GammaSlope = 1,
        NoAutoBright = true,
        UseCameraWhiteBalance = true,
        UseCameraMatrix = true,
        HighlightMode = mode.Value
    });

    context.Process();
    using var processed = context.MakeProcessedImage();
    var samples = MemoryMarshal.Cast<byte, ushort>(
        processed.AsSpan()).ToArray();
    results.Add(new DecodeResult(
        mode.Value,
        mode.Name,
        checked((int)processed.Description.Width),
        checked((int)processed.Description.Height),
        samples));
}

var clip = results[0];
var highlightPixels = new bool[clip.Samples.Length / 3];
var highlightPixelCount = 0;
for (var pixel = 0; pixel < highlightPixels.Length; pixel++)
{
    var offset = pixel * 3;
    var maximum = Math.Max(
        clip.Samples[offset],
        Math.Max(clip.Samples[offset + 1], clip.Samples[offset + 2]));
    highlightPixels[pixel] = maximum >= ushort.MaxValue * 0.95;
    if (highlightPixels[pixel])
    {
        highlightPixelCount++;
    }
}

Console.WriteLine($"Asset: {rawPath}");
Console.WriteLine(
    $"Evaluated pixels: {clip.Width}x{clip.Height}; bright-mask pixels: {highlightPixelCount}");
Console.WriteLine(
    "mode       clipped-channels  bright-chroma  bright-detail-sd  delta-from-blend");

var blend = results[1];
foreach (var result in results)
{
    var clippedChannels = result.Samples.Count(sample => sample == ushort.MaxValue);
    var chromaTotal = 0d;
    var luminanceTotal = 0d;
    var luminanceSquaredTotal = 0d;
    var deltaFromBlend = 0d;
    var selectedChannels = 0;

    for (var pixel = 0; pixel < highlightPixels.Length; pixel++)
    {
        if (!highlightPixels[pixel])
        {
            continue;
        }

        var offset = pixel * 3;
        var red = result.Samples[offset] / 65535d;
        var green = result.Samples[offset + 1] / 65535d;
        var blue = result.Samples[offset + 2] / 65535d;
        var luminance = 0.2126 * red + 0.7152 * green + 0.0722 * blue;
        chromaTotal += Math.Max(red, Math.Max(green, blue)) -
                       Math.Min(red, Math.Min(green, blue));
        luminanceTotal += luminance;
        luminanceSquaredTotal += luminance * luminance;

        for (var channel = 0; channel < 3; channel++)
        {
            deltaFromBlend += Math.Abs(
                result.Samples[offset + channel] -
                blend.Samples[offset + channel]) / 65535d;
            selectedChannels++;
        }
    }

    var mean = luminanceTotal / highlightPixelCount;
    var standardDeviation = Math.Sqrt(Math.Max(
        0,
        luminanceSquaredTotal / highlightPixelCount - mean * mean));
    Console.WriteLine(
        $"{result.Name,-11}{clippedChannels,16}  " +
        $"{chromaTotal / highlightPixelCount,13:F5}  " +
        $"{standardDeviation,16:F5}  " +
        $"{deltaFromBlend / selectedChannels,16:F5}");

    var displaySamples = EncodeSrgb(result.Samples);
    using var image = new MagickImage(
        MagickColors.Black,
        (uint)result.Width,
        (uint)result.Height);
    image.ColorSpace = ColorSpace.sRGB;
    image.ImportPixels(
        MemoryMarshal.AsBytes(displaySamples.AsSpan()),
        new PixelImportSettings(
            (uint)result.Width,
            (uint)result.Height,
            StorageType.Short,
            PixelMapping.RGB));
    image.Resize(new MagickGeometry(1200, 1200)
    {
        IgnoreAspectRatio = false,
        Greater = true
    });
    image.Write(
        Path.Combine(outputDirectory, $"{result.Mode}-{result.Name}.png"),
        MagickFormat.Png);
}

Console.WriteLine($"Rendered comparisons: {outputDirectory}");
return 0;

static ushort[] EncodeSrgb(ushort[] linearSamples)
{
    var displaySamples = new ushort[linearSamples.Length];
    for (var index = 0; index < linearSamples.Length; index++)
    {
        var linear = linearSamples[index] / (double)ushort.MaxValue;
        var encoded = linear <= 0.0031308
            ? 12.92 * linear
            : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
        displaySamples[index] = (ushort)Math.Round(
            encoded * ushort.MaxValue);
    }

    return displaySamples;
}

sealed record DecodeResult(
    int Mode,
    string Name,
    int Width,
    int Height,
    ushort[] Samples);
