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

    [Fact]
    public void MacPackaging_AppliesRequiredJitEntitlement()
    {
        var script = File.ReadAllText(Path.Combine(
            GoldenTestPaths.RepositoryRoot,
            "scripts",
            "package-macos.sh"));
        var entitlements = File.ReadAllText(Path.Combine(
            GoldenTestPaths.RepositoryRoot,
            "Platforms",
            "macOS",
            "HappyPhoton.entitlements"));

        Assert.Contains("--entitlements \"$entitlements_file\"", script);
        Assert.Contains("sign_app_bundle", script);
        Assert.Contains("com.apple.security.cs.allow-jit", entitlements);

        var workflow = File.ReadAllText(Path.Combine(
            GoldenTestPaths.RepositoryRoot,
            ".github",
            "workflows",
            "release.yml"));
        Assert.Contains("Smoke test signed app launch", workflow);
        Assert.Contains("happy-photon-launch.log", workflow);
    }
}
