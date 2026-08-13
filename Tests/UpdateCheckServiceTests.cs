using System.Text.Json;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class UpdateCheckServiceTests
{
    private const string Repository =
        "https://github.com/seasalim/happy-photon";

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2.3-beta.4", 1, 2, 3)]
    [InlineData("1.2.3+revision", 1, 2, 3)]
    [InlineData("v1.2.3-beta.4+revision", 1, 2, 3)]
    public void TryParseVersion_NormalizesSupportedReleaseVersions(
        string value,
        int major,
        int minor,
        int build)
    {
        Assert.True(UpdateCheckService.TryParseVersion(value, out var version));
        Assert.Equal(new Version(major, minor, build), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v1.2")]
    [InlineData("1.2.3-rc.1")]
    [InlineData("1.2.3-beta.x")]
    [InlineData("release-1.2.3")]
    public void TryParseVersion_RejectsMalformedOrUnsupportedTags(string? value)
    {
        Assert.False(UpdateCheckService.TryParseVersion(value, out _));
    }

    [Theory]
    [InlineData("1.2.3-beta.1", "v1.2.3", false)]
    [InlineData("1.2.3+local", "v1.2.3", false)]
    [InlineData("1.2.2", "v1.2.3", true)]
    [InlineData("1.3.0", "v1.2.3", false)]
    public void IsUpdateAvailable_ComparesStableCoreVersions(
        string current,
        string latest,
        bool expected)
    {
        Assert.Equal(
            expected,
            UpdateCheckService.IsUpdateAvailable(current, latest));
    }

    [Fact]
    public async Task CheckAsync_RequestsLatestStableReleaseEndpoint()
    {
        Uri? requestedUri = null;
        var service = CreateService((uri, _) =>
        {
            requestedUri = uri;
            return Task.FromResult(ReleaseJson("v1.0.0"));
        });

        var result = await service.CheckAsync();

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Equal(
            "https://api.github.com/repos/seasalim/happy-photon/releases/latest",
            requestedUri?.AbsoluteUri);
    }

    [Theory]
    [InlineData(true, true, UpdateInstallChannel.MicrosoftStore)]
    [InlineData(true, false, UpdateInstallChannel.GitHubRelease)]
    [InlineData(false, true, UpdateInstallChannel.GitHubRelease)]
    public void ChannelSelection_UsesWindowsPackageIdentityOnly(
        bool isWindows,
        bool isPackaged,
        UpdateInstallChannel expected)
    {
        Assert.Equal(
            expected,
            UpdateChannelSelector.Select(isWindows, () => isPackaged));
    }

    [Fact]
    public void MicrosoftStoreDeepLink_MatchesPublishedSiteConfiguration()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            GoldenTestPaths.RepositoryRoot,
            "site",
            "site-config.json")));

        Assert.Equal(
            UpdateChannelSelector.MicrosoftStoreProductId,
            document.RootElement.GetProperty("microsoftStoreProductId").GetString());
        Assert.Equal(
            UpdateChannelSelector.MicrosoftStoreUri,
            document.RootElement.GetProperty("microsoftStoreDeepLink").GetString());
    }

    private static UpdateCheckService CreateService(
        Func<Uri, CancellationToken, Task<string>> fetchAsync) =>
        new("1.0.0-beta.1+revision", Repository, fetchAsync);

    private static string ReleaseJson(string tag) => JsonSerializer.Serialize(new
    {
        tag_name = tag,
        html_url = $"{Repository}/releases/tag/{tag}"
    });

}
