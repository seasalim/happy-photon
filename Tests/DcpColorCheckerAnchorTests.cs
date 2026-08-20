using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(ColorCheckerTestCollection.Name)]
public sealed class DcpColorCheckerAnchorTests
{
    private const double FrozenMeanDeltaE00 = 2.692493210433866;
    private const double FrozenMaximumDeltaE00 = 5.9933530284561485;
    private static readonly double[] FrozenForwardMatrix =
    [
        0.7867939425472178, 0.16500419195015067, 0.012421820350681305,
        0.3831617896040121, 0.7718243429144013, -0.15498617711662652,
        0.0820842903941891, -0.15050988351365302, 0.893635602156614
    ];

    [Fact]
    public void SyntheticDcp_D300ColorCheckerStaysWithinFrozenDeltaEBounds()
    {
        var fixture = Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "nikon-d300-colorchecker.nef");

        using var directory = new TemporaryDirectory();
        var path = SyntheticDcpFactory.WriteTemporary(
            directory.Path,
            new SyntheticDcpOptions
            {
                Name = "Synthetic D300 characterization",
                UniqueCameraModel = "NIKON D300",
                ForwardMatrix1 = FrozenForwardMatrix,
                EmbedPolicy = 3,
                HueSatDimensions = [2, 2, 1],
                HueSatTable1 = DcpProfileReaderTests.CreateTable(
                    2, 2, 1, 0, 1, 1)
            });
        var reader = new DcpProfileReader();
        var snapshot = reader.ReadExternalSnapshot(path);
        var parsed = reader.ParseExternal(snapshot, "synthetic");
        var selection = new RawProfileSelection
        {
            Source = RawProfileSource.UserFile,
            Location = path,
            ContentHash = snapshot.ContentHash
        };
        var decode = BaseDecodeSettings.From(new EditSettings
        {
            RawProfile = selection
        }).WithProfileResolution(DcpProfileResolution.Success(selection, parsed));

        var loader = new RawBaseLoader();
        using var activeBase = loader.LoadFullBase(
            new ImageFile(fixture),
            decode,
            CancellationToken.None);
        using var builtInBase = loader.LoadFullBase(
            new ImageFile(fixture),
            BaseDecodeSettings.Default,
            CancellationToken.None);
        Assert.NotNull(activeBase);
        Assert.NotNull(builtInBase);
        Assert.Equal(DcpProfileErrorCode.None, activeBase!.Info.ProfileStatus);
        Assert.Equal(decode.ProfileResolution!.Token, activeBase.Info.ProfileToken);
        Assert.Equal(activeBase.Info.ProfileToken, activeBase.Info.DcpProfile?.Token);
        Assert.NotNull(activeBase.Info.DcpProfile?.HueSatMap);
        Assert.NotNull(parsed.ForwardMatrix1);
        Assert.NotEqual(
            RawBaseLoaderTestSupport.PixelHash(builtInBase!.Pixels),
            RawBaseLoaderTestSupport.PixelHash(activeBase.Pixels));

        var measurement = ColorCheckerGroundTruthTests.Measure(decode);

        Assert.True(
            Math.Abs(measurement.MeanDeltaE00 - FrozenMeanDeltaE00) <= 0.02 &&
            Math.Abs(measurement.MaximumDeltaE00 - FrozenMaximumDeltaE00) <= 0.02,
            $"Synthetic DCP observation changed: mean={measurement.MeanDeltaE00:R}; " +
            $"maximum={measurement.MaximumDeltaE00:R}.");
    }

}
