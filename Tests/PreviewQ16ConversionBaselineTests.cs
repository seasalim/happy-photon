using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

// P0 oracle on unchanged production code. No performance measurements or run artifacts.
public sealed class PreviewQ16ConversionBaselineTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryQuantum_MatchesMagickBgraAndFloorCounts(bool alpha)
    {
        using var image = new MagickImage(MagickColors.Black, 256, 256)
        {
            ColorType = alpha ? ColorType.TrueColorAlpha : ColorType.TrueColor,
            Depth = 16
        };
        using (var pixels = image.GetPixels())
        {
            var layout = RenderKernelSupport.GetLayout(pixels);
            var alphaIndex = pixels.GetChannelIndex(PixelChannel.Alpha);
            Assert.Equal(alpha, alphaIndex.HasValue);
            var values = new ushort[65536 * layout.Channels];
            for (var q = 0; q <= ushort.MaxValue; q++)
            {
                var offset = q * layout.Channels;
                values[offset + layout.Red] = (ushort)q;
                values[offset + layout.Green] = (ushort)(65535 - q);
                values[offset + layout.Blue] = (ushort)((q + 32768) & 65535);
                if (alphaIndex is { } a) values[offset + (int)a] = (ushort)((q + 128) & 65535);
            }
            pixels.SetArea(0, 0, image.Width, image.Height, values);
            Assert.Equal(values, pixels.GetArea(0, 0, image.Width, image.Height));
        }
        AssertParity(image, $"all-q16 alpha={alpha}");
    }

    [Theory]
    [InlineData("canon-eos-6d-iso-6400.cr2")]
    [InlineData("fujifilm-x30.raf")]
    [InlineData("generated-jpeg")]
    public void FinalizedFixturePreview_MatchesMagickBgraAndFloorCounts(string fixture)
    {
        using var temporary = new TemporaryDirectory();
        var path = fixture == "generated-jpeg"
            ? CullPerfFiles.GeneratedJpeg(temporary.Path)
            : GoldenTestPaths.Asset(fixture);
        using var pair = LoadPair(path);
        using (var rendered = Render(pair.Interactive))
        {
            Assert.Equal(1600u, Math.Max(rendered.Image.Width, rendered.Image.Height));
            AssertParity(rendered.Image, fixture + " native-final");
        }

        // Add varying alpha only to an owned synthetic variant of the decoded base.
        using var alphaBase = new BaseImage(new MagickImage(pair.Interactive.Pixels), pair.Interactive.Info);
        alphaBase.Pixels.Alpha(AlphaOption.Set);
        using (var pixels = alphaBase.Pixels.GetPixels())
        {
            var values = pixels.GetArea(0, 0, alphaBase.Pixels.Width, alphaBase.Pixels.Height)!;
            var layout = RenderKernelSupport.GetLayout(pixels);
            var a = checked((int)pixels.GetChannelIndex(PixelChannel.Alpha)!.Value);
            for (var p = 0; p < values.Length / layout.Channels; p++)
                values[p * layout.Channels + a] = (ushort)(p & 65535);
            pixels.SetArea(0, 0, alphaBase.Pixels.Width, alphaBase.Pixels.Height, values);
        }
        using var alphaRender = Render(alphaBase);
        Assert.True(alphaRender.Image.HasAlpha);
        AssertParity(alphaRender.Image, fixture + " alpha-final");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GraySource_RecordsFinalizedPreviewLayout(bool alpha)
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "gray.png");
        using (var gray = new MagickImage("gradient:black-white",
                   new MagickReadSettings { Width = 1600, Height = 32 }))
        {
            gray.ColorType = alpha ? ColorType.GrayscaleAlpha : ColorType.Grayscale;
            gray.Depth = 16;
            if (alpha)
            {
                using var pixels = gray.GetPixels();
                var values = pixels.GetArea(0, 0, gray.Width, gray.Height)!;
                var a = checked((int)pixels.GetChannelIndex(PixelChannel.Alpha)!.Value);
                var channels = checked((int)pixels.Channels);
                for (var p = 0; p < values.Length / channels; p++)
                    values[p * channels + a] = (ushort)(p & 65535);
                pixels.SetArea(0, 0, gray.Width, gray.Height, values);
            }
            output.WriteLine($"gray-source alpha={alpha}: channels={gray.ChannelCount}, colorspace={gray.ColorSpace}");
            gray.Write(path, MagickFormat.Png);
        }
        using var pair = LoadPair(path);
        using var rendered = Render(pair.Interactive);
        Assert.Equal(alpha, rendered.Image.HasAlpha);
        AssertParity(rendered.Image, $"gray-source alpha={alpha} final");
    }

    private static PreviewBasePair LoadPair(string path)
    {
        var availability = new SourceAvailabilityService();
        Assert.True(SourceAccessPolicy.CanRead(availability.GetAvailability(path), SourceReadIntent.Background),
            $"Fixture unavailable locally: {path}");
        var loader = new GatedBaseImageLoader(
            new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()), availability);
        return loader.LoadPreviewBaseWithOutcome(new ImageFile(path), BaseDecodeSettings.Default,
            CancellationToken.None).Pair ?? throw new InvalidOperationException($"Fixture did not decode: {path}");
    }

    private static RenderResult Render(BaseImage source) => new RenderPipeline().Render(new RenderRequest(
        source, new EditSettings { Contrast = 20 }, RenderIntent.Preview, 1600,
        new RenderOptions(ComputeStats: true, PreparePreviewPixels: true)));

    private void AssertParity(MagickImage image, string label)
    {
        ushort[] area;
        RenderKernelSupport.PixelLayout layout;
        int? alpha;
        using (var pixels = image.GetPixels())
        {
            layout = RenderKernelSupport.GetLayout(pixels);
            alpha = pixels.GetChannelIndex(PixelChannel.Alpha) is { } a ? checked((int)a) : null;
            area = pixels.GetArea(0, 0, image.Width, image.Height)!;
        }
        var count = checked((int)(image.Width * image.Height));
        Assert.Equal(count * layout.Channels, area.Length);
        var managed = new byte[count * 4];
        for (var p = 0; p < count; p++)
        {
            var offset = p * layout.Channels;
            managed[p * 4] = Scale(area[offset + layout.Blue]);
            managed[p * 4 + 1] = Scale(area[offset + layout.Green]);
            managed[p * 4 + 2] = Scale(area[offset + layout.Red]);
            managed[p * 4 + 3] = alpha is { } a ? Scale(area[offset + a]) : (byte)255;
        }
        var oracle = BitmapConversionService.CopyBgraPixels(image);
        Assert.Equal(managed.Length, oracle.Length);
        var mismatches = 0;
        string? first = null;
        for (var i = 0; i < managed.Length; i++)
        {
            if (managed[i] == oracle[i]) continue;
            mismatches++;
            first ??= $"{label}: byte={i}, pixel={i / 4}, BGRA-channel={i % 4}, managed={managed[i]}, Magick={oracle[i]}";
        }
        ushort[] rgb;
        using (var pixels = image.GetPixelsUnsafe()) rgb = pixels.ToShortArray(PixelMapping.RGB)!;
        Assert.Equal(count * 3, rgb.Length);
        var areaLow = LowCounts(area, layout.Channels, layout.Red, layout.Green, layout.Blue);
        var oracleLow = LowCounts(rgb, 3, 0, 1, 2);
        output.WriteLine($"{label}: {image.Width}x{image.Height}, channels={layout.Channels}, " +
            $"R/G/B={layout.Red}/{layout.Green}/{layout.Blue}, A={alpha?.ToString() ?? "absent->255"}, " +
            $"BGRA bytes={managed.Length}, mismatches={mismatches}, " +
            $"low R/G/B/all area={string.Join('/', areaLow)} oracle={string.Join('/', oracleLow)}");
        if (first != null) output.WriteLine(first);
        Assert.True(mismatches == 0, first);
        Assert.Equal(oracleLow, areaLow);
    }

    // Integer round-to-nearest Q16 / 257; no integer Q16 value is a half tie.
    private static byte Scale(ushort q) => (byte)((q + 128) / 257);

    private static long[] LowCounts(ushort[] values, int channels, int r, int g, int b)
    {
        var counts = new long[4];
        for (var offset = 0; offset < values.Length; offset += channels)
        {
            var red = values[offset + r] <= 128;
            var green = values[offset + g] <= 128;
            var blue = values[offset + b] <= 128;
            if (red) counts[0]++;
            if (green) counts[1]++;
            if (blue) counts[2]++;
            if (red && green && blue) counts[3]++;
        }
        return counts;
    }
}
