using System.Runtime.InteropServices;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class AgxLookGateTests
{
    private static readonly HashSet<string> RawExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".cr2", ".nef", ".raf", ".dng"
        };

    private readonly ITestOutputHelper _output;

    public AgxLookGateTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void CommittedRawFixtures_GenerateCurrentAndAgxPreviewPairs()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_LOOKGATE") != "1",
            "Set HAPPY_PHOTON_LOOKGATE=1 and HAPPY_PHOTON_LOOKGATE_DIR to generate pairs.");
        var outputDirectory =
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_LOOKGATE_DIR");
        Assert.False(
            string.IsNullOrWhiteSpace(outputDirectory),
            "HAPPY_PHOTON_LOOKGATE_DIR must name the output directory.");
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var fixtures = Directory
            .EnumerateFiles(GoldenTestPaths.AssetDirectory)
            .Where(path => RawExtensions.Contains(Path.GetExtension(path)))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.NotEmpty(fixtures);

        var loader = new RawBaseLoader();
        foreach (var fixture in fixtures)
        {
            using var baseImage = loader.LoadPreviewBase(
                new ImageFile(fixture),
                BaseDecodeSettings.Default,
                CancellationToken.None);
            Assert.NotNull(baseImage);
            using var current = RenderCurrentTone(baseImage!);
            using var agx = RenderAgxTone(baseImage);
            using var pairImages = new MagickImageCollection
            {
                new MagickImage(current),
                new MagickImage(agx)
            };
            using var pair = pairImages.AppendHorizontally();
            pair.Format = MagickFormat.Png;
            pair.Depth = 16;
            pair.Strip();

            var stem = Path.GetFileNameWithoutExtension(fixture);
            var outputPath = Path.Combine(
                outputDirectory,
                $"{stem}-current-left-agx-right.png");
            pair.Write(outputPath);
            _output.WriteLine(outputPath);
        }
    }

    private static MagickImage RenderCurrentTone(BaseImage baseImage)
    {
        var image = new MagickImage(baseImage.Pixels);
        var settings = new EditSettings();
        var chromatic = RenderChromaticStage.CreateNormalizedMatrix(
            baseImage.Info,
            settings);
        var lut = ToneLut.Compose(new ToneParams(
            baseImage.Info.SourceExposureBiasEv,
            chromatic.Fold,
            settings.Brightness,
            settings.Contrast,
            settings.Shadows,
            settings.Highlights,
            BaseLookEnabled: true,
            settings.Curve));
        ToneLutApplicator.Apply(image, chromatic.Matrix, lut);
        RenderColorEncoding.RetagAsSrgb(image);
        return image;
    }

    private static MagickImage RenderAgxTone(BaseImage baseImage)
    {
        var samples = baseImage.Pixels.GetPixelsUnsafe()
            .ToShortArray(PixelMapping.RGB) ??
            throw new InvalidOperationException("Unable to read base pixels.");
        var crossing = new AgxCrossing(
            AgxToneEnginePropertyTests.Parameters(
                sourceExposureEv: baseImage.Info.SourceExposureBiasEv));
        crossing.Apply(samples);
        ConvertEncodedRec2020ToSrgb(samples);

        var image = RawBaseLoader.ImportRgb16(
            MemoryMarshal.AsBytes(samples.AsSpan()),
            checked((int)baseImage.Pixels.Width),
            checked((int)baseImage.Pixels.Height));
        RenderColorEncoding.RetagAsSrgb(image);
        return image;
    }

    private static void ConvertEncodedRec2020ToSrgb(ushort[] samples)
    {
        var matrix = RgbColorSpaceMatrices.LinearRec2020ToLinearSrgb;
        for (var offset = 0; offset < samples.Length; offset += 3)
        {
            var encoded = new AgxRgb(
                samples[offset] / (double)ushort.MaxValue,
                samples[offset + 1] / (double)ushort.MaxValue,
                samples[offset + 2] / (double)ushort.MaxValue);
            var linear2020 = new AgxRgb(
                ToneLut.SrgbDecode(encoded.Red),
                ToneLut.SrgbDecode(encoded.Green),
                ToneLut.SrgbDecode(encoded.Blue));
            var linearSrgb = AgxBlenderOracleTests.Transform(matrix, linear2020);
            samples[offset] = Encode(linearSrgb.Red);
            samples[offset + 1] = Encode(linearSrgb.Green);
            samples[offset + 2] = Encode(linearSrgb.Blue);
        }
    }

    private static ushort Encode(double linear) =>
        (ushort)Math.Round(
            ToneLut.SrgbEncode(Math.Clamp(linear, 0, 1)) * ushort.MaxValue,
            MidpointRounding.AwayFromZero);
}
