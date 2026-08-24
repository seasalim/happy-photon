using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LensfunPrescriptionReaderTests
{
    [Fact]
    public void MatchedProfileKeepsEveryPresentClass()
    {
        WriteDatabase(Lens(
            "Exact Lens",
            "Mount A",
            """
            <distortion model="poly3" focal="24" k1="0.1"/>
            <tca model="linear" focal="24" kr="1.001" kb="0.999"/>
            <vignetting model="pa" focal="24" aperture="4" distance="1000" k1="-0.2" k2="0" k3="0"/>
            """));
        var result = new LensfunPrescriptionReader(_directory).Read(
            Metadata("Exact Lens"), 6000, 4000);

        var prescription = LensfunPrescriptionReader.Merge(
            null, result.Prescription)!;

        Assert.Equal(LensPrescriptionStatus.Available, result.Status);
        Assert.NotNull(prescription.LensfunDistortion);
        Assert.NotNull(prescription.LensfunTca);
        Assert.NotNull(prescription.LensfunVignette);
        Assert.Equal("LENSFUN", prescription.Summary.Source);
    }

    [Fact]
    public void AmbiguousMatchReturnsNoDataFromRawResolver()
    {
        var dimensions = new LibRawDimensions(
            5472, 3648, 5472, 3648, 5472, 3648, 1);
        var metadata = Metadata(
            "8mm", "Canon", "EOS 6D", "Canon", "EOS 6D", 8, 3.5f);

        var result = RawBaseLoader.ReadLensPrescription(
            new ImageFile("ambiguous.cr2"), metadata, null, dimensions);

        Assert.Equal(LensPrescriptionStatus.None, result.Status);
        Assert.Null(result.Prescription);
    }

    [Fact]
    public void InvalidEmbeddedPrescriptionPropagatesWhenLensfunHasNoMatch()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "invalid.dng");
        File.WriteAllBytes(path, [0]);
        var dimensions = new LibRawDimensions(
            6000, 4000, 6000, 4000, 6000, 4000, 1);

        var result = RawBaseLoader.ReadLensPrescription(
            new ImageFile(path), Metadata("Missing Lens"), null, dimensions);

        Assert.Equal(LensPrescriptionStatus.Invalid, result.Status);
        Assert.Null(result.Prescription);
    }

    [Fact]
    public void ForcedSourceBypassesEmbeddedPrescription()
    {
        var path = GoldenTestPaths.Asset("fujifilm-x30.raf");
        using var context = LibRawContext.Open(path);
        LensfunPrescriptionReader.ForceSource = true;

        var result = RawBaseLoader.ReadLensPrescription(
            new ImageFile(path), context.GetMetadata(), context.GetLensIdentity(),
            context.GetDimensions());

        Assert.Equal(LensPrescriptionStatus.Available, result.Status);
        Assert.Equal(LensPrescriptionSource.Lensfun, result.Prescription!.Source);
        Assert.Null(result.Prescription.FujiTables);
        Assert.Equal("LENSFUN", result.Prescription.Summary.Source);
    }

    [Fact]
    public void ResolutionIsIndependentOfApplicationToggles()
    {
        // Regression: gating the lookup on decode settings let an all-off
        // toggle state erase the capabilities and dead-lock the OPTICS UI.
        var partiallyFilled = new LensPrescription(
            LensPrescriptionSource.FujifilmMakerNote, "Embedded", [], [],
            LensFrameWindow.Full, LensFrameWindow.Full,
            TableWarps: [new LensTableWarp(new LensRadialTable(
                1, [0, 1], [0, 1], 1, 1), null)]);
        var fullyFilled = Prescription(
            "Any",
            distortion: new LensfunDistortion(
                LensfunDistortionModel.Poly3, [0.1], 1, 0.5, 0.5),
            tca: new LensfunTca(
                LensfunTcaModel.Linear, [1.001], [0.999], 1, 0.5, 0.5),
            vignette: new LensfunVignette(-0.2, 0, 0, 1, 0.5, 0.5));

        Assert.True(RawBaseLoader.NeedsLensfun(null));
        Assert.True(RawBaseLoader.NeedsLensfun(partiallyFilled));
        Assert.False(RawBaseLoader.NeedsLensfun(fullyFilled));
    }

    private static LibRawMetadata Metadata(
        string lens,
        string make = "Camera Co",
        string model = "Model One",
        string normalizedMake = "Camera Co",
        string normalizedModel = "Model One",
        float focalLength = 24,
        float aperture = 4) => new(
        make, model, normalizedMake, normalizedModel, lens,
        100, 0.01f, aperture, focalLength, null, null, 1,
        new LibRawGpsFacts(false, null, null, null));
}
