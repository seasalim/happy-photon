using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SrgbExportBaselineTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"HappyPhotonSrgbBaseline_{Guid.NewGuid():N}");

    public SrgbExportBaselineTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void DefaultSrgbExports_MatchPreR2BytesForCurrentRid()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "export-baselines.json")));
        var rid = RuntimeInformation.RuntimeIdentifier;
        Assert.True(
            document.RootElement.GetProperty("observations").TryGetProperty(
                rid,
                out var observations),
            $"No pre-R2 export baseline is recorded for {rid}.");

        AssertAsset("canon-eos-350d", "canon-eos-350d.cr2", observations);
        AssertAsset("srgb-reference", "srgb-reference.jpg", observations);
    }

    private void AssertAsset(
        string key,
        string fileName,
        JsonElement observations)
    {
        var source = Path.Combine(GoldenTestPaths.AssetDirectory, fileName);
        var imageFile = new ImageFile(source);
        using var baseImage = new BaseLoaderRouter(
            new RawBaseLoader(),
            new StandardBaseLoader()).LoadFullBase(
                imageFile,
                BaseDecodeSettings.Default,
                CancellationToken.None) ?? throw new InvalidOperationException(
                    $"Baseline asset did not decode: {source}");
        using var rendered = new RenderPipeline().Render(new RenderRequest(
            baseImage,
            new EditSettings(),
            RenderIntent.Export,
            600,
            new RenderOptions(false, false)));

        foreach (var (format, suffix) in new[]
        {
            (ExportFormat.Jpeg, "jpeg"),
            (ExportFormat.Png, "png"),
            (ExportFormat.Webp, "webp")
        })
        {
            var path = Path.Combine(_directory, $"{key}.{suffix}");
            using var output = new MagickImage(rendered.Image);
            ExportEncoder.Write(
                output,
                new ExportSettings { Format = format },
                OutputColorSpace.Srgb,
                path);

            var expected = observations.GetProperty($"{key}.{suffix}");
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(expected.GetProperty("bytes").GetInt64(), bytes.LongLength);
            Assert.Equal(
                expected.GetProperty("sha256").GetString(),
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
