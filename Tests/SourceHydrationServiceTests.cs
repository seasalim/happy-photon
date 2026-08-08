using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SourceHydrationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-source-hydration-{Guid.NewGuid():N}");

    [Fact]
    public async Task ExplicitHydration_ReadsAndRechecksTheSelectedSource()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "photo.jpg");
        await File.WriteAllBytesAsync(path, new byte[128 * 1024]);
        var checks = 0;
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.RequiresHydration)
        {
            Resolver = _ => Interlocked.Increment(ref checks) == 1
                ? SourceAvailability.RequiresHydration
                : SourceAvailability.AvailableLocally
        };
        var service = new SourceHydrationService(availability);

        var hydrated = await service.HydrateAsync(
            new ImageFile(path),
            CancellationToken.None);

        Assert.True(hydrated);
        Assert.Equal(2, checks);
    }

    [Fact]
    public async Task UnavailableSource_IsNeverOpened()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "photo.jpg");
        await File.WriteAllBytesAsync(path, [1]);
        var service = new SourceHydrationService(
            new TestSourceAvailabilityService(
                SourceAvailability.Unavailable));

        var hydrated = await service.HydrateAsync(
            new ImageFile(path),
            CancellationToken.None);

        Assert.False(hydrated);
    }

    [Fact]
    public async Task DownloadAndOpen_RefreshesCloudStateAndMetadata()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "photo.jpg");
        await File.WriteAllBytesAsync(path, new byte[128 * 1024]);
        var checks = 0;
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.RequiresHydration)
        {
            Resolver = _ => Interlocked.Increment(ref checks) <= 2
                ? SourceAvailability.RequiresHydration
                : SourceAvailability.AvailableLocally
        };
        using var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        await catalog.InitializeAsync();
        var metadataLoads = 0;
        await using var viewModel = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            image =>
            {
                metadataLoads++;
                image.ApplyMetadata(new ImageMetadata { FileSize = 128 * 1024 });
                return Task.CompletedTask;
            },
            availability);
        var image = new ImageFile(path)
        {
            SourceRequiresHydration = true,
            ThumbnailDeferredForHydration = true,
            EditSettings = new EditSettings { Exposure = 1.25 }
        };
        viewModel.Library.SetImages([image]);
        viewModel.InitializeCloudSourceCount([image]);
        viewModel.SelectedImage = image;

        await viewModel.DownloadAndOpenCommand.ExecuteAsync(null);

        Assert.False(image.SourceRequiresHydration);
        Assert.False(image.ThumbnailDeferredForHydration);
        Assert.True(image.MetadataLoaded);
        Assert.Equal(1, metadataLoads);
        Assert.Equal(0, viewModel.OnlineOnlyPhotoCount);
        Assert.True(viewModel.IsDevelopMode);
        Assert.True(viewModel.CanEditSelectedImage);
        Assert.Equal(1.25, viewModel.Exposure);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class NullBaseLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;
        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) => null;
        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) => null;
    }
}
