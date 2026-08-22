using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class MonochromePreviewArtifactsTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("mono-artifacts");

    [Fact]
    public async Task FreshPreviewArtifacts_CarryMonochromeCapability()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var service = new PreviewService(
            catalog,
            new MonochromeLoader(),
            new RenderPipeline());

        using var artifacts = await service.LoadPreviewArtifactsAsync(
            new ImageFile(_fixture.Path("mono.dng")),
            new EditSettings(),
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Medium),
            skipHistogram: true,
            ClippingOverlaySide.None);

        Assert.NotNull(artifacts.Bitmap);
        Assert.True(artifacts.IsRawSource);
        Assert.True(artifacts.IsMonochrome);
    }

    public void Dispose() => _fixture.Dispose();

    private sealed class MonochromeLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) => BaseImageLoadOutcome.Loaded(
                new PreviewBasePair(Create(decode), large: null),
                PreviewSourceAnalysis.Empty);

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) => Create(decode);

        private static BaseImage Create(BaseDecodeSettings decode) => new(
            new MagickImage(MagickColors.Gray, 16, 12)
            {
                ColorSpace = ColorSpace.RGB,
                Depth = 16
            },
            new BaseImageInfo(
                BaseSourceKind.RawLibRaw,
                IsRawSource: true,
                decode,
                CamMul: null,
                CamToSrgb: null,
                AsShotKelvin: 5500,
                AsShotTint: 0,
                HadIccProfile: false,
                IccDescription: null,
                ExifOrientationApplied: 1,
                FullWidth: 16,
                FullHeight: 12)
            {
                IsMonochrome = true
            });
    }
}
