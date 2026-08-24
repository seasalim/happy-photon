using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LensfunVariantIdentityTests
{
    [Fact]
    public void MatcherAllowsDatabaseOnlyTrailingCalibrationInteger()
    {
        var database = new LensfunDatabase(Path.Combine(
            AppContext.BaseDirectory, "data", "lensfun"));

        var match = database.Resolve(
            "Nikon", "D300",
            "AF-S Nikkor 70-200mm f/2.8G ED VR II",
            70, 2.8, 4288, 2848);

        Assert.Equal("Nikon AF-S Nikkor 70-200mm f/2.8G ED VR II 162",
            match?.LensName);
    }
}
