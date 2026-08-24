using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LensIdentityIntegrationTests
{
    [Theory]
    [InlineData("nikon-d70-burst-1.nef", "Nikon AF Nikkor 50mm f/1.8D")]
    [InlineData("nikon-d70-burst-2.nef", "Nikon AF Nikkor 50mm f/1.8D")]
    [InlineData("nikon-d300-colorchecker.nef", "Nikon AF-S Nikkor 70-200mm f/2.8G ED VR II 162")]
    public void CommittedNikonFixturesReachLensfunThroughMakerNoteIdentity(
        string fixture,
        string expectedLens)
    {
        var path = GoldenTestPaths.Asset(fixture);
        using var context = LibRawContext.Open(path);
        var metadata = context.GetMetadata();
        var identity = context.GetLensIdentity();
        var dimensions = context.GetDimensions();
        var resolved = new LensIdentityResolver().Resolve(
            metadata.NormalizedMake ?? metadata.Make, identity);

        var result = RawBaseLoader.ReadLensPrescription(
            new ImageFile(path), metadata, identity, dimensions);

        Assert.NotNull(identity);
        Assert.True(result.Status == LensPrescriptionStatus.Available,
            $"make={metadata.Make}; normalizedMake={metadata.NormalizedMake}; " +
            $"model={metadata.Model}; normalizedModel={metadata.NormalizedModel}; " +
            $"exifLens={metadata.Lens}; id={identity!.LensId:X16}; resolved={resolved}");
        Assert.Equal(expectedLens, result.Prescription!.LensName);
    }

    [Fact]
    public void MissingIdentityKeepsGenericNikonDescriptorAtNoData()
    {
        var metadata = new LibRawMetadata(
            "NIKON CORPORATION", "NIKON D70", "Nikon", "D70",
            "50.0 mm f/1.8", 200, 0.01f, 2.8f, 50, null, null, 1,
            new LibRawGpsFacts(false, null, null, null));

        var result = RawBaseLoader.ReadLensPrescription(
            new ImageFile("missing.nef"), metadata, null,
            new LibRawDimensions(0, 0, 3008, 2000, 0, 0, 1));

        Assert.Equal(LensPrescriptionStatus.None, result.Status);
    }
}
