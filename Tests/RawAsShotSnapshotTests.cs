using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;
using static HappyPhoton.Tests.RawBaseLoaderTestSupport;

namespace HappyPhoton.Tests;

public sealed class RawAsShotSnapshotTests
{
    [Theory]
    [InlineData("canon-eos-350d.cr2", 4624.520237776831, 11.157327608666723)]
    [InlineData("nikon-d70-burst-1.nef", 4651.4002190626215, -33.671729728178555)]
    [InlineData("fujifilm-x30.raf", 4975.442200153131, -23.507197134216007)]
    [InlineData("pentax-k-r.dng", 6057.343948228288, 49.92973780194121)]
    public void AsShotWhiteBalance_MatchesPreR5aSnapshot(
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
