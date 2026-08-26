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
    }

    public void Dispose() => _root.Dispose();

    private sealed class ProofLoader : IBaseImageLoader
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

        private static BaseImage CreateBase(BaseDecodeSettings decode) => new(
            new MagickImage(MagickColors.Gray, 128, 96)
            {
                Depth = 16,
                ColorSpace = ColorSpace.RGB
            },
            new BaseImageInfo(
                BaseSourceKind.Standard,
                false,
                decode,
                null,
                null,
                6504,
                0,
                false,
                null,
                1,
                128,
                96));
    }
}
