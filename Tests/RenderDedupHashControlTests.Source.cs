using System.Runtime.InteropServices;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class RenderDedupHashControlTests
{
    private static readonly double[,] ProfileMatrix =
    {
        { 1.42, -0.31, -0.11 },
        { -0.18, 1.29, -0.11 },
        { 0.03, -0.24, 1.21 }
    };

    [Theory]
    [InlineData("rotate", 7.5, 0, 0, 0, 0)]
    [InlineData("perspective", -2.25, 55, -45, 70, 35)]
    public void GeometryWarp_MatchesFrozenHash(
        string name,
        double rotation,
        int vertical,
        int horizontal,
        int aspect,
        int distortion)
    {
        using var source = CreateImage(Width, Height, 0x2545F491);
        var map = new RenderGeometryMap(Width, Height, rotation, new GeometrySettings
        {
            Vertical = vertical,
            Horizontal = horizontal,
            Aspect = aspect,
            Distortion = distortion
        });

        using var warped = GeometryWarpProcessor.Apply(source, map);
        var hash = ImageHash(warped);

        AssertFrozen($"geometry-{name}", hash);

        foreach (var cap in Caps())
        {
            using var resting = GeometryWarpProcessor.Apply(source, map, Resting(cap));
            Assert.Equal(hash, ImageHash(resting));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public void LensImportCorrected_MatchesFrozenHash(int orientation)
    {
        var samples = Samples(Width, Height, 0x0BADF00D);
        var prescription = LensFixture();
        var (width, height) = LensCorrectionProcessor.GetOutputSize(
            Width, Height, orientation, null, prescription);

        using var image = LensCorrectionProcessor.ImportCorrected(
            MemoryMarshal.AsBytes(samples.AsSpan()),
            Width, Height, width, height, orientation,
            CameraRgbCharacterization.CreateProfile(ProfileMatrix),
            prescription, LensSettings(), CancellationToken.None);

        AssertFrozen($"lens-import-o{orientation}", ImageHash(image));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public void LensWarpMask_MatchesFrozenHash(int orientation)
    {
        using var source = CreateImage(Width, Height, 0x51ED270B);
        var mask = SourceSaturationMask.CaptureEncoded(
            source, 1023, Width, CancellationToken.None);
        var prescription = LensFixture();
        var (width, height) = LensCorrectionProcessor.GetOutputSize(
            Width, Height, orientation, null, prescription);

        var warped = LensCorrectionProcessor.WarpMask(
            mask, width, height, orientation, prescription, LensSettings());

        using var hash = new HashBuilder();
        AddMask(hash, warped);
        AssertFrozen($"lens-warpmask-o{orientation}", hash.Finish());
    }

    [Theory]
    [InlineData("linear", false, false)]
    [InlineData("srgb", true, false)]
    [InlineData("dual", true, true)]
    public void DcpHueSat_MatchesFrozenHash(string name, bool encodeValueAsSrgb, bool dual)
    {
        using var image = CreateImage(Width, Height, 0x7F4A7C15);
        var map = new DcpHueSatMap(
            6, 3, 2, encodeValueAsSrgb,
            DcpProfileReaderTests.CreateTable(6, 3, 2, 8, 1.1f, 0.92f),
            dual ? DcpProfileReaderTests.CreateTable(6, 3, 2, -5, 0.95f, 1.05f) : null,
            dual ? 0.35 : 0);

        DcpHueSatRenderer.Apply(image, map);

        AssertFrozen($"dcp-huesat-{name}", ImageHash(image));
    }

    [Fact]
    public void CameraCharacterization_MatchesFrozenHash()
    {
        var samples = Samples(Width, Height, 0x3C6EF372);
        var cameraFromXyz = new double[,]
        {
            { 0.58, 0.21, 0.08 },
            { 0.14, 0.72, 0.12 },
            { 0.03, 0.11, 0.91 }
        };
        var cases = new (string Name, CameraRgbCharacterization Value)[]
        {
            ("usable", CameraRgbCharacterization.Create(
                new RawCameraFactSnapshot([2, 1, 1.5], ProfileMatrix, [1, 1, 1], null, false))),
            ("derived", CameraRgbCharacterization.Create(
                new RawCameraFactSnapshot([2, 1, 1.5], null, [1, 1, 1], cameraFromXyz, true))),
            ("profile", CameraRgbCharacterization.CreateProfile(ProfileMatrix))
        };

        foreach (var (name, characterization) in cases)
        {
            using var image = characterization.ImportRgb16(
                MemoryMarshal.AsBytes(samples.AsSpan()), Width, Height);
            using var hash = new HashBuilder();
            hash.AddInt32((int)characterization.Outcome);
            hash.AddMatrix(characterization.CameraToRec2020);
            hash.AddBytes(Convert.FromHexString(ImageHash(image)));
            AssertFrozen($"characterization-{name}", hash.Finish());
        }
    }

    [Fact]
    public void DcpMatrixCalculator_MatchesFrozenHash()
    {
        var samples = Samples(Width, Height, 0xA54FF53A);
        var profile = new DcpProfile(
            "Synthetic", "Synthetic Camera",
            new[,] { { 0.71, -0.12, -0.05 }, { -0.41, 1.18, 0.24 }, { -0.08, 0.19, 0.62 } },
            new[,] { { 0.66, -0.09, -0.07 }, { -0.45, 1.25, 0.21 }, { -0.11, 0.23, 0.58 } },
            new[,] { { 0.62, 0.21, 0.13 }, { 0.27, 0.83, -0.10 }, { 0.03, -0.12, 0.92 } },
            null, 17, 21, "hash-control",
            0, 0, 0, 0, false, null, null, new string('b', 64));
        var camera = new DcpCameraData(
            [1.05, 1.0, 0.97],
            new[,] { { 1.0, 0.1, 0.0 }, { 0.0, 0.9, 0.05 }, { 0.05, 0.0, 1.1 } },
            null, null, null, null, "hash-control");
        var resolution = DcpProfileResolution.Success(
            new RawProfileSelection
            {
                Source = RawProfileSource.UserFile,
                Location = "synthetic.dcp",
                ContentHash = profile.ContentHash
            },
            profile);
        var facts = new RawCameraFactSnapshot(
            [2.1, 1, 1.6], ChromaticAdaptation.Identity(), [1, 1, 1], null, false);

        var result = DcpMatrixCalculator.Create(resolution, camera, facts, 5200);

        Assert.True(result.IsActive);
        var inverse = DcpMatrixCalculator.Invert(result.CameraToRec2020!);
        using var image = CameraRgbCharacterization.CreateProfile(result.CameraToRec2020!)
            .ImportRgb16(MemoryMarshal.AsBytes(samples.AsSpan()), Width, Height);
        using var hash = new HashBuilder();
        hash.AddMatrix(result.CameraToRec2020!);
        hash.AddMatrix(inverse);
        hash.AddMatrix(DcpMatrixCalculator.Invert(ProfileMatrix));
        hash.AddBytes(Convert.FromHexString(ImageHash(image)));
        AssertFrozen("dcp-matrix", hash.Finish());
    }

    private static BaseDecodeSettings LensSettings() =>
        BaseDecodeSettings.Default with { Vignetting = true };

    private static LensPrescription LensFixture() => new(
        LensPrescriptionSource.DngOpcode,
        null,
        [
            new LensWarp(
            [
                new LensWarpCoefficients(1, -0.081, 0.012, -0.002, 0.0011, -0.0006),
                new LensWarpCoefficients(1, -0.074, 0.010, -0.001, 0.0011, -0.0006),
                new LensWarpCoefficients(1, -0.067, 0.008, 0.000, 0.0011, -0.0006)
            ], 0.52, 0.47)
        ],
        [new LensVignette(0.31, -0.12, 0.03, 0, 0, 0.5, 0.5)],
        LensFrameWindow.Full,
        LensFrameWindow.Full);
}
