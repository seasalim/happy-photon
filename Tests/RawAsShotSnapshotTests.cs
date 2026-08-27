using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;
using static HappyPhoton.Tests.RawBaseLoaderTestSupport;

namespace HappyPhoton.Tests;

public sealed class RawAsShotSnapshotTests
{
    [Theory]
    [InlineData("canon-eos-6d-iso-6400.cr2", 4381.3147868759042, 3.8983677058133104)]
    [InlineData("nikon-d300-colorchecker.nef", 4308.7514297285525, -34.340535566293575)]
    public void ExtendedFixtureAsShotWhiteBalance_MatchesPreR5aSnapshot(
        string fileName,
        double expectedKelvin,
        double expectedTint)
    {
        using var image = new RawBaseLoader().LoadPreviewBase(
            new ImageFile(Asset(fileName)),
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.NotNull(image);
        Assert.Equal(expectedKelvin, image!.Info.AsShotKelvin);
        Assert.Equal(expectedTint, image.Info.AsShotTint);
    }
}
