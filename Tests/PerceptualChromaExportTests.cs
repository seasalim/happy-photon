using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(CheckpointCRenderGateCollection.Name)]
public sealed class PerceptualChromaExportTests : IDisposable
{
    private readonly string _output = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"HappyPhotonChromaExport_{Guid.NewGuid():N}")).FullName;

    [Theory]
    [InlineData(false, OutputColorSpace.Srgb)]
    [InlineData(false, OutputColorSpace.DisplayP3)]
    [InlineData(true, OutputColorSpace.Srgb)]
    [InlineData(true, OutputColorSpace.DisplayP3)]
    public async Task RealExportService_ReadsBackActiveChromaAcrossVariants(
        bool isRaw,
        OutputColorSpace outputColorSpace)
    {
        var fileName = isRaw
            ? "canon-eos-350d.cr2"
            : "srgb-reference.jpg";
        var sourcePath = Path.Combine(
            GoldenTestPaths.AssetDirectory,
            fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var label = $"{(isRaw ? "raw" : "standard")}-{outputColorSpace}";
        var variants = new[]
        {
            new ExportVariant("large", 500),
            new ExportVariant("small", 250)
        };
        var activeFolder = Path.Combine(_output, label, "active");
        var neutralFolder = Path.Combine(_output, label, "neutral");

        var activeCount = await Export(
            sourcePath,
            activeFolder,
            outputColorSpace,
            new EditSettings { Saturation = 48, Vibrance = -37 },
            variants);
        var neutralCount = await Export(
            sourcePath,
            neutralFolder,
            outputColorSpace,
            new EditSettings(),
            variants);

        Assert.Equal(1, activeCount);
        Assert.Equal(1, neutralCount);
        foreach (var variant in variants)
        {
            using var active = Read(Path.Combine(
                activeFolder, variant.Name, $"{stem}.png"));
            using var neutral = Read(Path.Combine(
                neutralFolder, variant.Name, $"{stem}.png"));
            Assert.Equal((uint)variant.MaxDimension!.Value,
                Math.Max(active.Width, active.Height));
            Assert.Equal(active.Width, neutral.Width);
            Assert.Equal(active.Height, neutral.Height);
            Assert.NotEqual(
                RenderPipelineTestSupport.ReadPixels(neutral),
                RenderPipelineTestSupport.ReadPixels(active));
            Assert.NotNull(active.GetColorProfile());
        }
    }

    private static async Task<int> Export(
        string sourcePath,
        string outputFolder,
        OutputColorSpace outputColorSpace,
        EditSettings editSettings,
        IReadOnlyList<ExportVariant> variants)
    {
        var file = new ImageFile(sourcePath) { EditSettings = editSettings };
        var settings = new ExportSettings
        {
            OutputFolder = outputFolder,
            Format = ExportFormat.Png,
            OutputColorSpace = outputColorSpace,
            OutputSharpening = false
        };
        var service = new ImageExportService(
            new RenderPipeline(),
            new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()),
            new ExportMetadataService());
        return await service.ExportBatchAsync(
            [file],
            settings,
            variants,
            useSubfolders: true);
    }

    private static MagickImage Read(string path)
    {
        var settings = new MagickReadSettings();
        settings.SetDefine(MagickFormat.Png, "preserve-iCCP", "true");
        return new MagickImage(path, settings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_output))
        {
            Directory.Delete(_output, recursive: true);
        }
    }
}
