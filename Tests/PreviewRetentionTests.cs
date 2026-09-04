using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class PreviewRetentionTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    [WindowsFact]
    public async Task SameImageDevelopBrowseDevelopReusesSettledPair()
    {
        using var catalog = await CreateCatalogAsync("settled");
        var loader = new CountingPairLoader();
        var vm = CreateViewModel(catalog, loader);
        var image = CreateImage("settled.jpg");

        try
        {
            vm.SelectedImage = image;
            await TestWaits.UntilAsync(() => vm.Histogram != null);

            vm.IsDevelopMode = false;
            vm.IsDevelopMode = true;
            await TestWaits.UntilAsync(() => vm.Histogram != null);

            Assert.Equal(1, loader.DecodeCount);
            Assert.Equal(1, vm.ImageService.Previews.RetainedBasePairCount);
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    [WindowsFact]
    public async Task SameImageRoundTripJoinsInFlightDecode()
    {
        using var catalog = await CreateCatalogAsync("in-flight");
        var loader = new GatedPairLoader();
        var vm = CreateViewModel(catalog, loader);
        var image = CreateImage("in-flight.jpg");

        try
        {
            vm.SelectedImage = image;
            Assert.True(loader.DecodeStarted.Wait(TestWaits.Condition));

            vm.IsDevelopMode = false;
            vm.IsDevelopMode = true;
            loader.Release.Set();
            await TestWaits.UntilAsync(() => vm.Histogram != null);

            Assert.Equal(1, loader.DecodeCount);
            Assert.Equal(1, vm.ImageService.Previews.RetainedBasePairCount);
        }
        finally
        {
            loader.Release.Set();
            await vm.DisposeAsync();
        }
    }

    [WindowsFact]
    public async Task NonNullSelectionChangeReplacesRetainedPair()
    {
        using var catalog = await CreateCatalogAsync("selection");
        var loader = new CountingPairLoader();
        var vm = CreateViewModel(catalog, loader);
        var first = CreateImage("first.jpg");
        var second = CreateImage("second.jpg");

        try
        {
            vm.SelectedImage = first;
            await TestWaits.UntilAsync(() => vm.Histogram != null);
            vm.SelectedImage = second;
            await TestWaits.UntilAsync(() =>
                loader.DecodeCount == 2 && vm.Histogram != null);

            Assert.Equal(1, vm.ImageService.Previews.RetainedBasePairCount);
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    [WindowsTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AvailabilityAndFolderReplacementRetirePair(
        bool availabilityInvalidation)
    {
        using var catalog = await CreateCatalogAsync(
            availabilityInvalidation ? "availability" : "folder");
        var vm = CreateViewModel(catalog, new CountingPairLoader());
        var image = CreateImage(
            availabilityInvalidation ? "availability.jpg" : "folder.jpg");

        try
        {
            vm.SelectedImage = image;
            await TestWaits.UntilAsync(() => vm.Histogram != null);
            Assert.Equal(1, vm.ImageService.Previews.RetainedBasePairCount);

            if (availabilityInvalidation)
            {
                vm.ApplyThumbnailLoadStatus(
                    image,
                    ThumbnailLoadStatus.DeferredForHydration);
            }
            else
            {
                vm.SelectedImage = null;
            }

            Assert.Equal(0, vm.ImageService.Previews.RetainedBasePairCount);
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    [WindowsFact]
    public async Task ShutdownRetiresPair()
    {
        using var catalog = await CreateCatalogAsync("shutdown");
        var vm = CreateViewModel(catalog, new CountingPairLoader());
        vm.SelectedImage = CreateImage("shutdown.jpg");
        await TestWaits.UntilAsync(() => vm.Histogram != null);

        await vm.DisposeAsync();

        Assert.Equal(0, vm.ImageService.Previews.RetainedBasePairCount);
    }

    private async Task<CatalogService> CreateCatalogAsync(string name)
    {
        var catalog = new CatalogService(Path.Combine(_root.Path, name));
        await catalog.InitializeAsync();
        return catalog;
    }

    private ImageFile CreateImage(string name)
    {
        var path = Path.Combine(_root.Path, name);
        File.WriteAllBytes(path, [0]);
        return new ImageFile(path);
    }

    private static MainWindowViewModel CreateViewModel(
        CatalogService catalog,
        IBaseImageLoader loader) =>
        new(
            catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally))
        {
            IsDevelopMode = true
        };

    public void Dispose()
    {
        _root.Dispose();
    }
}
