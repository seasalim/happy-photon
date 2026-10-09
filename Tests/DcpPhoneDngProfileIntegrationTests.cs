using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

// FIXES-DEVELOP-WP16: phone DNGs whose CameraCalibration has no matching signature keep their Adobe profile.
// The sample files stay out of git; the test skips when they or the installed profiles are absent.
public sealed class DcpPhoneDngProfileIntegrationTests
{
    private const string SampleFolder = @"D:\Workspace\.agent-runs\happy-photon-specs\spec-fixes\phone-dngs";

    // Discovery finds no profile for this file's model string, so the test names it (a discovery gap, not WP16's).
    private const string GalaxyS21UltraProfile = "Samsung Galaxy S21 Ultra Rear Main Camera Adobe Standard.dcp";

    public static TheoryData<string> Samples => new()
    {
        "Google_Pixel-6-Pro__PXL_20220910_093206982.dng",
        "Google_Pixel-7-Pro__PXL_20221204_063131635.dng",
        "Samsung_Galaxy-S21-Ultra__20230712_115041.dng",
        "Samsung_Galaxy-S23__20230414_115131.dng",
        "Samsung_Galaxy-S23-Ultra__20230817_120455.dng",
        "Samsung_Galaxy-S23-Ultra__20231214_130645.dng",
        "Apple_iPhone-12-Pro__IMG_1361.DNG",
        "Apple_iPhone-XS__IMG_1105.dng"
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task AdobeProfileStaysActiveThroughRawBaseLoader(string sample)
    {
        var path = Path.Combine(SampleFolder, sample);
        Assert.SkipWhen(!File.Exists(path), "Sample DNGs are not present on this machine.");
        var selection = await FindAdobeProfile(path);
        Assert.SkipWhen(selection == null, "No installed Adobe profile for this sample.");
        var reader = new DcpProfileReader();
        var snapshot = reader.ReadExternalSnapshot(selection!.Location!);
        var profile = reader.ParseExternal(snapshot, Path.GetFileNameWithoutExtension(selection.Location!));
        var resolution = DcpProfileResolution.Success(selection, profile);
        var decode = BaseDecodeSettings.From(new EditSettings { RawProfile = selection })
            .WithProfileResolution(resolution);

        using var loaded = new RawBaseLoader().LoadPreviewBase(new ImageFile(path), decode, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(DcpProfileErrorCode.None, loaded!.Info.ProfileStatus);
        Assert.Equal(resolution.Token, loaded.Info.ProfileToken);
    }

    private static async Task<RawProfileSelection?> FindAdobeProfile(string path)
    {
        var named = DcpAdobeProfileIndex.GetDefaultRoots()
            .Select(root => Path.Combine(root, "Adobe Standard", GalaxyS21UltraProfile))
            .FirstOrDefault(File.Exists);

        if (path.Contains("Galaxy-S21-Ultra", StringComparison.Ordinal))
        {
            return named == null ? null : new RawProfileSelection
            {
                Source = RawProfileSource.UserFile,
                Location = named,
                ContentHash = new DcpProfileReader().ReadExternalSnapshot(named).ContentHash
            };
        }

        using var context = LibRawContext.Open(path, CancellationToken.None);
        var metadata = context.GetMetadata(CancellationToken.None);
        var identity = new CameraIdentity(metadata.NormalizedMake ?? metadata.Make,
            metadata.NormalizedModel ?? metadata.Model);
        var discovery = new DcpProfileDiscovery(new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        var found = await discovery.DiscoverAsync(new ImageFile(path), identity, CancellationToken.None);

        return found.Options.FirstOrDefault(option => !option.IsBuiltIn && option.Selection?.Location != null &&
            option.DisplayName.Contains("Adobe Standard", StringComparison.Ordinal))?.Selection;
    }
}
