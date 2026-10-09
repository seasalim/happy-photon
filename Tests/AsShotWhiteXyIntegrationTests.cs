using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;
using static HappyPhoton.Tests.RawBaseLoaderTestSupport;

namespace HappyPhoton.Tests;

public sealed class AsShotWhiteXyIntegrationTests
{
    private const string SamplePath = @"D:\Workspace\.agent-runs\happy-photon-specs\spec-fixes\phone-dngs\Google_Pixel-8-Pro__PXL_20240415_103400204.RAW-02.ORIGINAL.dng";
    private const string ProfileName = "Google Pixel 8 Pro Wide Camera Adobe Standard.dcp";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pixel8Pro_DecodesWithRecordedWhiteAndEmptyNativeFacts(bool selectProfile)
    {
        RequireLocal(SamplePath);
        var reader = new DcpProfileReader();
        var camera = reader.ReadCameraData(SamplePath);
        Assert.Null(camera.AsShotNeutral);
        Assert.NotNull(camera.AsShotWhiteXy);
        var resolution = selectProfile ? ReadPixelProfile() : DcpProfileResolution.BuiltIn;
        var decode = BaseDecodeSettings.Default with { Distortion = false, ChromaticAberration = false, Vignetting = false };
        decode = decode.WithProfileResolution(resolution);
        var neutralProfile = resolution.Profile ?? reader.ReadEmbeddedWhiteBalanceProfile(SamplePath);
        var neutral = DcpMatrixCalculator.DeriveAsShotNeutral(neutralProfile, camera)!;
        var expectedGains = neutral.Select(value => 1 / value).ToArray();

        using (var context = LibRawContext.Open(SamplePath, CancellationToken.None))
        {
            context.Unpack(CancellationToken.None);
            Assert.Equal(RawCameraFactSnapshot.Empty, RawCameraFactSnapshot.Copy(context.GetCameraFacts(CancellationToken.None)));
        }

        using var loaded = new RawBaseLoader().LoadPreviewBase(new ImageFile(SamplePath), decode, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(DcpProfileErrorCode.None, loaded.Info.ProfileStatus);
        Assert.Equal(resolution.Token, loaded.Info.ProfileToken);
        Assert.Equal(selectProfile, loaded.Info.DcpProfile != null);
        Assert.Equal(expectedGains, loaded.Info.CamMul);
        Assert.Null(loaded.Info.CamToSrgb);
        Assert.InRange(loaded.Info.AsShotKelvin, 5600, 6200);
        var expectedWhite = DcpMatrixCalculator.GetAsShotWhiteXy(camera.AsShotWhiteXy!);
        Assert.Equal(expectedWhite.kelvin, loaded.Info.AsShotKelvin);
        Assert.Equal(expectedWhite.tint, loaded.Info.AsShotTint);
        Assert.True(Math.Abs(loaded.Info.AsShotTint) > 1);

        if (!selectProfile)
        {
            var pixels = PixelHash(loaded.Pixels);
            Assert.Equal(NativePreviewHash(decode, expectedGains, loaded.Info), pixels);
            Assert.NotEqual(NativePreviewHash(decode, null, loaded.Info), pixels);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pixel8Pro_RejectedProfileUsesBuiltInGainsAndPixels(bool invalidNeutral)
    {
        RequireLocal(SamplePath);
        var reader = new DcpProfileReader();
        var bytes = SyntheticDcpFactory.Create(new SyntheticDcpOptions
        {
            ColorMatrix1 = invalidNeutral
                ? [-1, 0, 0, 0, 1, 0, 0, 0, 1]
                : [1, 0, 0, 1, 0, 0, 1, 0, 0]
        });
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        var profile = reader.ParseExternal(new DcpExternalSnapshot(bytes, hash), "rejected.dcp");
        var selection = new RawProfileSelection
        {
            Source = RawProfileSource.UserFile,
            Location = "rejected.dcp",
            ContentHash = profile.ContentHash
        };
        var resolution = DcpProfileResolution.Success(selection, profile);
        var decode = BaseDecodeSettings.Default with
        {
            Distortion = false,
            ChromaticAberration = false,
            Vignetting = false
        };
        var camera = reader.ReadCameraData(SamplePath);

        if (invalidNeutral)
        {
            Assert.Throws<DcpProfileException>(() => DcpMatrixCalculator.DeriveAsShotNeutral(profile, camera));
        }
        else
        {
            Assert.NotNull(DcpMatrixCalculator.DeriveAsShotNeutral(profile, camera));
        }

        using var builtIn = new RawBaseLoader().LoadPreviewBase(
            new ImageFile(SamplePath), decode, CancellationToken.None);
        using var rejected = new RawBaseLoader().LoadPreviewBase(
            new ImageFile(SamplePath), decode.WithProfileResolution(resolution), CancellationToken.None);

        Assert.NotNull(builtIn);
        Assert.NotNull(rejected);
        Assert.NotNull(builtIn.Info.CamMul);
        Assert.Equal(DcpProfileErrorCode.UnsupportedVariant, rejected.Info.ProfileStatus);
        Assert.Equal(DcpProfileErrorCode.UnsupportedVariant, rejected.Info.Decode.ProfileResolution!.Status);
        Assert.NotEqual(resolution.Token, rejected.Info.ProfileToken);
        Assert.Null(rejected.Info.DcpProfile);
        Assert.Equal(builtIn.Info.CamMul, rejected.Info.CamMul);
        Assert.Equal(builtIn.Info.AsShotKelvin, rejected.Info.AsShotKelvin);
        Assert.Equal(builtIn.Info.AsShotTint, rejected.Info.AsShotTint);
        Assert.Equal(PixelHash(builtIn.Pixels), PixelHash(rejected.Pixels));
    }

    private static byte[] NativePreviewHash(BaseDecodeSettings decode, double[]? gains, BaseImageInfo info)
    {
        RequireLocal(SamplePath);
        using var context = LibRawContext.Open(SamplePath, CancellationToken.None);
        context.Unpack(CancellationToken.None);
        context.ConfigureOutput(RawBaseLoader.ConfigureOutput(decode, preview: true, asShotGains: gains), CancellationToken.None);
        context.Process(CancellationToken.None);
        using var processed = context.MakeProcessedImage(CancellationToken.None);
        using var image = CameraRgbCharacterization.Passthrough.ImportRgb16(processed.AsSpan(),
            checked((int)processed.Description.Width), checked((int)processed.Description.Height));
        using var pair = PreviewBasePairFactory.Create(image, info, CancellationToken.None);

        return PixelHash(pair.Interactive.Pixels);
    }

    private static DcpProfileResolution ReadPixelProfile()
    {
        var path = DcpAdobeProfileIndex.GetDefaultRoots()
            .Select(root => Path.Combine(root, "Adobe Standard", ProfileName)).FirstOrDefault(File.Exists);
        Assert.SkipWhen(path == null, "The Pixel 8 Pro Adobe profile is not installed.");
        RequireLocal(path!);
        var reader = new DcpProfileReader();
        var snapshot = reader.ReadExternalSnapshot(path!);
        var profile = reader.ParseExternal(snapshot, ProfileName);
        var selection = new RawProfileSelection
        {
            Source = RawProfileSource.UserFile,
            Location = path,
            ContentHash = profile.ContentHash
        };

        return DcpProfileResolution.Success(selection, profile);
    }

    private static void RequireLocal(string path)
    {
        Assert.SkipWhen(!File.Exists(path), "The external sample or profile is not present on this machine.");
        var availability = new SourceAvailabilityService().GetAvailability(path);
        Assert.SkipUnless(availability is SourceAvailability.AvailableLocally or SourceAvailability.Unknown,
            "The external sample or profile is not locally readable.");
    }
}
