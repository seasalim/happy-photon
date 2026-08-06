using Xunit;

namespace HappyPhoton.Tests;

public sealed class ReleaseStampingTests
{
    [Fact]
    public void MacPackaging_ForwardsSharedBuildIdentityProperties()
    {
        var script = File.ReadAllText(Path.Combine(
            GoldenTestPaths.RepositoryRoot,
            "scripts",
            "package-macos.sh"));

        Assert.Contains("HAPPY_PHOTON_SOURCE_REVISION", script);
        Assert.Contains("HAPPY_PHOTON_BUILD_TIMESTAMP", script);
        Assert.Contains(
            "-p:SourceRevisionId=\"$HAPPY_PHOTON_SOURCE_REVISION\"",
            script);
        Assert.Contains(
            "-p:SourceRevision=\"$HAPPY_PHOTON_SOURCE_REVISION\"",
            script);
        Assert.Contains(
            "-p:BuildTimestampUtc=\"$HAPPY_PHOTON_BUILD_TIMESTAMP\"",
            script);
    }
}
