using System.Security.Cryptography;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

// Independent baseCommit oracle; snapshots belong in the caller's run directory.
public sealed class ExportIdentityOracleTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ExportAndProof_MatchIndependentOracle_WhenEnabled()
    {
        var directory = Environment.GetEnvironmentVariable("HAPPY_PHOTON_EXPORT_ORACLE_DIR");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(directory),
            "Set HAPPY_PHOTON_EXPORT_ORACLE_DIR to generate or compare an export oracle.");
        var compare = Environment.GetEnvironmentVariable("HAPPY_PHOTON_EXPORT_ORACLE_COMPARE");
        if (!string.IsNullOrWhiteSpace(compare))
            Assert.True(File.Exists(compare), $"Oracle does not exist: {compare}");
        // Independent renders check determinism, not performance.
        var first = await CaptureAsync();
        var second = await CaptureAsync();
        AssertMatches(first, second, "repeat determinism");
        AssertMatches(first, await CaptureAsync(disabled: true), "present but disabled");
        if (!string.IsNullOrWhiteSpace(compare))
        {
            var expected = File.ReadAllLines(compare).ToDictionary(
                line => line.Split(' ')[0], line => line.Split(' ')[1], StringComparer.Ordinal);
            AssertMatches(expected, first, compare);
            output.WriteLine($"Matched {first.Count} artifacts against {compare}.");
        }
        else
        {
            Directory.CreateDirectory(directory!);
            File.WriteAllLines(Path.Combine(directory!, "hashes.txt"),
                first.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key} {pair.Value}"));
        }
        output.WriteLine($"Deterministic: {first.Count} artifacts, 2 independent renders each.");
        output.WriteLine("Encoded outputs: SHA-256 of complete file bytes. " +
            "Proof: width/height UInt32 LE followed by row-major RGB UInt16 LE.");
    }

    [Fact]
    public async Task AbsentAndDisabled_AreIdenticalAcrossFormatsSizesAndProofs() =>
        AssertMatches(await CaptureAsync(), await CaptureAsync(disabled: true), "absent == disabled");

    private static async Task<Dictionary<string, string>> CaptureAsync(bool disabled = false)
    {
        using var root = new TemporaryDirectory();
        var source = Path.Combine(root.Path, "nonuniform.jpg");
        using (var fixture = new MagickImage("gradient:#182a48-#edce91",
                   new MagickReadSettings { Width = 384, Height = 256 }))
        {
            fixture.Strip();
            fixture.Quality = 93;
            fixture.Write(source, MagickFormat.Jpeg);
        }
        File.SetLastWriteTimeUtc(source, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var file = new ImageFile(source);
        var loader = new GatedBaseImageLoader(
            new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()),
            new SourceAvailabilityService());
        var pipeline = new RenderPipeline();
        var service = new ImageExportService(pipeline, loader, new ExportMetadataService());
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var color in new[] { OutputColorSpace.Srgb, OutputColorSpace.DisplayP3 })
        {
            foreach (var format in new[]
                     { ExportFormat.Jpeg, ExportFormat.Png, ExportFormat.Tiff, ExportFormat.Webp })
            {
                foreach (var multi in new[] { false, true })
                {
                    var name = $"{format}-{color}-{(multi ? "multi" : "single")}";
                    var settings = CreateSettings(Path.Combine(root.Path, name), format, color, multi);
                    ConfigureOffCase(settings, disabled);
                    var job = settings.CreateJob([file]);
                    var result = await service.ExportBatchAsync(job);
                    Assert.False(result.Stopped);
                    Assert.True(result.SuccessfulTargetCount == job.Targets.Count,
                        string.Join(Environment.NewLine, result.FailedTargets.Select(
                            target => $"{target.ResolvedPath}: {target.FailureReason}")));
                    foreach (var target in result.Outcomes)
                        hashes.Add($"export/{name}/{target.Recipe.Name}",
                            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(target.ResolvedPath))));
                }
            }
            var proofSettings = CreateSettings(root.Path, ExportFormat.Jpeg, color, true);
            ConfigureOffCase(proofSettings, disabled);
            foreach (var variant in proofSettings.GetActiveVariants())
            {
                // PreviewService.RenderProofAsync up to bitmap conversion: fresh full
                // base, Export intent, uncapped upstream, owned proof finalizer.
                var edits = file.EditSettings.Clone();
                using var baseImage = loader.LoadFullBase(file,
                    BaseDecodeSettings.From(edits), CancellationToken.None);
                Assert.NotNull(baseImage);
                var upstream = pipeline.RenderDisplayRec2020(new RenderRequest(
                    baseImage, edits, RenderIntent.Export, null, new RenderOptions(false, false)));
                using var proof = FinalizeProofCase(upstream, variant, proofSettings, edits);
                Assert.Equal((uint)(variant.MaxDimension ?? 384), proof.Width);
                hashes.Add($"proof/{color}/{variant.Name}", HashProof(proof));
            }
        }
        Assert.Equal(38, hashes.Count);
        return hashes;
    }

    private static ExportSettings CreateSettings(
        string folder, ExportFormat format, OutputColorSpace color, bool multi) => new()
    {
        OutputFolder = folder, Format = format, OutputColorSpace = color,
        Quality = 85, NamingPattern = "{name}", OutputSharpening = OutputSharpeningMode.Screen,
        ExportHiRes = true, ExportWeb = multi, ExportSmall = multi,
        WebMaxSize = 256, SmallMaxSize = 128
    };

    private static void ConfigureOffCase(ExportSettings settings, bool disabled)
    {
        if (disabled) settings.Watermark.Restore(new WatermarkSpec("© Jane Doe 2026",
            "No Such Font 269", true, true, 12, WatermarkColor.Black, 75,
            WatermarkEdge.Right, WatermarkAlignment.Start, false, 10), enabled: false);
        Assert.Null(settings.SnapshotOutput().Watermark);
    }

    private static MagickImage FinalizeProofCase(MagickImage upstream,
        ExportVariant variant, ExportSettings settings, EditSettings edits) =>
        RenderFinalizer.FinalizeOwnedProof(upstream, variant.MaxDimension,
            settings.OutputColorSpace, settings.OutputSharpening, edits.Effects,
            settings.SnapshotOutput().Watermark);

    private static string HashProof(MagickImage image)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(image.Width);
            writer.Write(image.Height);
            using var pixels = image.GetPixelsUnsafe();
            foreach (var value in pixels.ToShortArray(PixelMapping.RGB)!) writer.Write(value);
        }
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static void AssertMatches(IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual, string label)
    {
        var failures = expected.Keys.Union(actual.Keys).Order(StringComparer.Ordinal)
            .Where(key => !expected.TryGetValue(key, out var left) ||
                          !actual.TryGetValue(key, out var right) || left != right)
            .Select(key => $"{key}: expected {expected.GetValueOrDefault(key, "<missing>")}; " +
                           $"actual {actual.GetValueOrDefault(key, "<missing>")}").ToArray();
        Assert.True(failures.Length == 0, $"{label}:\n{string.Join("\n", failures)}");
    }
}
