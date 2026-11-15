using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using ImageMagick.Formats;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class GoldenDependencyBaselineTests
{
    [Fact]
    public void CaptureDecodeAndExportPixels()
    {
        var destination = Environment.GetEnvironmentVariable("HAPPY_PHOTON_G1_OUT");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(destination), "Set HAPPY_PHOTON_G1_OUT to capture G1.");
        var root = Path.GetFullPath(destination!);
        Assert.False(Directory.Exists(root), "G1 output must be a new directory; baselines are never overwritten.");
        Directory.CreateDirectory(root);
        var fixtures = CompatibilityFixtureManifest.Load(Path.Combine(
            GoldenTestPaths.RepositoryRoot, "Tests", "compatibility-fixtures.json")).Fixtures!;
        var standardFixtures = fixtures.Where(f => !new ImageFile(f.FileName).IsRaw).ToArray();
        var inputs = GoldenTestCases.Assets.Select(a => (a.Slug, Path: a.FilePath)).ToList();
        inputs.AddRange(standardFixtures.Select(f => (f.Slug!, Path.Combine(
            GoldenTestPaths.RepositoryRoot, "artifacts", "compatibility-fixtures", f.FileName))));
        var outputs = inputs.SelectMany(input => new[]
        {
            new Output(input.Slug + "__decode.png", "decode", 16),
            new Output(input.Slug + "__Jpeg.png", "export", 8),
            new Output(input.Slug + "__Tiff.png", "export", 16),
            new Output(input.Slug + "__Png.png", "export", 8)
        }).ToArray();
        var errors = new List<string>();
        var loader = new GatedBaseImageLoader(new BaseLoaderRouter(
            new RawBaseLoader(), new StandardBaseLoader()), new SourceAvailabilityService());
        var renderer = new RenderPipeline();

        foreach (var input in inputs)
        {
            try
            {
                GoldenTestPaths.RequireReadableFixture(input.Path);
                using var decoded = loader.LoadFullBase(new ImageFile(input.Path),
                    BaseDecodeSettings.Default, CancellationToken.None)
                    ?? throw new InvalidOperationException("Production decode returned null (unsupported or unreadable).");
                SavePixels(decoded.Pixels, Path.Combine(root, input.Slug + "__decode.png"));
                using var rendered = renderer.Render(new RenderRequest(decoded, new EditSettings(),
                    RenderIntent.Export, null, new RenderOptions(false, false), OutputColorSpace.Srgb));

                foreach (var format in new[] { ExportFormat.Jpeg, ExportFormat.Tiff, ExportFormat.Png })
                {
                    using var export = new MagickImage(rendered.Image);
                    var encodedPath = Path.Combine(root, input.Slug + "." + format.ToString().ToLowerInvariant());
                    ExportEncoder.Write(export, new ExportSettings { Format = format }, OutputColorSpace.Srgb, encodedPath);
                    using var readback = new MagickImage(encodedPath);
                    SavePixels(readback, Path.Combine(root, input.Slug + "__" + format + ".png"));
                }
            }
            catch (Exception exception)
            {
                errors.Add(input.Slug + ": " + exception.Message);
            }
        }

        var manifest = new
        {
            SchemaVersion = 1,
            CompatibilityRawCount = fixtures.Count - standardFixtures.Length,
            CompatibilityNonRawCount = standardFixtures.Length,
            Inputs = inputs.Select(i => new { i.Slug, FileName = Path.GetFileName(i.Path) }),
            Settings = "Full decode; identity render at native size; sRGB; JPEG quality 85; TIFF 16-bit; PNG 8-bit",
            Outputs = outputs,
            Errors = errors
        };
        File.WriteAllText(Path.Combine(root, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    private static void SavePixels(IMagickImage<ushort> pixels, string path)
    {
        using var copy = new MagickImage(pixels);
        copy.Write(path, new PngWriteDefines { BitDepth = 16, ColorType = ColorType.TrueColorAlpha, CompressionLevel = 1 });
    }

    private sealed record Output(string File, string Kind, int NativeDepth);
}
