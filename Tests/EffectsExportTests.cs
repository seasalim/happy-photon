using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class EffectsExportTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-effects-export-{Guid.NewGuid():N}")).FullName;

    [Theory]
    [InlineData(OutputColorSpace.Srgb)]
    [InlineData(OutputColorSpace.DisplayP3)]
    public async Task MultiVariantExport_AppliesEffectsAfterResizeAndSharpen(
        OutputColorSpace outputColorSpace)
    {
        var sourcePath = Path.Combine(_root, $"active-{outputColorSpace}.dng");
        File.WriteAllBytes(sourcePath, []);
        var effects = new EffectsSettings
        {
            Vignette = -47,
            Midpoint = 58,
            Grain = 52,
            GrainSize = GrainSize.Coarse
        };
        var file = new ImageFile(sourcePath)
        {
            EditSettings = new EditSettings
            {
                Contrast = 18,
                Effects = effects.Clone()
            }
        };
        var variants = new[]
        {
            new ExportVariant("full", null),
            new ExportVariant("small", 32)
        };
        var output = Path.Combine(_root, $"active-out-{outputColorSpace}");
        var exportSettings = new ExportSettings
        {
            OutputFolder = output,
            Format = ExportFormat.Png,
            OutputSharpening = true,
            OutputColorSpace = outputColorSpace
        };
        var loader = new PatternBaseLoader();
        var pipeline = new RenderPipeline();

        var count = await new ImageExportService(
            pipeline,
            loader,
            new ExportMetadataService()).ExportBatchAsync(
                [file],
                exportSettings,
                variants,
                useSubfolders: true);

        Assert.Equal(1, count);
        using var baseImage = loader.LoadFullBase(
            file,
            BaseDecodeSettings.Default,
            CancellationToken.None)!;
        using var shared = pipeline.RenderDisplayRec2020(new RenderRequest(
            baseImage,
            file.EditSettings,
            RenderIntent.Export,
            null,
            new RenderOptions(false, false),
            outputColorSpace));
        foreach (var variant in variants)
        {
            using var expectedSource = new MagickImage(shared);
            if (variant.MaxDimension is { } maxDimension)
            {
                RenderColorEncoding.ResizeInLinearLight(
                    expectedSource,
                    maxDimension);
            }
            using var expected = RenderFinalizer.Finalize(
                expectedSource,
                maxDimension: null,
                outputColorSpace,
                outputSharpening: true,
                wasResized: variant.MaxDimension.HasValue,
                effects: effects);
            using var actual = new MagickImage(Path.Combine(
                output,
                variant.Name,
                $"active-{outputColorSpace}.png"));

            AssertPixelsWithinOne(expected, actual);
        }
    }

    [Theory]
    [InlineData(OutputColorSpace.Srgb)]
    [InlineData(OutputColorSpace.DisplayP3)]
    public async Task MultiVariantExport_InactiveObjectIsBitIdenticalToNull(
        OutputColorSpace outputColorSpace)
    {
        var sourcePath = Path.Combine(_root, $"off-{outputColorSpace}.dng");
        File.WriteAllBytes(sourcePath, []);
        var variants = new[]
        {
            new ExportVariant("full", null),
            new ExportVariant("small", 32)
        };
        var nullOutput = Path.Combine(_root, $"null-{outputColorSpace}");
        var explicitOutput = Path.Combine(_root, $"explicit-{outputColorSpace}");

        await ExportAsync(
            sourcePath,
            nullOutput,
            outputColorSpace,
            effects: null,
            variants);
        await ExportAsync(
            sourcePath,
            explicitOutput,
            outputColorSpace,
            new EffectsSettings
            {
                Midpoint = 94,
                GrainSize = GrainSize.Fine
            },
            variants);

        foreach (var variant in variants)
        {
            using var baseline = new MagickImage(Path.Combine(
                nullOutput,
                variant.Name,
                $"off-{outputColorSpace}.png"));
            using var explicitIdentity = new MagickImage(Path.Combine(
                explicitOutput,
                variant.Name,
                $"off-{outputColorSpace}.png"));
            Assert.Equal(ReadRgb(baseline), ReadRgb(explicitIdentity));
        }
    }

    private static async Task ExportAsync(
        string sourcePath,
        string output,
        OutputColorSpace outputColorSpace,
        EffectsSettings? effects,
        IReadOnlyList<ExportVariant> variants)
    {
        var file = new ImageFile(sourcePath)
        {
            EditSettings = new EditSettings { Effects = effects }
        };
        var settings = new ExportSettings
        {
            OutputFolder = output,
            Format = ExportFormat.Png,
            OutputSharpening = true,
            OutputColorSpace = outputColorSpace
        };
        var count = await new ImageExportService(
            new RenderPipeline(),
            new PatternBaseLoader(),
            new ExportMetadataService()).ExportBatchAsync(
                [file],
                settings,
                variants,
                useSubfolders: true);
        Assert.Equal(1, count);
    }

    private static void AssertPixelsWithinOne(
        MagickImage expected,
        MagickImage actual)
    {
        var expectedPixels = expected.GetPixelsUnsafe()
            .ToByteArray(PixelMapping.RGB) ??
            throw new InvalidOperationException("Expected pixels unavailable.");
        var actualPixels = actual.GetPixelsUnsafe()
            .ToByteArray(PixelMapping.RGB) ??
            throw new InvalidOperationException("Actual pixels unavailable.");
        Assert.Equal(expectedPixels.Length, actualPixels.Length);
        Assert.All(
            expectedPixels.Zip(actualPixels),
            pair => Assert.InRange(
                Math.Abs(pair.First - pair.Second),
                0,
                1));
    }

    private static ushort[] ReadRgb(MagickImage image) =>
        image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGB) ??
        throw new InvalidOperationException("Pixels unavailable.");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class PatternBaseLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            Create(decode);

        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.Loaded(Create(decode));

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            Create(decode);

        private static BaseImage Create(BaseDecodeSettings decode)
        {
            const int width = 64;
            const int height = 48;
            var values = new ushort[width * height * 3];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var offset = (y * width + x) * 3;
                    values[offset] = (ushort)(8000 + x * 600);
                    values[offset + 1] = (ushort)(12000 + y * 700);
                    values[offset + 2] = (ushort)(16000 + (x + y) * 350);
                }
            }
            using var baseImage = RenderPipelineTestSupport.CreateBase(
                values,
                isRaw: true,
                height: height);
            return new BaseImage(
                new MagickImage(baseImage.Pixels),
                baseImage.Info with { Decode = decode });
        }
    }
}
