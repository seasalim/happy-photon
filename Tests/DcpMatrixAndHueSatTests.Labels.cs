using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class DcpMatrixAndHueSatTests
{
    [Theory]
    [InlineData(RawProfileSource.Adobe, "Google Pixel 9 Ultrawide Camera", "Adobe Standard · Ultrawide Camera")]
    [InlineData(RawProfileSource.Adobe, "Canon EOS 6D", "Adobe Standard")]
    [InlineData(RawProfileSource.UserFile, "Google Pixel 9 Ultrawide Camera", "Adobe Standard")]
    public void Create_PayloadNameIsThePickerLabelOfTheResolvedProfile(
        RawProfileSource source,
        string declared,
        string label)
    {
        var profile = CreateProfile() with { Name = "Adobe Standard", UniqueCameraModel = declared };
        var resolution = DcpProfileResolution.Success(
            new RawProfileSelection
            {
                Source = source,
                Location = "profile.dcp",
                ContentHash = profile.ContentHash
            },
            profile);

        var result = DcpMatrixCalculator.Create(resolution, DcpCameraData.Defaults, Facts([1, 1, 1]), 5500);

        Assert.True(result.IsActive);
        Assert.Equal(label, result.Payload!.Name);
    }
}
