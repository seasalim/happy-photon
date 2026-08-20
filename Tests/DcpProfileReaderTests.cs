using System.Buffers.Binary;
using System.Security.Cryptography;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DcpProfileReaderTests
{
    [Fact]
    public void ParseExternal_ReadsFullSupportedSetAndDefaults()
    {
        var bytes = SyntheticDcpFactory.Create(new SyntheticDcpOptions
        {
            Name = "Synthetic dual profile",
            UniqueCameraModel = "Canon EOS 6D",
            ColorMatrix2 = ScaleIdentity(2),
            ForwardMatrix1 = D50Forward(1),
            ForwardMatrix2 = D50Forward(2),
            Illuminant1 = 17,
            Illuminant2 = 21,
            CalibrationSignature = "synthetic-signature",
            HueSatDimensions = [2, 2, 2],
            HueSatTable1 = CreateTable(2, 2, 2, 0, 1, 1),
            HueSatTable2 = CreateTable(2, 2, 2, 10, 1.2f, 0.9f)
        });
        var snapshot = new DcpExternalSnapshot(bytes, Hash(bytes));

        var profile = new DcpProfileReader().ParseExternal(snapshot, "fallback");

        Assert.Equal("Synthetic dual profile", profile.Name);
        Assert.Equal("Canon EOS 6D", profile.UniqueCameraModel);
        Assert.Equal(17, profile.CalibrationIlluminant1);
        Assert.Equal(21, profile.CalibrationIlluminant2);
        Assert.Equal("synthetic-signature", profile.CalibrationSignature);
        Assert.Equal(0, profile.EmbedPolicy);
        Assert.False(profile.EncodeValueAsSrgb);
        Assert.Equal([2, 2, 2],
            [profile.HueDivisions, profile.SaturationDivisions, profile.ValueDivisions]);
        Assert.NotNull(profile.ColorMatrix2);
        Assert.NotNull(profile.ForwardMatrix1);
        Assert.NotNull(profile.ForwardMatrix2);
        Assert.NotNull(profile.HueSatTable1);
        Assert.NotNull(profile.HueSatTable2);
        Assert.Equal(snapshot.ContentHash, profile.ContentHash);
    }

    [Fact]
    public void ParseExternal_UsesNormativeOptionalDefaults()
    {
        var bytes = SyntheticDcpFactory.Create(new SyntheticDcpOptions
        {
            Name = null,
            Illuminant1 = 21
        });

        var profile = new DcpProfileReader().ParseExternal(
            new DcpExternalSnapshot(bytes, Hash(bytes)),
            "fallback-name");

        Assert.Equal("fallback-name", profile.Name);
        Assert.Null(profile.UniqueCameraModel);
        Assert.Equal(0, profile.EmbedPolicy);
        Assert.Equal(0, profile.HueDivisions);
        Assert.False(profile.EncodeValueAsSrgb);
        Assert.Null(profile.ForwardMatrix1);
        Assert.Null(profile.HueSatTable1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    public void ParseExternal_RejectsUnknownIlluminant(int illuminant)
    {
        var bytes = SyntheticDcpFactory.Create(
            new SyntheticDcpOptions { Illuminant1 = illuminant });

        var exception = Assert.Throws<DcpProfileException>(() =>
            Parse(bytes));

        Assert.Equal(DcpProfileErrorCode.UnknownIlluminant, exception.Code);
    }

    [Fact]
    public void ParseExternal_RejectsMissingMandatoryColorMatrix()
    {
        var bytes = SyntheticDcpFactory.Create(
            new SyntheticDcpOptions { IncludeColorMatrix1 = false });

        var exception = Assert.Throws<DcpProfileException>(() => Parse(bytes));

        Assert.Equal(DcpProfileErrorCode.MissingMandatoryTag, exception.Code);
    }

    [Fact]
    public void ParseExternal_RejectsUnpairedDualIlluminantTags()
    {
        var bytes = SyntheticDcpFactory.Create(new SyntheticDcpOptions
        {
            ColorMatrix2 = ScaleIdentity(2),
            Illuminant2 = null
        });

        var exception = Assert.Throws<DcpProfileException>(() => Parse(bytes));

        Assert.Equal(DcpProfileErrorCode.MissingMandatoryTag, exception.Code);
    }

    [Theory]
    [InlineData(0, 2, 1)]
    [InlineData(2, 1, 1)]
    [InlineData(2, 2, 0)]
    public void ParseExternal_RejectsBadHueSatDimensions(uint h, uint s, uint v)
    {
        var bytes = SyntheticDcpFactory.Create(new SyntheticDcpOptions
        {
            HueSatDimensions = [h, s, v],
            HueSatTable1 = []
        });

        var exception = Assert.Throws<DcpProfileException>(() => Parse(bytes));

        Assert.Equal(DcpProfileErrorCode.InvalidDimensions, exception.Code);
    }

    [Fact]
    public void ParseExternal_RejectsUnsupportedHueSatEncoding()
    {
        var bytes = SyntheticDcpFactory.Create(new SyntheticDcpOptions
        {
            HueSatDimensions = [2, 2, 2],
            HueSatTable1 = CreateTable(2, 2, 2, 0, 1, 1),
            HueSatEncoding = 2
        });

        var exception = Assert.Throws<DcpProfileException>(() => Parse(bytes));

        Assert.Equal(DcpProfileErrorCode.UnsupportedVariant, exception.Code);
    }

    [Fact]
    public void ParseExternal_IgnoresEncodingForTwoAndAHalfDimensionalMap()
    {
        var bytes = SyntheticDcpFactory.Create(new SyntheticDcpOptions
        {
            HueSatDimensions = [2, 2, 1],
            HueSatTable1 = CreateTable(2, 2, 1, 0, 1, 1),
            HueSatEncoding = 99
        });

        var profile = Parse(bytes);

        Assert.False(profile.EncodeValueAsSrgb);
    }

    [Fact]
    public void ParseExternal_RejectsSparseOffsetsBeforeAllocation()
    {
        var bytes = SyntheticDcpFactory.Create();
        var colorEntry = FindEntry(bytes, 50721);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(colorEntry + 8, 4),
            uint.MaxValue);

        var exception = Assert.Throws<DcpProfileException>(() => Parse(bytes));

        Assert.Equal(DcpProfileErrorCode.InvalidContainer, exception.Code);
    }

    [Fact]
    public void ParseExternal_RejectsCyclicIfd()
    {
        var bytes = SyntheticDcpFactory.Create(
            new SyntheticDcpOptions { CyclicIfd = true });

        var exception = Assert.Throws<DcpProfileException>(() => Parse(bytes));

        Assert.Equal(DcpProfileErrorCode.InvalidContainer, exception.Code);
    }

    [Fact]
    public void ReadExternalSnapshot_RejectsOverFourMiBBeforeRead()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "oversized.dcp");
        using (var stream = File.Create(path))
        {
            stream.SetLength(DcpProfileReader.MaximumExternalBytes + 1L);
        }

        var exception = Assert.Throws<DcpProfileException>(() =>
            new DcpProfileReader().ReadExternalSnapshot(path));

        Assert.Equal(DcpProfileErrorCode.TooLarge, exception.Code);
    }

    [Fact]
    public void ReadExternalUniqueCameraModel_DoesNotReadHueSatPayload()
    {
        using var directory = new TemporaryDirectory();
        var bytes = SyntheticDcpFactory.Create(new SyntheticDcpOptions
        {
            UniqueCameraModel = "Canon EOS 6D",
            HueSatDimensions = [2, 2, 2],
            HueSatTable1 = CreateTable(2, 2, 2, 0, 1, 1)
        });
        var tableEntry = FindEntry(bytes, 50938);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(tableEntry + 8, 4),
            uint.MaxValue);
        var path = Path.Combine(directory.Path, "sparse-table.dcp");
        File.WriteAllBytes(path, bytes);

        var model = new DcpProfileReader().ReadExternalUniqueCameraModel(path);

        Assert.Equal("Canon EOS 6D", model);
    }

    [Fact]
    public void ReadCameraData_ReadsCalibrationBalanceReductionAndNeutral()
    {
        using var directory = new TemporaryDirectory();
        var path = SyntheticDcpFactory.WriteTemporary(
            directory.Path,
            new SyntheticDcpOptions
            {
                CameraCalibration1 = ScaleIdentity(1.1),
                CameraCalibration2 = ScaleIdentity(0.9),
                ReductionMatrix1 = ScaleIdentity(0.8),
                ReductionMatrix2 = ScaleIdentity(0.7),
                AnalogBalance = [2, 1, 0.5],
                AsShotNeutral = [0.4, 1, 0.7],
                CameraCalibrationSignature = "camera-signature"
            },
            "camera.dng");

        var data = new DcpProfileReader().ReadCameraData(path);

        Assert.Equal([2, 1, 0.5], data.AnalogBalance!);
        Assert.Equal([0.4, 1, 0.7], data.AsShotNeutral!);
        Assert.Equal(1.1, data.CameraCalibration1![0, 0], 6);
        Assert.Equal(0.9, data.CameraCalibration2![1, 1], 6);
        Assert.Equal(0.8, data.ReductionMatrix1![2, 2], 6);
        Assert.Equal(0.7, data.ReductionMatrix2![0, 0], 6);
        Assert.Equal("camera-signature", data.CalibrationSignature);
    }

    [Fact]
    public void ReadEmbeddedProfiles_ReadsExtraProfileIfdOffsetsInPlace()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "extra-profiles.dng");
        File.WriteAllBytes(path, SyntheticDcpFactory.CreateDngWithExtraProfile(
            new SyntheticDcpOptions { Name = "Primary" },
            new SyntheticDcpOptions
            {
                Name = "Extra",
                ColorMatrix1 = ScaleIdentity(1.2)
            }));

        var profiles = new DcpProfileReader().ReadEmbeddedProfiles(path);

        Assert.Equal(["Primary", "Extra"],
            profiles.Select(profile => profile.Name));
        Assert.Equal(1.2, profiles[1].ColorMatrix1[0, 0], 6);
    }

    [Fact]
    public void ParseExternal_RejectsHueSatTableAboveEntryCap()
    {
        var bytes = SyntheticDcpFactory.Create(new SyntheticDcpOptions
        {
            HueSatDimensions = [1024, 1024, 2],
            HueSatTable1 = []
        });

        var exception = Assert.Throws<DcpProfileException>(() => Parse(bytes));

        Assert.Equal(DcpProfileErrorCode.TooLarge, exception.Code);
    }

    [Theory]
    [InlineData(52529)]
    [InlineData(52533)]
    [InlineData(52551)]
    public void ParseExternal_ExplicitlyRejectsUnsupportedProfileVariants(
        ushort tag)
    {
        var bytes = SyntheticDcpFactory.Create(new SyntheticDcpOptions
        {
            ExtraLongTag = tag
        });

        var exception = Assert.Throws<DcpProfileException>(() => Parse(bytes));

        Assert.Equal(DcpProfileErrorCode.UnsupportedVariant, exception.Code);
    }

    private static DcpProfile Parse(byte[] bytes) =>
        new DcpProfileReader().ParseExternal(
            new DcpExternalSnapshot(bytes, Hash(bytes)),
            "synthetic");

    private static int FindEntry(byte[] bytes, ushort tag)
    {
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8, 2));
        for (var index = 0; index < count; index++)
        {
            var offset = 10 + index * 12;
            if (BinaryPrimitives.ReadUInt16LittleEndian(
                    bytes.AsSpan(offset, 2)) == tag)
            {
                return offset;
            }
        }
        throw new InvalidOperationException($"Synthetic tag {tag} not found.");
    }

    internal static float[] CreateTable(
        int hueDivisions,
        int saturationDivisions,
        int valueDivisions,
        float hueShift,
        float saturationScale,
        float valueScale)
    {
        var result = new float[hueDivisions * saturationDivisions * valueDivisions * 3];
        for (var value = 0; value < valueDivisions; value++)
        for (var saturation = 0; saturation < saturationDivisions; saturation++)
        for (var hue = 0; hue < hueDivisions; hue++)
        {
            var index = ((value * hueDivisions + hue) *
                saturationDivisions + saturation) * 3;
            result[index] = hueShift;
            result[index + 1] = saturationScale;
            result[index + 2] = saturation == 0 ? 1 : valueScale;
        }
        return result;
    }

    internal static double[] ScaleIdentity(double value) =>
        [value, 0, 0, 0, value, 0, 0, 0, value];

    internal static double[] D50Forward(double scale) =>
        [0.96422 * scale, 0, 0, 0, 1 * scale, 0, 0, 0, 0.82521 * scale];

    private static string Hash(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}
