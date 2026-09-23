using System.Text.Json;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class WorkingSpaceColorConversionTests(ITestOutputHelper output)
{
    private static readonly MagickColorMatrix SrgbToRec2020Matrix = new(3,
    [
        RgbColorSpaceMatrices.LinearSrgbToLinearRec2020[0, 0],
        RgbColorSpaceMatrices.LinearSrgbToLinearRec2020[0, 1],
        RgbColorSpaceMatrices.LinearSrgbToLinearRec2020[0, 2],
        RgbColorSpaceMatrices.LinearSrgbToLinearRec2020[1, 0],
        RgbColorSpaceMatrices.LinearSrgbToLinearRec2020[1, 1],
        RgbColorSpaceMatrices.LinearSrgbToLinearRec2020[1, 2],
        RgbColorSpaceMatrices.LinearSrgbToLinearRec2020[2, 0],
        RgbColorSpaceMatrices.LinearSrgbToLinearRec2020[2, 1],
        RgbColorSpaceMatrices.LinearSrgbToLinearRec2020[2, 2]
    ]);

    private readonly List<object> reports = [];

    [Fact]
    public void ManagedKernel_MatchesMagickExactly()
    {
        using (var ramp = Create(256, 768, false, (i, c) => c == i / 65536 ? (ushort)i : (ushort)0))
            Compare("q16-channel-ramps", ramp);
        var random = new Random(265);
        using (var triples = Create(1024, 1024, false, (_, _) => (ushort)random.Next(65536)))
            Compare("random-1048576-seed265", triples);
        foreach (var alpha in new[] { false, true })
        {
            using var rgb = Create(19, 17, alpha, (i, c) => (ushort)((i * 197 + c * 11003) % 65536));
            Compare(alpha ? "rgba" : "rgb", rgb);
            using var gray = new MagickImage(rgb);
            gray.ColorType = alpha ? ColorType.GrayscaleAlpha : ColorType.Grayscale;
            Compare(alpha ? "gray-alpha" : "gray", gray);
            foreach (var depth in new[] { 8u, 16u })
            foreach (var source in new[] { rgb, gray })
            {
                source.Depth = depth;
                using var decoded = new MagickImage(source.ToByteArray(MagickFormat.Png));
                Assert.Equal(depth, decoded.Depth);
                Compare($"png-{source.ColorType}-{depth}", decoded);
            }
        }
        foreach (var format in new[] { MagickFormat.Gif, MagickFormat.Png, MagickFormat.Tiff })
        {
            using var source = Create(19, 17, false, (i, c) => (ushort)((i * 197 + c * 11003) % 65536));
            source.Depth = 16;
            if (format == MagickFormat.Gif) source.Quantize(new QuantizeSettings { Colors = 16 });
            using var decoded = new MagickImage(source.ToByteArray(format));
            Compare(format.ToString(), decoded);
        }
        WriteReport(nameof(ManagedKernel_MatchesMagickExactly));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ManagedKernel_MatchesLargeJpegExactly_WhenEnabled(bool preview)
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1", "Opt-in large JPEG parity.");
        var settings = new MagickReadSettings();
        if (preview) BitmapConversionService.ApplyJpegSizeHint(settings, BaseImage.LargePreviewMaxDimension);
        using var jpeg = new MagickImage(CullPerfFiles.GeneratedJpeg(), settings);
        jpeg.AutoOrient();
        Compare(preview ? "jpeg-24mp-preview-decode" : "jpeg-24mp-full-decode", jpeg);
        WriteReport($"{nameof(ManagedKernel_MatchesLargeJpegExactly_WhenEnabled)}-{preview}");
    }

    [Fact]
    public void ManagedKernel_ChangesNonTrivialSrgbSamples()
    {
        using var image = Create(19, 17, true, (i, c) => (ushort)((i * 197 + c * 11003) % 65536));
        var before = ReadPixels(image);

        WorkingSpaceColorConversion.ConvertSrgbToLinearRec2020(image);

        Assert.Equal(ColorSpace.RGB, image.ColorSpace);
        Assert.False(before.SequenceEqual(ReadPixels(image)));
    }

    [Fact]
    public void ManagedKernel_MatchesDiskCacheExactly_WhenEnabled()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1", "Opt-in global cache limits.");
        Assert.SkipWhen(Environment.GetEnvironmentVariable("MAGICK_MAP_LIMIT") != "0",
            "Set MAGICK_MAP_LIMIT=0 before starting the test host to force disk-only caches.");
        var memory = ResourceLimits.Memory;
        try
        {
            ResourceLimits.Memory = 0;
            using var source = Create(19, 17, true, (i, c) => (ushort)((i * 197 + c * 11003) % 65536));
            Compare("disk-cache-rgba", source);
        }
        finally
        {
            ResourceLimits.Memory = memory;
        }
    }

    private void Compare(string name, MagickImage source)
    {
        var sourceTag = source.ColorSpace;
        var sourceSamples = ReadPixels(source);
        using var oracle = new MagickImage(source);
        // Keep the replaced whole-frame conversion solely as the parity oracle.
        oracle.SetAttribute("colorspace", "sRGB");
        Assert.Equal(ColorSpace.sRGB, oracle.ColorSpace);
        oracle.ColorSpace = ColorSpace.RGB;
        oracle.ColorMatrix(SrgbToRec2020Matrix);
        oracle.SetAttribute("colorspace", "RGB");
        Assert.Equal(ColorSpace.RGB, oracle.ColorSpace);
        using var candidate = new MagickImage(source);
        WorkingSpaceColorConversion.ConvertSrgbToLinearRec2020(candidate);
        Assert.Equal(ColorSpace.RGB, candidate.ColorSpace);
        var expected = ReadPixels(oracle);
        var actual = ReadPixels(candidate);
        Assert.Equal(expected.Length, actual.Length);
        long differences = 0;
        var max = 0;
        for (var i = 0; i < expected.Length; i++)
        {
            var delta = Math.Abs((int)expected[i] - actual[i]);
            if (delta != 0) differences++;
            max = Math.Max(max, delta);
        }
        output.WriteLine($"parity input={name} size={source.Width}x{source.Height} channels={candidate.ChannelCount} oracleChannels={oracle.ChannelCount} differingSamples={differences} maxAbs={max}");
        reports.Add(new { input = name, source.Width, source.Height, channels = candidate.ChannelCount,
            oracleChannels = oracle.ChannelCount, differingSamples = differences, maxAbs = max });
        Assert.True(differences == 0, $"{name}: {differences} differing Q16 samples; max absolute difference {max}.");
        using var expectedPixels = oracle.GetPixels();
        using var actualPixels = candidate.GetPixels();
        Assert.Equal(expectedPixels.GetArea(0, 0, oracle.Width, oracle.Height),
            actualPixels.GetArea(0, 0, candidate.Width, candidate.Height));
        // An in-place conversion of a clone must not mutate its source cache or tag.
        Assert.Equal(sourceTag, source.ColorSpace);
        Assert.Equal(sourceSamples, ReadPixels(source));
    }

    private void WriteReport(string name)
    {
        if (Environment.GetEnvironmentVariable("HAPPY_PHOTON_STAGE_REPORT_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, name + ".json"),
                JsonSerializer.Serialize(reports, CullPerfFiles.Json));
        }
    }

    private static ushort[] ReadPixels(MagickImage image)
    {
        using var pixels = image.GetPixels();
        return pixels.ToShortArray(PixelMapping.RGBA)!;
    }

    private static MagickImage Create(uint width, uint height, bool alpha, Func<int, int, ushort> sample)
    {
        var image = new MagickImage(MagickColors.Black, width, height);
        image.ColorType = alpha ? ColorType.TrueColorAlpha : ColorType.TrueColor;
        using var pixels = image.GetPixels();
        var channels = (int)pixels.Channels;
        var values = new ushort[checked((int)(width * height) * channels)];
        for (var i = 0; i < values.Length / channels; i++)
            for (var c = 0; c < channels; c++) values[i * channels + c] = sample(i, c);
        pixels.SetArea(0, 0, width, height, values);
        return image;
    }
}
