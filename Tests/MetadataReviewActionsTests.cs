using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class MetadataReviewActionsTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("metadata-actions");

    [Fact]
    public async Task CopyDetails_IncludesVisibleRowsAndOmitsAbsentRows()
    {
        using var catalog = _fx.CreateCatalog("catalog");
        await using var vm = _fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask);
        var image = new ImageFile(_fx.Path("photo.jpg"));
        image.ApplyMetadata(new ImageMetadata
        {
            FileSize = 2_048,
            PixelWidth = 6000,
            PixelHeight = 4000,
            Iso = 100,
            ExposureBias = 0.7,
            GpsAltitude = -12
        });
        vm.SelectedImage = image;
        string? copied = null;
        vm.CopyToClipboardAsync = text =>
        {
            copied = text;
            return Task.CompletedTask;
        };

        await vm.CopyMetadataDetailsCommand.ExecuteAsync(null);

        Assert.Equal(
            "FILE\n" +
            "photo.jpg\n" +
            "6000×4000 · 24.0 MP · 2.0 KB\n" +
            "CAMERA\n" +
            "ISO 100  +0.7 EV\n" +
            "LOCATION\n" +
            "-12 m altitude",
            copied!.Replace("\r\n", "\n"));
        Assert.DoesNotContain("Lens", copied);
        Assert.DoesNotContain("0° N", copied);
    }

    [Fact]
    public async Task OpenMap_LaunchesOnlyAfterExplicitCommandActivation()
    {
        using var catalog = _fx.CreateCatalog("map-catalog");
        await using var vm = _fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask);
        vm.SelectedImage = new ImageFile(_fx.Path("mapped.jpg"))
        {
            GpsLatitude = 47.608333,
            GpsLongitude = -122.320833
        };
        var launches = new List<Uri>();
        vm.LaunchUriAsync = uri =>
        {
            launches.Add(uri);
            return Task.FromResult(true);
        };

        Assert.Empty(launches);
        await vm.OpenSelectedImageMapCommand.ExecuteAsync(null);

        var launched = Assert.Single(launches);
        Assert.Equal(
            "https://www.openstreetmap.org/?mlat=47.608333&" +
            "mlon=-122.320833#map=15/47.608333/-122.320833",
            launched.AbsoluteUri);
    }

    public void Dispose() => _fx.Dispose();
}
