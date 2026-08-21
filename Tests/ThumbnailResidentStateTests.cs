using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class ThumbnailResidentStateTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-thumbnail-resident-{Guid.NewGuid():N}");

    [WindowsFact]
    public async Task ResidentBitmap_RecordsUpgradeDeferralWithoutCloudState()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "upgrade-vm"));
        await catalog.InitializeAsync();
        await using var viewModel = new MainWindowViewModel(catalog);
        var image = new ImageFile("resident.jpg")
        {
            Thumbnail = CreateBitmap()
        };
        viewModel.Library.SetImages([image]);
        var request = ThumbnailSizeRequest.For(LibraryThumbnailSize.Large);
        using var result = ThumbnailLoadResult.Deferred(request);

        viewModel.ApplyThumbnailLoadResult(image, result);

        Assert.Equal(request, result.Request);
        Assert.Equal(512, image.ThumbnailUpgradeDeferredDimension);
        Assert.False(image.ThumbnailDeferredForHydration);
        Assert.False(image.SourceRequiresHydration);
        Assert.Equal(0, viewModel.OnlineOnlyPhotoCount);
    }

    [WindowsFact]
    public async Task ResidentBitmap_KeepsPixelsAndShowsUpgradeFailure()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "failure-vm"));
        await catalog.InitializeAsync();
        await using var viewModel = new MainWindowViewModel(catalog);
        var image = new ImageFile("resident.jpg")
        {
            Thumbnail = CreateBitmap()
        };
        viewModel.Library.SetImages([image]);
        var request = ThumbnailSizeRequest.For(LibraryThumbnailSize.Large);
        using var result = ThumbnailLoadResult.Failed(request);

        viewModel.ApplyThumbnailLoadResult(image, result);

        Assert.Equal(request, result.Request);
        Assert.Equal(512, image.ThumbnailUpgradeFailedDimension);
        Assert.True(image.ThumbnailLoadFailed);
        Assert.True(image.HasVisibleLoadFailure);
        Assert.NotNull(image.Thumbnail);
    }

    [WindowsFact]
    public async Task DeferredPrefetch_DoesNotEvictLocalResidents()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "resident-catalog"));
        await catalog.InitializeAsync();
        await using var viewModel = new MainWindowViewModel(catalog);
        var residents = Enumerable.Range(0, 512)
            .Select(index => new ImageFile($"resident-{index}.jpg")
            {
                Thumbnail = CreateBitmap()
            })
            .ToList();
        var deferred = Enumerable.Range(0, 100)
            .Select(index => new ImageFile($"cloud-{index}.jpg")
            {
                ThumbnailDeferredForHydration = true
            })
            .ToList();
        viewModel.Library.SetImages(residents.Concat(deferred));

        viewModel.ReserveThumbnailResidency(deferred);

        Assert.Equal(512, residents.Count(image => image.Thumbnail != null));
    }

    private static WriteableBitmap CreateBitmap() => new(
        new PixelSize(1, 1),
        new Vector(96, 96),
        PixelFormat.Bgra8888,
        AlphaFormat.Premul);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
