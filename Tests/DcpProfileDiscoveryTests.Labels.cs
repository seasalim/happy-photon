using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class DcpProfileDiscoveryTests
{
    [Fact]
    public async Task Discover_PhoneModuleProfilesGetDistinctLabelsInModuleOrder()
    {
        using var directory = new TemporaryDirectory();
        var modules = new[] { "Wide", "Ultrawide", "Front" };

        foreach (var module in modules)
        {
            SyntheticDcpFactory.WriteTemporary(directory.Path, new SyntheticDcpOptions
            {
                Name = "Adobe Standard",
                UniqueCameraModel = $"Google Pixel 9 {module} Camera",
                ColorMatrix1 = [1 + Array.IndexOf(modules, module) * 0.01, 0, 0, 0, 1, 0, 0, 0, 1]
            }, $"Google Pixel 9 {module} Camera Adobe Standard.dcp");
        }

        var discovery = new DcpProfileDiscovery(
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally), adobeRoots: [directory.Path]);

        var result = await discovery.DiscoverAsync(new ImageFile(Path.Combine(directory.Path, "image.raw")),
            new CameraIdentity("Google", "Pixel 9"), CancellationToken.None, hints: new(null, 24));

        Assert.Equal(
            ["Adobe Standard · Wide Camera", "Adobe Standard · Front Camera", "Adobe Standard · Ultrawide Camera"],
            result.Options.Where(option => !option.IsBuiltIn).Select(option => option.DisplayName));
    }

    [Theory]
    [InlineData("OnePlus", "12", "OnePlus 12 back camera 6.06mm f/1.6", "Adobe Standard · back camera 6.06mm f/1.6")]
    [InlineData("Samsung", "Galaxy S23+", "Samsung Galaxy S23+ Rear Wide Camera", "Adobe Standard · Rear Wide Camera")]
    [InlineData("Google", "Pixel 5", "Google Pixel 5 Rear Ultra Wide Camera", "Adobe Standard · Rear Ultra Wide Camera")]
    [InlineData("Canon", "EOS 6D", "Canon EOS 6D", "Adobe Standard")]
    [InlineData("FUJIFILM", "FinePix X100", "FUJIFILM X100", "Adobe Standard")]
    public async Task Discover_AdobeLabelNamesTheModuleOnlyForModuleProfiles(
        string make,
        string model,
        string declared,
        string label)
    {
        using var directory = new TemporaryDirectory();
        SyntheticDcpFactory.WriteTemporary(directory.Path, new SyntheticDcpOptions
        {
            Name = "Adobe Standard",
            UniqueCameraModel = declared
        });
        var discovery = new DcpProfileDiscovery(
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally), adobeRoots: [directory.Path]);

        var result = await discovery.DiscoverAsync(new ImageFile(Path.Combine(directory.Path, "image.raw")),
            new CameraIdentity(make, model), CancellationToken.None);

        Assert.Equal(label, result.Options.Single(option => !option.IsBuiltIn).DisplayName);
    }

    [Fact]
    public async Task Discover_IphoneModulesFromTheDngTagAreLabelledInTheProfilesCasing()
    {
        using var directory = new TemporaryDirectory();
        var dng = SyntheticDcpFactory.WriteTemporary(directory.Path, new SyntheticDcpOptions
        {
            IncludeColorMatrix1 = false,
            UniqueCameraModel = "iPhone13,3 back camera"
        }, "image.dng");
        var declarations = new[] { "back camera", "back telephoto camera" };

        foreach (var suffix in declarations)
        {
            SyntheticDcpFactory.WriteTemporary(directory.Path, new SyntheticDcpOptions
            {
                Name = "Adobe Standard",
                UniqueCameraModel = "iPhone13,3 " + suffix,
                ColorMatrix1 = [1 + Array.IndexOf(declarations, suffix) * 0.01, 0, 0, 0, 1, 0, 0, 0, 1]
            }, suffix + ".dcp");
        }

        var discovery = new DcpProfileDiscovery(
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally), adobeRoots: [directory.Path]);

        var result = await discovery.DiscoverAsync(new ImageFile(dng), new CameraIdentity("Apple", "iPhone 12 Pro"),
            CancellationToken.None, includeImageProfiles: false);

        Assert.Equal(
            ["Adobe Standard · back camera", "Adobe Standard · back telephoto camera"],
            result.Options.Where(option => !option.IsBuiltIn).Select(option => option.DisplayName).Order());
    }

    [Fact]
    public void InspectUserFile_ChosenModuleProfileKeepsItsNameAndTooltip()
    {
        using var directory = new TemporaryDirectory();
        var path = SyntheticDcpFactory.WriteTemporary(directory.Path, new SyntheticDcpOptions
        {
            Name = "Adobe Standard",
            UniqueCameraModel = "Google Pixel 9 Wide Camera"
        });
        var discovery = new DcpProfileDiscovery(
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));

        var option = new RawProfileOptionViewModel(discovery.InspectUserFile(path));

        Assert.Equal("Adobe Standard", option.Label);
        Assert.Equal("Adobe Standard · Google Pixel 9 Wide Camera · Chosen file", option.ClosedRowTooltip);
    }
}
