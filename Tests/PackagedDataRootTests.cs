using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PackagedDataRootTests
{
    [Fact]
    public void MacBundleResolvesDataBesideMacOSDirectory()
    {
        var contents = Path.Combine(Path.GetTempPath(),
            $"happy-photon-bundle-{Guid.NewGuid():N}", "Contents");
        var macOS = Path.Combine(contents, "MacOS");

        var resolved = PackagedDataRoot.Resolve(macOS, isMacOS: true);

        Assert.Equal(Path.Combine(contents, "Resources"), resolved);
    }

    [Fact]
    public void MacBundleWithTrailingSeparatorResolvesDataBesideMacOSDirectory()
    {
        var contents = Path.Combine(Path.GetTempPath(),
            $"happy-photon-bundle-{Guid.NewGuid():N}", "Contents");
        var macOS = Path.Combine(contents, "MacOS") + Path.DirectorySeparatorChar;

        var resolved = PackagedDataRoot.Resolve(macOS, isMacOS: true);

        Assert.Equal(Path.Combine(contents, "Resources"), resolved);
    }

    [Fact]
    public void NonMacBundleLayoutRemainsNextToBinary()
    {
        var macOS = Path.Combine(Path.GetTempPath(),
            "Happy Photon.app", "Contents", "MacOS");

        Assert.Equal(macOS,
            PackagedDataRoot.Resolve(macOS, isMacOS: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PlainLayoutRemainsNextToBinary(bool isMacOS)
    {
        var binaryDirectory = Path.Combine(Path.GetTempPath(), "HappyPhoton");

        Assert.Equal(binaryDirectory,
            PackagedDataRoot.Resolve(binaryDirectory, isMacOS));
    }

    [Fact]
    public void UnrelatedMacOSDirectoryRemainsNextToBinary()
    {
        var macOS = Path.Combine(Path.GetTempPath(), "Other", "MacOS");

        Assert.Equal(macOS, PackagedDataRoot.Resolve(macOS, isMacOS: true));
    }

    [Theory]
    [InlineData("Services/LensfunPrescriptionReader.cs", "lensfun")]
    [InlineData("Services/LensIdentityResolver.cs", "lens-ids")]
    public void BundledDataConsumersResolveThroughPackagedDataRoot(
        string relativePath, string dataDirectory)
    {
        var source = File.ReadAllText(
            Path.Combine(GoldenTestPaths.RepositoryRoot, relativePath));

        Assert.Contains(
            $"PackagedDataRoot.Resolve(), \"data\", \"{dataDirectory}\"",
            source);
        Assert.DoesNotContain("AppContext.BaseDirectory", source);
    }

    [Fact]
    public void CurrentPlainLayoutUsesApplicationBaseDirectory()
    {
        Assert.Equal(AppContext.BaseDirectory,
            PackagedDataRoot.Resolve());
    }
}
