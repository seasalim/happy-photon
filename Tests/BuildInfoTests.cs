using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public class BuildInfoTests
{
    private const string Revision =
        "0123456789abcdef0123456789abcdef01234567";
    private const string Repository =
        "https://github.com/seasalim/happy-photon";

    [Theory]
    [InlineData("0.2.0-beta.1", "0.2.0-beta.1")]
    [InlineData("0.2.0-beta.1+abcdef", "0.2.0-beta.1")]
    [InlineData("0.1.0+local", "0.1.0")]
    public void FriendlyVersion_PreservesPrereleaseAndOmitsBuildMetadata(
        string informationalVersion,
        string expected)
    {
        Assert.Equal(
            expected,
            AppBuildIdentityFactory.FormatFriendlyVersion(
                informationalVersion,
                "9.9.9"));
    }

    [Fact]
    public void ExplicitRevision_IsAuthoritativeOverInformationalVersionSuffix()
    {
        var identity = CreateStamped(
            informationalVersion: "0.2.0+ffffffffffffffffffffffffffffffffffffffff");

        Assert.Equal(Revision, identity.SourceRevision);
        Assert.Equal("01234567", identity.ShortSourceRevision);
        Assert.Contains($"Source revision: {Revision}", identity.SupportText);
        Assert.DoesNotContain("ffffffff", identity.SupportText);
    }

    [Fact]
    public void OffsetCommitTimestamp_NormalizesToUtc()
    {
        var identity = CreateStamped(timestamp: "2026-08-04T11:42:00-07:00");

        Assert.Equal(
            new DateTimeOffset(2026, 8, 4, 18, 42, 0, TimeSpan.Zero),
            identity.CommitTimestampUtc);
        Assert.Equal("Commit date (UTC) · 2026-08-04 18:42", identity.DateDisplayText);
    }

    [Theory]
    [InlineData(null, null, BuildIdentityProvenance.UnstampedLocalFallback)]
    [InlineData(Revision, null, BuildIdentityProvenance.IncompleteOrInvalidStamp)]
    [InlineData(null, "not-a-date", BuildIdentityProvenance.IncompleteOrInvalidStamp)]
    [InlineData("not-a-revision", "2026-08-04T18:42:00Z", BuildIdentityProvenance.IncompleteOrInvalidStamp)]
    public void MissingOrInvalidStamp_SelectsDocumentedFallback(
        string? revision,
        string? timestamp,
        BuildIdentityProvenance expected)
    {
        var identity = Create(revision, timestamp);

        Assert.Equal(expected, identity.Provenance);
        Assert.Contains("file date 2026-08-04 18:42 UTC", identity.DateDisplayText);
    }

    [Fact]
    public void StampedAndLocalBuilds_UseDistinctDateLabels()
    {
        Assert.Equal("Commit date (UTC)", CreateStamped().DateLabel);
        Assert.Equal("Local build", Create(null, null).DateLabel);
    }

    [Theory]
    [InlineData("0123456789abcdef", "01234567")]
    [InlineData("abcdef0", "abcdef0")]
    [InlineData("short", null)]
    [InlineData("", null)]
    public void ShortRevision_IsStableAndSafe(string revision, string? expected)
    {
        Assert.Equal(expected, AppBuildIdentityFactory.ShortenRevision(revision));
    }

    [Fact]
    public void ClipboardSupportText_ContainsStampedRuntimeIdentity()
    {
        var identity = CreateStamped();

        Assert.Contains("Version: 0.2.0-beta.1", identity.SupportText);
        Assert.Contains($"Source revision: {Revision}", identity.SupportText);
        Assert.Contains("Commit timestamp: 2026-08-04T18:42:00.0000000+00:00", identity.SupportText);
        Assert.Contains("Operating system: Test OS", identity.SupportText);
        Assert.Contains("Process architecture: Arm64", identity.SupportText);
    }

    [Fact]
    public void ClipboardSupportText_LabelsUnstampedLocalBuild()
    {
        var identity = Create(null, null);

        Assert.Contains("Build identity: unstamped local build", identity.SupportText);
        Assert.Contains(
            "Local executable file timestamp: 2026-08-04T18:42:00.0000000+00:00",
            identity.SupportText);
        Assert.DoesNotContain("Source revision:", identity.SupportText);
    }

    [Fact]
    public void StampedUrls_ArePinnedToExplicitRevision()
    {
        var identity = CreateStamped();

        Assert.Equal(Repository, identity.RepositoryRoot);
        Assert.Equal($"{Repository}/tree/{Revision}", identity.ProjectUrl);
        Assert.Equal($"{Repository}/blob/{Revision}/LICENSE", identity.LicenseUrl);
        Assert.Equal(
            $"{Repository}/blob/{Revision}/THIRD_PARTY_NOTICES.md",
            identity.ThirdPartyNoticesUrl);
    }

    [Fact]
    public void UnstampedUrls_UseDefaultBranchDocuments()
    {
        var identity = Create(null, null);

        Assert.Equal(Repository, identity.ProjectUrl);
        Assert.Equal($"{Repository}/blob/main/LICENSE", identity.LicenseUrl);
        Assert.Equal(
            $"{Repository}/blob/main/THIRD_PARTY_NOTICES.md",
            identity.ThirdPartyNoticesUrl);
    }

    private static AppBuildIdentity CreateStamped(
        string informationalVersion = "0.2.0-beta.1+sdk-derived-revision",
        string timestamp = "2026-08-04T18:42:00Z") =>
        Create(Revision, timestamp, informationalVersion);

    private static AppBuildIdentity Create(
        string? revision,
        string? timestamp,
        string informationalVersion = "0.2.0-beta.1") =>
        AppBuildIdentityFactory.Create(new AppBuildInfoInputs(
            informationalVersion,
            "0.0.0",
            revision,
            timestamp,
            Repository,
            "Copyright © 2026 Happy Photon contributors",
            new DateTimeOffset(2026, 8, 4, 18, 42, 0, TimeSpan.Zero),
            "Test OS",
            "Arm64"));
}
