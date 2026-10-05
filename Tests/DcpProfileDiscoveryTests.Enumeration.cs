using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class DcpProfileDiscoveryTests
{
    [Fact]
    public async Task Discover_AndProbeVisitCaseDistinctSiblingsOnLinux()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Requires Linux path comparison.");

        using var directory = new TemporaryDirectory();
        var upper = Directory.CreateDirectory(Path.Combine(directory.Path, "Canon"));
        var lower = Directory.CreateDirectory(Path.Combine(directory.Path, "canon"));
        var profile = SyntheticDcpFactory.WriteTemporary(directory.Path,
            new SyntheticDcpOptions { UniqueCameraModel = "Canon EOS 6D" });
        IEnumerable<FileSystemInfo> Enumerate(string path) => path == directory.Path
            ? [upper, lower]
            : path == upper.FullName ? [new FileInfo(profile)] : [];
        var discovery = new DcpProfileDiscovery(
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally), adobeRoots: [directory.Path])
        {
            EnumerateAdobeDirectory = Enumerate
        };
        var result = await discovery.DiscoverAsync(new ImageFile(Path.Combine(directory.Path, "image.cr2")),
            new CameraIdentity("Canon", "EOS 6D"), CancellationToken.None);

        Assert.Equal(1, result.AdobeCandidates);
        Assert.Equal(1, result.AdobeProfilesScanned);
        Assert.True(result.AdobeEnumerationComplete);
        Assert.Equal(DcpAdobeProfilePresence.Found,
            await DcpAdobeProfileIndex.ProbeAsync([directory.Path], enumerateDirectory: Enumerate));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discover_AndProbeSkipLinkedChildrenButExpandLinkedWildcardUsers(bool wildcard)
    {
        using var directory = new TemporaryDirectory();
        var target = Directory.CreateDirectory(Path.Combine(directory.Path, "target"));
        var linkPath = Path.Combine(directory.Path, "linked");

        try
        {
            Directory.CreateSymbolicLink(linkPath, target.FullName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            PlatformNotSupportedException)
        {
            Assert.Skip("This environment cannot create a directory symbolic link.");
        }

        var link = new DirectoryInfo(linkPath);
        Assert.True((link.Attributes & FileAttributes.ReparsePoint) != 0);
        var profile = SyntheticDcpFactory.WriteTemporary(target.FullName,
            new SyntheticDcpOptions { UniqueCameraModel = "Canon EOS 6D" });
        var calls = new List<string>();
        IEnumerable<FileSystemInfo> Enumerate(string path)
        {
            calls.Add(path);

            return path == directory.Path ? [link] : [new FileInfo(profile)];
        }

        var root = wildcard ? Path.Combine(directory.Path, "*", "profiles") : directory.Path;
        var discovery = new DcpProfileDiscovery(
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally), adobeRoots: [root])
        {
            EnumerateAdobeDirectory = Enumerate
        };
        var result = await discovery.DiscoverAsync(new ImageFile(Path.Combine(directory.Path, "image.cr2")),
            new CameraIdentity("Canon", "EOS 6D"), CancellationToken.None);

        Assert.Equal(wildcard ? 1 : 0, result.AdobeCandidates);
        Assert.True(result.AdobeEnumerationComplete);
        Assert.Equal(wildcard ? DcpAdobeProfilePresence.Found : DcpAdobeProfilePresence.None,
            await DcpAdobeProfileIndex.ProbeAsync([root], enumerateDirectory: Enumerate));
        Assert.DoesNotContain(linkPath, calls);
        Assert.Equal(wildcard ? 4 : 2, calls.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discover_CountsMalformedAndUnavailableCandidates(bool placeholder)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "candidate.dcp");
        File.WriteAllText(path, "malformed profile");
        var availability = new TestSourceAvailabilityService(placeholder
            ? SourceAvailability.RequiresHydration : SourceAvailability.AvailableLocally);
        // Exclusive access also proves that the placeholder path is not opened.
        using var held = placeholder ? File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null;
        var discovery = new DcpProfileDiscovery(availability, adobeRoots: [directory.Path]);
        var result = await discovery.DiscoverAsync(new ImageFile(Path.Combine(directory.Path, "image.cr2")),
            new CameraIdentity("Canon", "EOS 6D"), CancellationToken.None);

        Assert.Equal(1, result.AdobeCandidates);
        Assert.Equal(0, result.AdobeProfilesScanned);
        Assert.True(result.AdobeEnumerationComplete);
        Assert.True(result.AdobeScanAttempted);
        Assert.True(Assert.Single(result.Options).IsBuiltIn);
        Assert.Equal(1, availability.CallCount);
    }

    [Fact]
    public async Task Discover_MissingRootIsComplete()
    {
        using var directory = new TemporaryDirectory();
        var result = await new DcpProfileDiscovery(
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            adobeRoots: [Path.Combine(directory.Path, "missing")]).DiscoverAsync(
                new ImageFile(Path.Combine(directory.Path, "image.cr2")),
                new CameraIdentity("Canon", "EOS 6D"), CancellationToken.None);

        Assert.Equal(0, result.AdobeCandidates);
        Assert.Equal(0, result.AdobeProfilesScanned);
        Assert.True(result.AdobeEnumerationComplete);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("wildcard")]
    [InlineData("nested")]
    public async Task Discover_DeniedEnumerationIsIncompleteAndOtherRootsContinue(string denied)
    {
        using var directory = new TemporaryDirectory();
        var good = Directory.CreateDirectory(Path.Combine(directory.Path, "good")).FullName;
        SyntheticDcpFactory.WriteTemporary(good, new SyntheticDcpOptions { UniqueCameraModel = "Canon EOS 6D" });
        var bad = Directory.CreateDirectory(Path.Combine(directory.Path, "bad")).FullName;
        var blocked = denied == "nested" ? Directory.CreateDirectory(Path.Combine(bad, "child")).FullName : bad;
        var root = denied == "wildcard" ? Path.Combine(bad, "*", "profiles") : bad;
        var discovery = new DcpProfileDiscovery(
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally), adobeRoots: [root, good])
        {
            EnumerateAdobeDirectory = path => path == blocked
                ? throw new UnauthorizedAccessException()
                : new DirectoryInfo(path).EnumerateFileSystemInfos()
        };
        var result = await discovery.DiscoverAsync(new ImageFile(Path.Combine(directory.Path, "image.cr2")),
            new CameraIdentity("Canon", "EOS 6D"), CancellationToken.None);

        Assert.False(result.AdobeEnumerationComplete);
        Assert.Equal(1, result.AdobeCandidates);
        Assert.Equal(1, result.AdobeProfilesScanned);
        Assert.Equal(1, result.AdobeIdentityMatchCount);
    }
}
