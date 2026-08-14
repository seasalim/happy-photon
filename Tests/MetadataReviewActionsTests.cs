using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class MetadataReviewActionsTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-metadata-actions-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public async Task CopyDetails_IncludesVisibleRowsAndOmitsAbsentRows()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        await using var vm = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);
        var image = new ImageFile(Path.Combine(_root, "photo.jpg"));
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
        using var catalog = new CatalogService(Path.Combine(_root, "map-catalog"));
        await using var vm = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);
        vm.SelectedImage = new ImageFile(Path.Combine(_root, "mapped.jpg"))
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

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
