using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

// Run write once on the baseline, then compare on each candidate. Never update a baseline in compare mode.
[Collection(AvaloniaTestCollection.Name)]
public sealed class DependencyOutputIdentityTests(ITestOutputHelper output)
{
    private static readonly string[] Fixtures =
    [
        "canon-eos-6d-iso-6400.cr2",
        "canon-eos-350d.cr2",
        "srgb-reference.jpg"
    ];

    private static readonly string[] Arms = ["default", "finishing", "heal", "watermark", "mixer-agx"];

    private static readonly ExportFormat[] Formats = [ExportFormat.Tiff, ExportFormat.Jpeg];

    [Fact]
    public void Comparator_DetectsOneCodeAndReportsJpegByteOnlyChanges_WhenEnabled()
    {
        Assert.SkipWhen(string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_OUTPUT_IDENTITY_DIR")),
            "Set HAPPY_PHOTON_OUTPUT_IDENTITY_DIR to exercise the identity comparator.");
        using var root = new TemporaryDirectory();
        var baseline = Path.Combine(root.Path, "baseline.tif");
        var candidate = Path.Combine(root.Path, "candidate.tif");
        using var image = new MagickImage(MagickColors.Black, 2, 1);

        using (var pixels = image.GetPixels())
        {
            pixels.SetArea(0, 0, 2, 1, new ushort[] { 1000, 2000, 3000, 4000, 5000, 6000 });
        }

        image.Write(baseline, MagickFormat.Tiff);
        File.Copy(baseline, candidate);
        var reference = Describe(root.Path, baseline, ExportFormat.Tiff);
        var identical = Describe(root.Path, candidate, ExportFormat.Tiff);
        Assert.Equal(0, Compare(baseline, candidate, reference, identical, ExportFormat.Tiff, root.Path));

        using (var pixels = image.GetPixels())
        {
            pixels.SetArea(0, 0, 2, 1, new ushort[] { 1001, 2000, 3000, 4000, 5000, 6000 });
        }

        image.Write(candidate, MagickFormat.Tiff);
        var changed = Describe(root.Path, candidate, ExportFormat.Tiff);
        var failure = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            Compare(baseline, candidate, reference, changed, ExportFormat.Tiff, root.Path));
        Assert.Contains("max abs difference=1/65535; differing pixels=1", failure.Message);
        using var diff = new MagickImage(Path.Combine(root.Path, "diffs", "candidate.tif-diff.png"));
        Assert.Equal(new ushort[] { 16, 0, 0, 0, 0, 0 }, ReadRgb(diff));

        var jpeg = Path.Combine(root.Path, "baseline.jpg");
        var byteOnly = Path.Combine(root.Path, "candidate.jpg");
        image.Quality = 90;
        image.Write(jpeg, MagickFormat.Jpeg);
        File.WriteAllBytes(byteOnly, [.. File.ReadAllBytes(jpeg), 0]);
        var jpegReference = Describe(root.Path, jpeg, ExportFormat.Jpeg);
        var jpegCandidate = Describe(root.Path, byteOnly, ExportFormat.Jpeg);
        Assert.NotEqual(jpegReference.Sha256, jpegCandidate.Sha256);
        Assert.Equal(1, Compare(jpeg, byteOnly, jpegReference, jpegCandidate, ExportFormat.Jpeg, root.Path));
    }

    [Fact]
    public async Task ExportMatrix_MatchesDecodedBaselinePixels_WhenEnabled()
    {
        var configuredDirectory = Environment.GetEnvironmentVariable("HAPPY_PHOTON_OUTPUT_IDENTITY_DIR");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(configuredDirectory),
            "Set HAPPY_PHOTON_OUTPUT_IDENTITY_DIR and HAPPY_PHOTON_OUTPUT_IDENTITY_MODE=write|compare.");
        var directory = Path.GetFullPath(configuredDirectory!);
        var mode = Environment.GetEnvironmentVariable("HAPPY_PHOTON_OUTPUT_IDENTITY_MODE");
        Assert.True(mode is "write" or "compare", "Mode must be write or compare.");
        var manifestPath = Path.Combine(directory, "manifest.json");
        var expected = mode == "compare" ? ReadManifest(manifestPath) : null;

        if (mode == "write")
        {
            Assert.False(Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any(),
                "Write requires an empty baseline directory; choose a new directory instead of replacing evidence.");
            Directory.CreateDirectory(directory);
        }

        using var temporary = new TemporaryDirectory();
        var destination = mode == "write" ? directory : temporary.Path;
        var loader = new GatedBaseImageLoader(
            new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()),
            new SourceAvailabilityService());
        var service = new ImageExportService(new RenderPipeline(), loader, new ExportMetadataService());
        var entries = new List<ManifestEntry>();
        var watch = Stopwatch.StartNew();
        var jpegByteDifferences = 0;

        foreach (var fixture in Fixtures)
        {
            var source = GoldenTestPaths.Asset(fixture);
            GoldenTestPaths.RequireReadableFixture(source);

            foreach (var arm in Arms)
            {
                foreach (var format in Formats)
                {
                    var file = new ImageFile(source) { EditSettings = CreateEdits(arm) };
                    var settings = CreateOutput(destination, arm, format);
                    var job = settings.CreateJob([file]);
                    var sample = Stopwatch.StartNew();
                    var result = await service.ExportBatchAsync(job);
                    Assert.False(result.Stopped);
                    Assert.True(result.SuccessfulTargetCount == 1,
                        string.Join(Environment.NewLine, result.FailedTargets.Select(target => target.FailureReason)));
                    var path = Assert.Single(result.Outcomes).ResolvedPath;
                    var entry = Describe(destination, path, format);
                    entries.Add(entry);

                    if (expected != null)
                    {
                        var reference = Assert.Single(expected, item => item.File == entry.File);
                        var baseline = Path.Combine(directory, reference.File);
                        Assert.Equal(reference.Sha256, HashFile(baseline));
                        jpegByteDifferences += Compare(baseline, path, reference, entry, format, directory);
                    }

                    output.WriteLine($"{entry.File}: {entry.Width}x{entry.Height}; {sample.Elapsed.TotalSeconds:F3}s");
                }
            }
        }

        Assert.Equal(30, entries.Count);

        if (mode == "write")
        {
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(entries,
                new JsonSerializerOptions { WriteIndented = true }));
        }

        output.WriteLine($"G5b {mode}: 30 outputs; {watch.Elapsed.TotalSeconds:F3}s; " +
            $"JPEG byte differences={jpegByteDifferences}/15; manifest={manifestPath}");
    }

    private static EditSettings CreateEdits(string arm)
    {
        var edits = new EditSettings();

        switch (arm)
        {
            case "finishing":
                // Production grain has a fixed coordinate hash, including the size salt 0xC2B2AE3D;
                // there is no configurable/random seed. Freeze Medium and its parameters here.
                edits.Effects = new EffectsSettings
                {
                    Vignette = -47,
                    Midpoint = 58,
                    Grain = 52,
                    GrainSize = GrainSize.Medium
                };
                break;

            case "heal":
                edits.Repairs =
                [
                    new Repair
                    {
                        Id = "dependency-output-identity-spot",
                        Type = "heal",
                        U = .5,
                        V = .5,
                        Su = .35,
                        Sv = .35,
                        Radius = .01,
                        Feather = .5,
                        Opacity = 1
                    }
                ];
                break;

            case "mixer-agx":
                edits.BaseLook = true;
                edits.Contrast = 18;
                edits.Mixer = new ColorMixerSettings
                {
                    Red = new ColorMixerBandSettings { Hue = 15, Saturation = 35, Luminance = 10 },
                    Blue = new ColorMixerBandSettings { Hue = -12, Saturation = 25, Luminance = -8 }
                };
                break;
        }

        return edits;
    }

    private static ExportSettings CreateOutput(string destination, string arm, ExportFormat format)
    {
        var settings = new ExportSettings
        {
            OutputFolder = destination,
            NamingPattern = $"{{name}}-{arm}",
            Format = format,
            Quality = 90,
            OutputColorSpace = OutputColorSpace.Srgb,
            OutputSharpening = arm == "finishing" ? OutputSharpeningMode.Screen : OutputSharpeningMode.Off,
            // Sharpening runs only after downsizing. Other arms keep the full source dimensions.
            ExportHiRes = arm != "finishing",
            ExportWeb = arm == "finishing",
            WebMaxSize = 1024,
            StripLocationData = true
        };

        if (arm == "watermark")
        {
            settings.Watermark.Restore(new WatermarkSpec(
                "Happy Photon G5b", FontFamily: "Arial", Size: 5, Opacity: 70), enabled: true);
        }

        return settings;
    }

    private static List<ManifestEntry> ReadManifest(string path)
    {
        Assert.True(File.Exists(path), $"Missing baseline manifest: {path}");
        var entries = JsonSerializer.Deserialize<List<ManifestEntry>>(File.ReadAllText(path));
        Assert.NotNull(entries);
        var names = Fixtures.SelectMany(fixture => Arms.SelectMany(arm => Formats.Select(format =>
            $"{Path.GetFileNameWithoutExtension(fixture)}-{arm}.{(format == ExportFormat.Tiff ? "tif" : "jpg")}")))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(names, entries.Select(entry => entry.File).Order(StringComparer.Ordinal).ToArray());

        return entries;
    }

    private static ManifestEntry Describe(string directory, string path, ExportFormat format)
    {
        using var image = new MagickImage(path);
        Assert.Equal(format == ExportFormat.Tiff ? 16u : 8u, image.Depth);

        return new ManifestEntry(Path.GetRelativePath(directory, path), HashFile(path), image.Width, image.Height);
    }

    private int Compare(string baseline, string candidate, ManifestEntry reference,
        ManifestEntry actual, ExportFormat format, string directory)
    {
        var byteDifference = reference.Sha256 != actual.Sha256;

        if (format == ExportFormat.Jpeg)
        {
            output.WriteLine($"JPEG bytes {actual.File}: {(byteDifference ? "DIFFERENT" : "identical")}; " +
                $"baseline={reference.Sha256}; candidate={actual.Sha256}");
        }

        using var first = new MagickImage(baseline);
        using var second = new MagickImage(candidate);
        Assert.Equal(reference.Width, first.Width);
        Assert.Equal(reference.Height, first.Height);
        var width = Math.Max(first.Width, second.Width);
        var height = Math.Max(first.Height, second.Height);
        var before = ReadRgb(first);
        var after = ReadRgb(second);
        var differences = new ushort[checked((int)(width * height * 3))];
        var maximum = 0;
        long differingPixels = 0;

        for (uint y = 0; y < height; y++)
        {
            for (uint x = 0; x < width; x++)
            {
                var differs = x >= first.Width || y >= first.Height || x >= second.Width || y >= second.Height;

                for (var channel = 0; channel < 3; channel++)
                {
                    var left = x < first.Width && y < first.Height
                        ? before[checked((int)((y * first.Width + x) * 3 + channel))] : 0;
                    var right = x < second.Width && y < second.Height
                        ? after[checked((int)((y * second.Width + x) * 3 + channel))] : 0;
                    var difference = Math.Abs(left - right);
                    maximum = Math.Max(maximum, difference);
                    differs |= difference != 0;
                    differences[checked((int)((y * width + x) * 3 + channel))] =
                        (ushort)Math.Min(ushort.MaxValue, difference * 16);
                }

                if (differs) differingPixels++;
            }
        }

        if (differingPixels != 0)
        {
            var diffDirectory = Path.Combine(directory, "diffs");
            Directory.CreateDirectory(diffDirectory);
            var diffPath = Path.Combine(diffDirectory, actual.File + "-diff.png");
            using var diff = new MagickImage(MagickColors.Black, width, height);

            using (var pixels = diff.GetPixels())
            {
                pixels.SetArea(0, 0, width, height, differences);
            }

            diff.Write(diffPath, MagickFormat.Png);
            Assert.Fail($"{actual.File}: max abs difference={maximum}/65535; " +
                $"differing pixels={differingPixels}; dimensions={first.Width}x{first.Height} vs " +
                $"{second.Width}x{second.Height}; abs difference x16={diffPath}");
        }

        return format == ExportFormat.Jpeg && byteDifference ? 1 : 0;
    }

    private static ushort[] ReadRgb(MagickImage image)
    {
        using var pixels = image.GetPixelsUnsafe();

        return pixels.ToShortArray(PixelMapping.RGB) ?? throw new InvalidOperationException("Missing RGB pixels.");
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);

        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private sealed record ManifestEntry(string File, string Sha256, uint Width, uint Height);
}
