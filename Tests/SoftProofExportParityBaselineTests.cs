using System.Security.Cryptography;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SoftProofExportParityBaselineTests : IDisposable
{
    private const string OptInVariable = "HAPPY_PHOTON_SOFTPROOF_EXPORT_BASELINE";
    private readonly TemporaryDirectory _directory = new();
    private readonly ITestOutputHelper _output;

    public SoftProofExportParityBaselineTests(ITestOutputHelper output) =>
        _output = output;

    [Fact]
    public async Task DefaultRawExports_PrintParityHashes()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable(OptInVariable) != "1",
            $"Set {OptInVariable}=1 to measure the export parity baseline.");

        foreach (var format in new[] { ExportFormat.Jpeg, ExportFormat.Tiff })
        foreach (var colorSpace in new[] { OutputColorSpace.Srgb, OutputColorSpace.DisplayP3 })
        {
            var outputFolder = Path.Combine(_directory.Path, $"{format}-{colorSpace}");
            var settings = new ExportSettings
            {
                OutputFolder = outputFolder,
                Format = format,
                OutputColorSpace = colorSpace
            };
            var source = new ImageFile(
                GoldenTestPaths.Asset("canon-eos-6d-iso-6400.cr2"));
            var result = await CreateService().ExportBatchAsync([source], settings);
            Assert.Equal(1, result.SuccessfulTargetCount);

            var path = Path.Combine(outputFolder,
                $"canon-eos-6d-iso-6400{settings.FileExtension}");
            if (format == ExportFormat.Tiff)
            {
                using var image = new MagickImage(path);
                Assert.Equal(16u, image.Depth);
            }
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            _output.WriteLine($"{format}/{colorSpace}: {hash}");
        }
    }

    private static ImageExportService CreateService() => new(
        new RenderPipeline(),
        new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()),
        new ExportMetadataService());

    public void Dispose() => _directory.Dispose();
}
