using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

// P2 package A/B report. Run only this test in a fresh process for each CPU/package arm.
public sealed class PixelHashReportTests(ITestOutputHelper output)
{
    private sealed record Item(string Name, uint Width, uint Height, string Layout, string Sha256);

    [Fact]
    public void WriteQ16Hashes_WhenEnabled()
    {
        var destination = Environment.GetEnvironmentVariable("HAPPY_PHOTON_PIXEL_HASH_REPORT");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(destination), "Opt-in P2 pixel hash report.");
        var items = new List<Item>();
        var sources = new Dictionary<string, string>();
        using var temporary = new TemporaryDirectory();
        var defaults = new EditSettings();
        var edited = RenderSequenceGoldenTests.Settings();

        AddJpeg("jpeg-24mp", CullPerfFiles.GeneratedJpeg());
        foreach (var layout in new[] { "gray", "420" })
        {
            var path = Path.Combine(temporary.Path, layout + ".jpg");
            using (var source = new MagickImage(GoldenTestPaths.Asset("srgb-reference.jpg")))
            {
                source.Strip();
                if (layout == "gray") source.ColorType = ColorType.Grayscale;
                else source.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", "2x2,1x1,1x1");
                source.Quality = 90;
                source.Write(path, MagickFormat.Jpeg);
            }
            Assert.Equal(layout == "gray" ? "11" : "22,11,11", JpegSampling(path));
            AddJpeg("jpeg-" + layout, path);
        }

        var canonPath = GoldenTestPaths.Asset("canon-eos-6d-iso-6400.cr2");
        sources.Add("canon-6d", CullPerfFiles.Hash(canonPath));
        // One decode: every RAW render starts from this full base or its resized
        // clones. The full-base hash must match before interpreting RAW A/B deltas.
        using (var full = new RawBaseLoader().LoadFullBase(
            new ImageFile(canonPath), BaseDecodeSettings.Default, CancellationToken.None))
        {
            Assert.NotNull(full);
            using var pair = PreviewBasePairFactory.Create(full.Pixels, full.Info, CancellationToken.None);
            AddBasesAndRenders("canon-6d", pair, full);
        }

        Assert.Equal(36, items.Count);
        var report = new
        {
            Schema = 1,
            Header = new
            {
                MagickNET.Features,
                Thread = ResourceLimits.Thread,
                Environment.ProcessorCount,
                MagickVersion = MagickNET.Version,
                HashEncoding = "UTF8(width x height; layout; LF), then native interleaved Q16 samples as UInt16LE",
                ExportBoundary = "Full-size sRGB RenderIntent.Export Q16 pixels before file encoding",
                CanonDecodeCount = 1,
                DefaultSettings = EditSettingsJson.Serialize(defaults),
                EditedSettings = EditSettingsJson.Serialize(edited),
                Sources = sources
            },
            Items = items
        };
        var pathOut = Path.GetFullPath(destination!, GoldenTestPaths.RepositoryRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(pathOut)!);
        // Never replace baseline evidence accidentally.
        using var stream = new FileStream(pathOut, FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(stream, report, CullPerfFiles.Json);
        output.WriteLine($"P2: {items.Count} hashes; CPU={Environment.ProcessorCount}; " +
            $"Magick threads={ResourceLimits.Thread}; features={MagickNET.Features}; report={pathOut}");

        void AddJpeg(string name, string path)
        {
            sources.Add(name, CullPerfFiles.Hash(path));
            var loader = new StandardBaseLoader();
            var file = new ImageFile(path);
            using var pair = loader.LoadPreviewBaseWithOutcome(
                file, BaseDecodeSettings.Default, CancellationToken.None).Pair;
            using var full = loader.LoadFullBase(file, BaseDecodeSettings.Default, CancellationToken.None);
            Assert.NotNull(pair);
            Assert.NotNull(full);
            AddBasesAndRenders(name, pair, full);
        }

        void AddBasesAndRenders(string name, PreviewBasePair pair, BaseImage full)
        {
            Assert.NotNull(pair.Large);
            Add(name + "/base/interactive", pair.Interactive.Pixels);
            Add(name + "/base/large", pair.Large.Pixels);
            Add(name + "/base/full", full.Pixels);
            foreach (var (label, settings) in new[] { ("default", defaults), ("edited", edited) })
            {
                Render(name + "/" + label + "/preview-interactive", pair.Interactive, settings, RenderIntent.Preview);
                Render(name + "/" + label + "/preview-large", pair.Large, settings, RenderIntent.Preview);
                Render(name + "/" + label + "/export-full", full, settings, RenderIntent.Export);
            }
        }

        void Render(string name, BaseImage source, EditSettings settings, RenderIntent intent)
        {
            using var result = new RenderPipeline().Render(new RenderRequest(
                source, settings, intent, null, new RenderOptions(false, false)));
            Add(name, result.Image);
        }

        void Add(string name, MagickImage image) => items.Add(Hash(name, image));
    }

    private static string JpegSampling(string path)
    {
        var bytes = File.ReadAllBytes(path);
        for (var offset = 2; offset + 10 < bytes.Length;)
        {
            Assert.Equal(255, bytes[offset]);
            var marker = bytes[offset + 1];
            if (marker is 0xc0 or 0xc1 or 0xc2)
                return string.Join(',', Enumerable.Range(0, bytes[offset + 9])
                    .Select(i => bytes[offset + 11 + 3 * i].ToString("X2")));
            offset += 2 + BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 2, 2));
        }
        throw new InvalidOperationException("JPEG fixture has no supported frame header.");
    }

    private static Item Hash(string name, MagickImage image)
    {
        using var pixels = image.GetPixels();
        var channels = image.Channels.Select(channel =>
            $"{channel}:{pixels.GetChannelIndex(channel)}").Order(StringComparer.Ordinal);
        var layout = $"channels={pixels.Channels};{string.Join(',', channels)};colorspace={image.ColorSpace}";
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{image.Width}x{image.Height};{layout};\n"));
        var bytes = new byte[checked((int)(image.Width * pixels.Channels * 2))];
        for (var y = 0; y < image.Height; y++)
        {
            var row = pixels.GetArea(0, y, image.Width, 1);
            Assert.NotNull(row);
            Assert.Equal(bytes.Length / 2, row.Length);
            for (var i = 0; i < row.Length; i++)
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2, 2), row[i]);
            hash.AppendData(bytes);
        }
        return new Item(name, image.Width, image.Height, layout, Convert.ToHexString(hash.GetHashAndReset()));
    }
}
