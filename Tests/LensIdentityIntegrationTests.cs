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
        Assert.Equal(LibRawLensMounts.NikonF, identity!.LensMount);
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

    [Fact]
    public void ExifLensMatchShortCircuitsMakerNoteCandidates()
    {
        var result = Read(
            "Nikon AF-S Nikkor 70-200mm f/2.8G ED VR II 162",
            Identity(0x7658505014147A02, "AF Nikkor 50mm f/1.8D"));

        Assert.Equal(
            "Nikon AF-S Nikkor 70-200mm f/2.8G ED VR II 162",
            result.Prescription?.LensName);
    }

    [Fact]
    public void TransmittedLensNameWinsBeforeIdDerivedName()
    {
        var result = Read(
            "Missing EXIF lens",
            Identity(
                0x7658505014147A02,
                "AF-S Nikkor 70-200mm f/2.8G ED VR II"));

        Assert.Equal(
            "Nikon AF-S Nikkor 70-200mm f/2.8G ED VR II 162",
            result.Prescription?.LensName);
    }

    [Fact]
    public void FailedTransmittedLensNameFallsBackToIdDerivedName()
    {
        var result = Read(
            "Missing EXIF lens",
            Identity(0xA2485C802424A40E, "Missing transmitted lens"));

        Assert.Equal(
            "Nikon AF-S Nikkor 70-200mm f/2.8G ED VR II 162",
            result.Prescription?.LensName);
    }

    [Fact]
    public void MissingTransmittedAndIdDerivedMatchesStayNoData()
    {
        var result = Read(
            "Missing EXIF lens",
            Identity(ulong.MaxValue, "Missing transmitted lens"));

        Assert.Equal(LensPrescriptionStatus.None, result.Status);
        Assert.Null(result.Prescription);
    }

    private static LensPrescriptionReadResult Read(
        string exifLens,
        LibRawLensIdentity identity)
    {
        var metadata = new LibRawMetadata(
            "NIKON CORPORATION", "NIKON D300", "Nikon", "D300",
            exifLens, 100, 0.01f, 2.8f, 70, null, null, 1,
            new LibRawGpsFacts(false, null, null, null));
        return RawBaseLoader.ReadLensPrescription(
            new ImageFile("missing.nef"), metadata, identity,
            new LibRawDimensions(0, 0, 4288, 2848, 0, 0, 1));
    }

    private static LibRawLensIdentity Identity(ulong id, string? lens) => new(
        id, lens, 0, LibRawLensMounts.NikonF, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, null, 0, null, 0, null);
}
