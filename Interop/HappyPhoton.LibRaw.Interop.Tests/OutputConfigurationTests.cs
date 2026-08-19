using Xunit;

namespace HappyPhoton.LibRaw.Interop.Tests;

public sealed class OutputConfigurationTests
{
    [Theory]
    [InlineData(LibRawHighlightMode.Blend, LibRawFbddMode.Off, true, 2, 0)]
    [InlineData(LibRawHighlightMode.Blend, LibRawFbddMode.Light, false, 2, 1)]
    [InlineData(LibRawHighlightMode.Blend, LibRawFbddMode.Full, true, 2, 2)]
    [InlineData(LibRawHighlightMode.Clip, LibRawFbddMode.Off, false, 0, 0)]
    [InlineData(LibRawHighlightMode.Clip, LibRawFbddMode.Light, true, 0, 1)]
    [InlineData(LibRawHighlightMode.Clip, LibRawFbddMode.Full, false, 0, 2)]
    public void Linear_PinsDeterministicParameters(LibRawHighlightMode highlight,
        LibRawFbddMode noiseReduction, bool preview, int expectedHighlight, int expectedFbdd)
    {
        var value = LibRawOutputConfiguration.Linear(highlight, noiseReduction, preview);

        Assert.Equal(LibRawOutputConfiguration.Version, value.AbiVersion);
        Assert.Equal(16, value.OutputBits);
        Assert.Equal(1, value.OutputColor);
        Assert.Equal(1, value.GammaPower);
        Assert.Equal(1, value.GammaSlope);
        Assert.True(value.NoAutoBright);
        Assert.Equal(preview, value.HalfSize);
        Assert.Equal(expectedHighlight, value.HighlightMode);
        Assert.Equal(expectedFbdd, value.FbddNoiseReduction);
        Assert.True(value.UseCameraWhiteBalance);
        Assert.False(value.UseAutoWhiteBalance);
        Assert.True(value.UseCameraMatrix);
        Assert.Equal([0f, 0f, 0f, 0f], Multipliers(value));
    }

    [Fact]
    public void LinearRec2020_ChangesOnlyTheOutputSpace()
    {
        var srgb = LibRawOutputConfiguration.Linear(
            LibRawHighlightMode.Clip,
            LibRawFbddMode.Light,
            halfSize: true);
        var rec2020 = LibRawOutputConfiguration.LinearRec2020(
            LibRawHighlightMode.Clip,
            LibRawFbddMode.Light,
            halfSize: true);

        Assert.Equal(1, srgb.OutputColor);
        Assert.Equal(8, rec2020.OutputColor);
        Assert.Equal(srgb with { OutputColor = 8 }, rec2020);
    }

    [Fact]
    public void FullDecodeSrgb_PinsLegacyEightBitGammaParameters()
    {
        var value = LibRawOutputConfiguration.FullDecodeSrgb();

        Assert.Equal(LibRawOutputConfiguration.Version, value.AbiVersion);
        Assert.Equal(8, value.OutputBits);
        Assert.Equal(1, value.OutputColor);
        Assert.Equal(1.0 / 2.4, value.GammaPower);
        Assert.Equal(12.92, value.GammaSlope);
        Assert.False(value.NoAutoBright);
        Assert.False(value.HalfSize);
        Assert.Equal(0, value.HighlightMode);
        Assert.Equal(0, value.FbddNoiseReduction);
        Assert.True(value.UseCameraWhiteBalance);
        Assert.False(value.UseAutoWhiteBalance);
        Assert.True(value.UseCameraMatrix);
        Assert.Equal([0f, 0f, 0f, 0f], Multipliers(value));
    }

    [Fact]
    public unsafe void OptionalAbiV3Fields_MapPresenceAndValuesExplicitly()
    {
        var value = LibRawOutputConfiguration.Linear(
            LibRawHighlightMode.Clip, LibRawFbddMode.Off, halfSize: false) with
        {
            UserSaturation = 65535,
            UserQuality = LibRawDemosaicQuality.Dht,
            CropBox = new(7, 9, 101, 103)
        };

        value.Validate();
        var native = NativeApi.ToNative(value);

        Assert.Equal(65535, native.UserSaturation);
        Assert.Equal(1u, native.UserQualityPresent);
        Assert.Equal(11, native.UserQuality);
        Assert.Equal(1u, native.CropBoxPresent);
        Assert.Equal(new uint[] { 7, 9, 101, 103 },
            new ReadOnlySpan<uint>(native.CropBox, 4).ToArray());
    }

    [Fact]
    public unsafe void OptionalAbiV3Fields_DefaultToNativeAbsence()
    {
        var value = LibRawOutputConfiguration.Linear(
            LibRawHighlightMode.Clip, LibRawFbddMode.Off, halfSize: false);

        var native = NativeApi.ToNative(value);

        Assert.Null(value.UserSaturation);
        Assert.Null(value.UserQuality);
        Assert.Null(value.CropBox);
        Assert.Equal(0, native.UserSaturation);
        Assert.Equal(0u, native.UserQualityPresent);
        Assert.Equal(0, native.UserQuality);
        Assert.Equal(0u, native.CropBoxPresent);
        Assert.Equal(new uint[4], new ReadOnlySpan<uint>(native.CropBox, 4).ToArray());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public void UserSaturation_RejectsUnrepresentableValues(int saturation)
    {
        var value = LibRawOutputConfiguration.Linear(
            LibRawHighlightMode.Clip, LibRawFbddMode.Off, halfSize: false) with
        {
            UserSaturation = saturation
        };

        Assert.Throws<ArgumentException>(value.Validate);
    }

    [Fact]
    public void UserQuality_RejectsUnnamedRequests()
    {
        var value = LibRawOutputConfiguration.Linear(
            LibRawHighlightMode.Clip, LibRawFbddMode.Off, halfSize: false) with
        {
            UserQuality = (LibRawDemosaicQuality)5
        };

        Assert.Throws<ArgumentException>(value.Validate);
    }

    [Theory]
    [InlineData(LibRawDemosaicQuality.Linear)]
    [InlineData(LibRawDemosaicQuality.Vng)]
    [InlineData(LibRawDemosaicQuality.Ppg)]
    [InlineData(LibRawDemosaicQuality.Ahd)]
    [InlineData(LibRawDemosaicQuality.Dcb)]
    [InlineData(LibRawDemosaicQuality.Dht)]
    [InlineData(LibRawDemosaicQuality.Aahd)]
    public void UserQuality_AcceptsEveryNamedRequest(LibRawDemosaicQuality quality)
    {
        var value = LibRawOutputConfiguration.Linear(
            LibRawHighlightMode.Clip, LibRawFbddMode.Off, halfSize: false) with
        {
            UserQuality = quality
        };

        value.Validate();
    }

    [Fact]
    public void CropBox_RejectsEmptyOrNonRepresentableCoordinates()
    {
        var baseline = LibRawOutputConfiguration.Linear(
            LibRawHighlightMode.Clip, LibRawFbddMode.Off, halfSize: false);

        Assert.Throws<ArgumentException>((baseline with
            { CropBox = new(0, 0, 0, 10) }).Validate);
        Assert.Throws<ArgumentException>((baseline with
            { CropBox = new((uint)int.MaxValue + 1, 0, 10, 10) }).Validate);
    }

    private static float[] Multipliers(LibRawOutputConfiguration value) =>
        [value.UserMultiplier0, value.UserMultiplier1,
         value.UserMultiplier2, value.UserMultiplier3];
}
