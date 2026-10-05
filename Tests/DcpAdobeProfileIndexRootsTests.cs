using System.Runtime.InteropServices;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DcpAdobeProfileIndexRootsTests
{
    [Theory]
    [InlineData("LINUX", 2)]
    [InlineData("WINDOWS", 1)]
    [InlineData("OSX", 2)]
    public void RootDeduplicationUsesPlatformPathCasing(string platform, int expected)
    {
        var roots = DcpAdobeProfileIndex.GetDefaultRoots(
            OSPlatform.Create(platform), _ => null, string.Empty,
            folder => folder == Environment.SpecialFolder.ApplicationData ? "Canon" : "canon");

        Assert.Equal(expected, roots.Count);
    }

    [Theory]
    [InlineData(null, "home")]
    [InlineData("", "home")]
    [InlineData("custom-prefix", "home")]
    [InlineData("custom-prefix", "")]
    public void LinuxAppendsWineRootsWithoutAccessingDisk(string? winePrefix, string home)
    {
        var roots = DcpAdobeProfileIndex.GetDefaultRoots(
            OSPlatform.Linux, key => key == "WINEPREFIX" ? winePrefix : null,
            home, FolderPath);
        var prefix = string.IsNullOrEmpty(winePrefix)
            ? Path.Combine(home, ".wine") : winePrefix;

        Assert.Equal(
        [
            ProfileRoot("roaming"),
            ProfileRoot("common"),
            ProfileRoot(Path.Combine(prefix, "drive_c", "ProgramData")),
            ProfileRoot(Path.Combine(prefix, "drive_c", "users", "*", "AppData", "Roaming"))
        ], roots);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void LinuxWithoutPrefixOrHomeKeepsOnlyApplicationDataRoots(string? winePrefix)
    {
        var roots = DcpAdobeProfileIndex.GetDefaultRoots(
            OSPlatform.Linux, _ => winePrefix, string.Empty, FolderPath);

        Assert.Equal([ProfileRoot("roaming"), ProfileRoot("common")], roots);
    }

    [Fact]
    public void WindowsAndMacRootsRemainUnchanged()
    {
        var windows = DcpAdobeProfileIndex.GetDefaultRoots(
            OSPlatform.Windows, _ => throw new InvalidOperationException(), "home", FolderPath);
        var mac = DcpAdobeProfileIndex.GetDefaultRoots(
            OSPlatform.OSX, _ => throw new InvalidOperationException(), "home", FolderPath);

        Assert.Equal([ProfileRoot("roaming"), ProfileRoot("common")], windows);
        Assert.Equal(
        [
            ProfileRoot("roaming"),
            ProfileRoot("common"),
            ProfileRoot(Path.Combine("/Library", "Application Support")),
            ProfileRoot(Path.Combine("home", "Library", "Application Support"))
        ], mac);
    }

    [Fact]
    public async Task NextScanExpandsNewWineUserDirectories()
    {
        using var directory = new TemporaryDirectory();
        var roots = DcpAdobeProfileIndex.GetDefaultRoots(
            OSPlatform.Linux, _ => directory.Path, directory.Path, _ => string.Empty);
        var discovery = new DcpProfileDiscovery(
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            adobeRoots: roots);
        var image = new ImageFile(Path.Combine(directory.Path, "image.cr2"));
        var camera = new CameraIdentity("Canon", "EOS 6D");
        var before = await discovery.DiscoverAsync(image, camera, CancellationToken.None);
        Assert.Equal(0, before.AdobeProfilesScanned);

        var userRoot = ProfileRoot(Path.Combine(directory.Path, "drive_c", "users",
            "photographer", "AppData", "Roaming"));
        Directory.CreateDirectory(userRoot);
        SyntheticDcpFactory.WriteTemporary(userRoot, new SyntheticDcpOptions
        {
            Name = "Wine profile",
            UniqueCameraModel = "Canon EOS 6D"
        }, "camera.DCP");
        var after = await discovery.DiscoverAsync(image, camera, CancellationToken.None);

        Assert.Equal(1, after.AdobeProfilesScanned);
        Assert.Equal("Wine profile", Assert.Single(after.Options,
            option => option.Selection?.Source == RawProfileSource.Adobe).DisplayName);
    }

    private static string FolderPath(Environment.SpecialFolder folder) =>
        folder == Environment.SpecialFolder.ApplicationData ? "roaming" : "common";

    private static string ProfileRoot(string parent) =>
        Path.Combine(parent, "Adobe", "CameraRaw", "CameraProfiles");
}
