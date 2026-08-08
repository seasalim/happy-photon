using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class GatedBaseImageLoaderTests
{
    [Theory]
    [InlineData((int)SourceAvailability.AvailableLocally, 1)]
    [InlineData((int)SourceAvailability.Unknown, 1)]
    [InlineData((int)SourceAvailability.RequiresHydration, 0)]
    [InlineData((int)SourceAvailability.Unavailable, 0)]
    public void BackgroundIntent_EnforcesLiveAvailability(
        int availabilityValue,
        int expectedCalls)
    {
        var availability = (SourceAvailability)availabilityValue;
        var inner = new CountingLoader();
        var gated = new GatedBaseImageLoader(
            inner,
            new TestSourceAvailabilityService(availability));

        gated.LoadPreviewBase(
            new ImageFile("photo.jpg"),
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.Equal(expectedCalls, inner.PreviewLoads);
    }

    [Fact]
    public void UserApproval_AllowsOnlyHydrationRequiredSource()
    {
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.RequiresHydration);
        var inner = new CountingLoader();
        var gated = new GatedBaseImageLoader(inner, availability);
        var image = new ImageFile("photo.jpg");

        gated.LoadFullBase(
            image,
            BaseDecodeSettings.Default,
            SourceReadIntent.UserApprovedHydration,
            CancellationToken.None);
        availability.Availability = SourceAvailability.Unavailable;
        gated.LoadFullBase(
            image,
            BaseDecodeSettings.Default,
            SourceReadIntent.UserApprovedHydration,
            CancellationToken.None);

        Assert.Equal(1, inner.FullLoads);
    }

    [Fact]
    public void CanLoad_ReportsFormatSupportWithoutAvailabilityCheck()
    {
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.RequiresHydration);
        var gated = new GatedBaseImageLoader(
            new CountingLoader(),
            availability);

        Assert.True(gated.CanLoad(new ImageFile("photo.jpg")));
        Assert.Equal(0, availability.CallCount);
    }

    [Fact]
    public async Task ImageService_WrapsInjectedLoader()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-gated-loader-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var catalog = new CatalogService(Path.Combine(root, "catalog"));
            await catalog.InitializeAsync();
            var inner = new CountingLoader();
            await using var service = new ImageService(
                catalog,
                inner,
                new TestSourceAvailabilityService(
                    SourceAvailability.RequiresHydration));

            var (preview, _) = await service.LoadPreviewWithHistogramAsync(
                new ImageFile(Path.Combine(root, "photo.jpg")),
                new EditSettings(),
                skipHistogram: true);
            preview?.Dispose();

            Assert.Equal(0, inner.PreviewLoads);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GateBlocksDefaultRouterBeforeFormatLoaderSelection()
    {
        var raw = new CountingLoader();
        var standard = new CountingLoader();
        var gated = new GatedBaseImageLoader(
            new BaseLoaderRouter(raw, standard),
            new TestSourceAvailabilityService(
                SourceAvailability.RequiresHydration));

        gated.LoadPreviewBase(
            new ImageFile("photo.dng"),
            BaseDecodeSettings.Default,
            CancellationToken.None);
        gated.LoadPreviewBase(
            new ImageFile("photo.jpg"),
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.Equal(0, raw.PreviewLoads);
        Assert.Equal(0, standard.PreviewLoads);
    }

    private sealed class CountingLoader : IBaseImageLoader
    {
        internal int PreviewLoads { get; private set; }
        internal int FullLoads { get; private set; }

        public bool CanLoad(ImageFile file) => true;

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            PreviewLoads++;
            return null;
        }

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            FullLoads++;
            return null;
        }
    }
}
