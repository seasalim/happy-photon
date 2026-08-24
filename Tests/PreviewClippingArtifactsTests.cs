using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class PreviewClippingArtifactsTests : IDisposable
{
    private readonly AvaloniaTestFixture _fixture;
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-preview-clipping-{Guid.NewGuid():N}")).FullName;

    public PreviewClippingArtifactsTests(AvaloniaTestFixture fixture)
    {
        _fixture = fixture;
    }

    [WindowsFact]
    public async Task UnlatchedRequestIsMaskFreeAndStandardRequestIncludesHighlights()
    {
        // Guard the service layer against stripping the standard-source high side.
        _fixture.RequireWindows();
        using var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        await catalog.InitializeAsync();
        await using var service = new PreviewService(
            catalog,
            new SolidLoader(isRaw: false, MagickColors.White),
            new RenderPipeline());
        var image = new ImageFile(Path.Combine(_root, "standard.jpg"));
        var request = ThumbnailSizeRequest.For(BrowseThumbnailSize.Medium);

        using var unlatched = await service.ApplyEditsToPreviewArtifactsAsync(
            image,
            new EditSettings(),
            request,
            skipHistogram: true,
            ClippingOverlaySide.None);

        Assert.NotNull(unlatched.Bitmap);
        Assert.Null(unlatched.Clipping);
        Assert.Null(unlatched.ClippingMask);

        using var latched = await service.ApplyEditsToPreviewArtifactsAsync(
            image,
            new EditSettings(),
            request,
            skipHistogram: true,
            ClippingOverlaySide.Both);

        Assert.NotNull(latched.Clipping);
        Assert.False(latched.IsRawSource);
        Assert.Equal(
            ClippingOverlaySide.Both,
            latched.ClippingMask!.Sides);
        Assert.All(
            latched.ClippingMask.Flags.ToArray(),
            flag => Assert.Equal(
                (byte)ClippingOverlaySide.Highlights,
                flag));
    }

    [WindowsFact]
    public async Task CarrierDisposesBitmapAndSemanticMaskTogether()
    {
        _fixture.RequireWindows();
        using var catalog = new CatalogService(Path.Combine(_root, "lifetime"));
        await catalog.InitializeAsync();
        await using var service = new PreviewService(
            catalog,
            new SolidLoader(isRaw: true, MagickColors.White),
            new RenderPipeline());
        var image = new ImageFile(Path.Combine(_root, "raw.dng"));
        var artifacts = await service.ApplyEditsToPreviewArtifactsAsync(
            image,
            new EditSettings(),
            ThumbnailSizeRequest.For(BrowseThumbnailSize.Medium),
            skipHistogram: true,
            ClippingOverlaySide.Both);
        var bitmap = artifacts.Bitmap!;
        var mask = artifacts.ClippingMask!;

        artifacts.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = bitmap.PixelSize);
        Assert.Throws<ObjectDisposedException>(() => _ = mask.Flags.Length);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    private sealed class SolidLoader(bool isRaw, MagickColor color)
        : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        public BaseImage LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            Create(decode);

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            var sourceSaturation = new SourceSaturationMask(12, 8);
            for (var y = 0; y < sourceSaturation.Height; y++)
            for (var x = 0; x < sourceSaturation.Width; x++)
            {
                sourceSaturation.SetFlags(x, y, 7);
            }
            return BaseImageLoadOutcome.Loaded(
                new PreviewBasePair(Create(decode), large: null),
                new PreviewSourceAnalysis(null, sourceSaturation));
        }

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private BaseImage Create(BaseDecodeSettings decode)
        {
            return new BaseImage(
                new MagickImage(color, 12, 8)
                {
                    ColorSpace = ColorSpace.RGB,
                    Depth = 16
                },
                new BaseImageInfo(
                    isRaw ? BaseSourceKind.RawLibRaw : BaseSourceKind.Standard,
                    isRaw,
                    decode,
                    null,
                    null,
                    isRaw ? 5500 : 6504,
                    0,
                    false,
                    null,
                    1,
                    12,
                    8));
        }
    }
}
