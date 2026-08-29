using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportProofTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    [WindowsFact]
    public async Task RenderProof_RendersFreshAndAppliesFinalizerSettings()
    {
        using var catalog = new CatalogService(_root.Path);
        await catalog.InitializeAsync();
        var loader = new ProofLoader();
        await using var service = new PreviewService(
            catalog,
            loader,
            new RenderPipeline());
        var upstreamCount = 0;
        service.ProofRenderDisplayRec2020 = request =>
        {
            upstreamCount++;
            return new RenderPipeline().RenderDisplayRec2020(request);
        };
        var image = new ImageFile(Path.Combine(_root.Path, "proof.jpg"));

        using var large = Assert.IsAssignableFrom<Avalonia.Media.Imaging.Bitmap>(
            await service.RenderProofAsync(
                image,
                new EditSettings(),
                64,
                OutputColorSpace.DisplayP3,
                OutputSharpeningMode.Print));
        using var small = Assert.IsAssignableFrom<Avalonia.Media.Imaging.Bitmap>(
            await service.RenderProofAsync(
                image,
                new EditSettings(),
                32,
                OutputColorSpace.Srgb,
                OutputSharpeningMode.Off));

        Assert.Equal(2, upstreamCount);
        Assert.Equal(2, loader.FullLoadCount);
        Assert.Equal(64, large.PixelSize.Width);
        Assert.Equal(32, small.PixelSize.Width);
        AssertAllRowsContainProofPixels(large);
        AssertAllRowsContainProofPixels(small);
    }

    [WindowsFact]
    public async Task FullResolutionProof_PopulatesEveryBitmapRow()
    {
        using var catalog = new CatalogService(_root.Path);
        await catalog.InitializeAsync();
        var loader = new ProofLoader(6000, 4000, isRaw: true);
        await using var service = new PreviewService(
            catalog,
            loader,
            new RenderPipeline());
        (uint Width, uint Height) renderedSize = default;
        service.ProofRenderDisplayRec2020 = request =>
        {
            renderedSize = (request.Base.Pixels.Width, request.Base.Pixels.Height);
            return new RenderPipeline().RenderDisplayRec2020(request);
        };

        using var proof = Assert.IsAssignableFrom<Avalonia.Media.Imaging.Bitmap>(
            await service.RenderProofAsync(
                new ImageFile(Path.Combine(_root.Path, "full-proof.cr2")),
                new EditSettings(),
                maxDimension: null,
                OutputColorSpace.Srgb,
                OutputSharpeningMode.Print));

        Assert.Equal(((uint)6000, (uint)4000), renderedSize);
        Assert.Equal(new Avalonia.PixelSize(6000, 4000), proof.PixelSize);
        AssertAllRowsContainProofPixels(proof);
        AssertAllPixelsMatchFirstPixel(proof);
    }

    private static void AssertAllRowsContainProofPixels(
        Avalonia.Media.Imaging.Bitmap bitmap)
    {
        var pixels = BitmapConversionService.CopyBgraPixels(bitmap);
        var rowBytes = bitmap.PixelSize.Width * 4;
        var populatedRows = 0;
        for (var y = 0; y < bitmap.PixelSize.Height; y++)
        {
            var row = pixels.AsSpan(y * rowBytes, rowBytes);
            for (var offset = 0; offset < row.Length; offset += 4)
            {
                if (row[offset] == 0 &&
                    row[offset + 1] == 0 &&
                    row[offset + 2] == 0) continue;
                populatedRows++;
                break;
            }
        }

        Assert.True(
            populatedRows == bitmap.PixelSize.Height,
            $"Proof populated {populatedRows}/{bitmap.PixelSize.Height} rows.");
    }

    private static void AssertAllPixelsMatchFirstPixel(
        Avalonia.Media.Imaging.Bitmap bitmap)
    {
        var pixels = BitmapConversionService.CopyBgraPixels(bitmap);
        var rowBytes = bitmap.PixelSize.Width * 4;
        var firstPixel = pixels.AsSpan(0, 4);
        for (var offset = 4; offset < rowBytes; offset += 4)
        {
            Assert.True(
                pixels.AsSpan(offset, 4).SequenceEqual(firstPixel),
                $"Proof pixel {offset / 4} differs from the uniform source.");
        }
        var firstRow = pixels.AsSpan(0, rowBytes);
        for (var y = 1; y < bitmap.PixelSize.Height; y++)
        {
            Assert.True(
                pixels.AsSpan(y * rowBytes, rowBytes).SequenceEqual(firstRow),
                $"Proof row {y} differs from the uniform source.");
        }
    }

    public void Dispose() => _root.Dispose();

    private sealed class ProofLoader(
        uint width = 128,
        uint height = 96,
        bool isRaw = false) : IBaseImageLoader
    {
        public int FullLoadCount { get; private set; }

        public bool CanLoad(ImageFile file) => true;

        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.Loaded(CreateBase(decode));

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            FullLoadCount++;
            return CreateBase(decode);
        }

        private BaseImage CreateBase(BaseDecodeSettings decode) => new(
            new MagickImage(MagickColors.Gray, width, height)
            {
                Depth = 16,
                ColorSpace = ColorSpace.RGB
            },
            new BaseImageInfo(
                isRaw ? BaseSourceKind.RawLibRaw : BaseSourceKind.Standard,
                isRaw,
                decode,
                null,
                null,
                6504,
                0,
                false,
                null,
                1,
                checked((int)width),
                checked((int)height)));
    }
}
