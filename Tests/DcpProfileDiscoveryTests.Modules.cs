using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class DcpProfileDiscoveryTests
{
    [Theory]
    [InlineData("Google", "Pixel 9", "Google Pixel 9 Wide Camera", true)]
    [InlineData("Google", "Pixel 9", "Google Pixel 9 Front Camera", true)]
    [InlineData("Google", "Pixel 9", "Google Pixel 9 Pro Wide Camera", false)]
    [InlineData("Google", "Pixel 9", "Google Pixel 9a Wide Camera", false)]
    [InlineData("OnePlus", "12", "OnePlus 12 back camera 6.06mm f/1.6", true)]
    [InlineData("OnePlus", "12", "OnePlus 12 front camera f/2.4", true)]
    [InlineData("OnePlus", "12", "OnePlus 12 back camera 6mm", true)]
    [InlineData("OnePlus", "12", "OnePlus 12 back camera 6mm f/1.6 extra", false)]
    [InlineData("Samsung", "Galaxy S21", "Samsung Galaxy S21 Ultra Rear Main Camera", false)]
    [InlineData("Google", "Pixel 5", "Google Pixel 5 Rear Ultra Wide Camera", true)]
    [InlineData("Sony", "Xperia 1", "Sony Xperia 1 II Wide-angle Camera", false)]
    [InlineData("Sony", "Xperia 1", "Sony Xperia 1 Wide-angle Camera", true)]
    [InlineData("Leica", "M8", "Leica M8 Digital Camera", false)]
    [InlineData("Samsung", "Galaxy S23", "Samsung Galaxy S23+ Rear Wide Camera", false)]
    [InlineData("Samsung", "Galaxy S23+", "Samsung Galaxy S23 Rear Wide Camera", false)]
    [InlineData("Samsung", "Galaxy S23+", "Samsung Galaxy S23+ Rear Wide Camera", true)]
    [InlineData("Phase One", "P20+", "Phase One P20+", true)]
    [InlineData("Phase One", "P65+", "Phase One P65", true)]
    [InlineData("FUJIFILM", "FinePix X100", "FUJIFILM X100", true)]
    [InlineData("Google", "Pixel Fold", "Google Pixel Fold Inner Camera", true)]
    [InlineData("Samsung", "Galaxy Z Fold7", "Samsung Galaxy Z Fold7 Under Display Camera", true)]
    [InlineData("Google", "Pixel 9", "Google Pixel 9 WideCamera", false)]
    [InlineData("Google", "Pixel 9", "Google Pixel 9 Camera", false)]
    [InlineData("Google", "Pixel 9", "Google Pixel 9 Front Camera Adobe Standard", false)]
    [InlineData("OPPO", "Find X5", "OPPO Find X5 Wide camera 23mm f/1.8", false)]
    public async Task Discover_ModuleGrammarKeepsModelBoundaries(string make, string model, string declared, bool matches)
    {
        using var directory = new TemporaryDirectory();
        SyntheticDcpFactory.WriteTemporary(directory.Path, new SyntheticDcpOptions
        {
            Name = declared,
            UniqueCameraModel = declared
        });
        var discovery = new DcpProfileDiscovery(
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally), adobeRoots: [directory.Path]);

        var result = await discovery.DiscoverAsync(new ImageFile(Path.Combine(directory.Path, "image.raw")),
            new CameraIdentity(make, model), CancellationToken.None);

        Assert.Equal(matches ? 1 : 0, result.AdobeIdentityMatchCount);
        Assert.Equal(matches, result.HasProfiles);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Discover_DngIdentityIsIndependentOfCalibrationAndImageProfileScan(bool includeProfiles, bool invalidCalibration)
    {
        using var directory = new TemporaryDirectory();
        var dng = SyntheticDcpFactory.WriteTemporary(directory.Path, new SyntheticDcpOptions
        {
            IncludeColorMatrix1 = false,
            UniqueCameraModel = "iPhone13,3 back camera",
            CameraCalibration1 = invalidCalibration ? [1, 2] : null
        }, "image.dng");

        if (invalidCalibration)
        {
            Assert.Throws<DcpProfileException>(() => new DcpProfileReader().ReadCameraData(dng));
        }

        var declarations = new[] { "back camera", "back telephoto camera", "back ultra wide camera", "front camera" };

        foreach (var suffix in declarations)
        {
            SyntheticDcpFactory.WriteTemporary(directory.Path, new SyntheticDcpOptions
            {
                Name = suffix,
                UniqueCameraModel = "iPhone13,3 " + suffix,
                ColorMatrix1 = [1 + Array.IndexOf(declarations, suffix) * 0.01, 0, 0, 0, 1, 0, 0, 0, 1]
            }, suffix + ".dcp");
        }

        var discovery = new DcpProfileDiscovery(
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally), adobeRoots: [directory.Path]);
        var result = await discovery.DiscoverAsync(new ImageFile(dng), new CameraIdentity("Apple", "iPhone 12 Pro"),
            CancellationToken.None, includeProfiles);

        Assert.Equal(4, result.AdobeIdentityMatchCount);
        Assert.Equal(4, result.Options.Count(option => option.Selection?.Source == RawProfileSource.Adobe));
    }

    [Fact]
    public async Task Discover_DngIdentityExactMatchNeedsNeitherModuleNorMakeModel()
    {
        using var directory = new TemporaryDirectory();
        var dng = SyntheticDcpFactory.WriteTemporary(directory.Path,
            new SyntheticDcpOptions { UniqueCameraModel = "Hardware X", IncludeColorMatrix1 = false }, "image.dng");
        SyntheticDcpFactory.WriteTemporary(directory.Path, new SyntheticDcpOptions { UniqueCameraModel = "Hardware X" });
        var availability = new TestSourceAvailabilityService(SourceAvailability.AvailableLocally);
        var discovery = new DcpProfileDiscovery(availability, adobeRoots: [directory.Path]);
        var result = await discovery.DiscoverAsync(new ImageFile(dng), null, CancellationToken.None, false);
        Assert.Equal(1, result.AdobeIdentityMatchCount);
        availability.Resolver = path => path == dng ? SourceAvailability.RequiresHydration : SourceAvailability.AvailableLocally;

        result = await discovery.DiscoverAsync(new ImageFile(dng), null, CancellationToken.None, false);

        Assert.False(result.AdobeScanAttempted);
        Assert.False(result.HasProfiles);
    }

    [Theory]
    [InlineData("front camera", 230, "Front")]
    [InlineData(null, 18, "Rear Ultrawide")]
    [InlineData(null, 23, "Rear Wide")]
    [InlineData(null, 40, "Rear Wide")]
    [InlineData(null, 117, "Rear Telephoto")]
    [InlineData(null, 150, "Rear Telephoto")]
    [InlineData(null, 230, "Rear Super Telephoto")]
    [InlineData(null, 0, "Front")]
    public async Task Discover_PrefersModuleWithoutSelectingIt(string? lens, double focal, string preferred)
    {
        using var directory = new TemporaryDirectory();
        var modules = new[] { "Front", "Rear Super Telephoto", "Rear Telephoto", "Rear Ultrawide", "Rear Wide" };

        foreach (var module in modules)
        {
            SyntheticDcpFactory.WriteTemporary(directory.Path, new SyntheticDcpOptions
            {
                Name = module,
                UniqueCameraModel = "Samsung Galaxy S23 Ultra " + module + " Camera",
                ColorMatrix1 = [1 + Array.IndexOf(modules, module) * 0.01, 0, 0, 0, 1, 0, 0, 0, 1]
            }, module + ".dcp");
        }

        var image = new ImageFile(Path.Combine(directory.Path, "image.raw"));
        var discovery = new DcpProfileDiscovery(
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally), adobeRoots: [directory.Path]);
        var result = await discovery.DiscoverAsync(image, new CameraIdentity("Samsung", "Galaxy S23 Ultra"),
            CancellationToken.None, hints: new(lens, focal));

        Assert.Equal(preferred, result.Options[0].DisplayName);
        Assert.Null(image.EditSettings.RawProfile);
        Assert.Equal(modules.Length, result.AdobeIdentityMatchCount);
    }
}
