using System.Security.Cryptography;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

internal static class StraightenGateOwnerFixtures
{
    internal const string DirectoryVariable = "HAPPY_PHOTON_STRAIGHTEN_DIR";

    internal static readonly IReadOnlyDictionary<string, string> Manifest = new Dictionary<string, string>
    {
        ["DSCF0076.JPG"] = "97ba733a8789a1420ea2e63bd55586c092b12430e8a21b76c94800d2585ec0f3",
        ["DSCF0129.RAF"] = "0abc17dccc01573e21c4b5fc3cb645a5aef67fbe1f5c467535b42b21de54304b",
        ["DSCF0155.JPG"] = "c22f96436a7e30b6ac651422afc237a576a27cd71ce46952ae21e4f8d8b419a9",
        ["DSCF0423.JPG"] = "53ee77470f4aa1a39ae74b507fa25e8585f612e2da2615ff8206fa9f011dfff3",
        ["DSCF2263.RAF"] = "c837b742bdd720ccbcae2def899b83e09e58dc8e0f9f2f8667c74557dfffa72e",
        ["DSCF0075.JPG"] = "b7a5c553e9aab9c61a359ee97a304d497bc12675c7f57d10ca28f675d707e955",
        ["DSCF0076.RAF"] = "aee65b71a13d3841be377d98b44f4aa4a72943eab57c033586eaefed0d051b97",
        ["DSCF8369.JPG"] = "44b84ab25a8d8d9c27964039499cec3cb716149b9590d52c4e224c60e68a651d",
        ["IMG_4952.JPG"] = "762611c38106f4725f507070a380d802ec0d6b83c5185184775fa3b789c772d1",
        ["IMG_5177.JPG"] = "2e513675ccb3e13e9d438eaa3f36436d636c9a24efb04aa4b94ccdd4592e9232",
        ["IMG_5569.JPG"] = "37874d51732ae60ba53b30122338d88d6484ef9df8e36086b2850d00af2ef57e",
        ["IMG_6854.HEIC"] = "c59bd4de0e5470e5b358df9cd7712f54fffd60f786acae93462eb7f7b5e6a158"
    };

    internal static readonly string[] Labels =
        ["DSCF0076.JPG", "DSCF0075.JPG", "DSCF0076.RAF", "DSCF0129.RAF", "IMG_5569.JPG"];

    internal static string PathFor(string name)
    {
        Assert.True(Manifest.ContainsKey(name), $"Unknown straighten owner fixture: {name}");
        var folder = Environment.GetEnvironmentVariable(DirectoryVariable);
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(folder),
            $"Owner photo set unavailable: {DirectoryVariable} is unset");

        return Path.Combine(folder!, name);
    }

    internal static object Verify(string name)
    {
        var path = PathFor(name);
        Assert.True(File.Exists(path), $"Missing straighten owner fixture: {name}");
        // Check live availability before opening source content, including hashes.
        Assert.Equal(SourceAvailability.AvailableLocally, new SourceAvailabilityService().GetAvailability(path));
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        Assert.Equal(Manifest[name], actual);

        return new { name, present = true, sha256 = actual, match = true };
    }

    internal static PreviewBasePair Load(string name)
    {
        Verify(name);
        var file = FinishingGateSupport.LocalFile(PathFor(name));
        var pair = FinishingGateSupport.Loader().LoadPreviewBaseWithOutcome(file,
            BaseDecodeSettings.Default, CancellationToken.None).Pair;
        Assert.NotNull(pair);

        return pair;
    }

    internal static string ImagePath(string name, string suffix)
    {
        Assert.True(Manifest.ContainsKey(name), $"Unknown straighten owner fixture: {name}");
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "happy-photon-straighten-owner"));

        return Path.Combine(folder.FullName, name + "-" + suffix + ".png");
    }
}
