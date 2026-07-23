using System.Text.RegularExpressions;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public class BuildInfoTests
{
    [Fact]
    public void Version_MatchesAssemblyVersion()
    {
        var expected = typeof(AppBuildInfo).Assembly.GetName().Version;

        Assert.NotNull(expected);
        Assert.Equal(expected, AppBuildInfo.Version);
    }

    [Fact]
    public void BuildTime_IsPlausible()
    {
        Assert.NotEqual(default, AppBuildInfo.BuildTime);
        Assert.True(AppBuildInfo.BuildTime > new DateTime(2020, 1, 1),
            $"Build time {AppBuildInfo.BuildTime} is implausibly old");
        Assert.True(AppBuildInfo.BuildTime <= DateTime.Now.AddMinutes(5),
            $"Build time {AppBuildInfo.BuildTime} is in the future");
    }

    [Fact]
    public void StatusText_HasVersionAndBuildTimestamp()
    {
        Assert.Matches(
            new Regex(@"^v\d+\.\d+\.\d+ · built \d{4}-\d{2}-\d{2} \d{2}:\d{2}$"),
            AppBuildInfo.StatusText);
    }
}
