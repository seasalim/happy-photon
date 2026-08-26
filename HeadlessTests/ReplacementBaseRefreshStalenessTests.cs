using Avalonia.Threading;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ReplacementBaseRefreshStalenessTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();
    private CatalogService? _catalog;

    // A replacement-base refresh can finish rendering, then wait on its ready
    // gate while a newer render generation settles for the same image. When the
    // gate releases the stale refresh must not overwrite the newer preview.
    //
    // The view model is a live reactive system, so every step below runs with
    // no dispatcher pump in between: that freezes the selection/histogram
    // cascade and isolates the refresh-application decision under test.
    [AvaloniaFact]
    public async Task StaleRefreshCannotOverwriteNewerRenderGeneration()
    {
        var vm = CreateDevelopViewModel(out var image);
        using var source = new MagickImage(MagickColors.Red, 4, 3);

        var newerPreview = BitmapConversionService.ConvertToBitmap(source)!;
        vm.ReplacePreviewImage(newerPreview, PreviewPaintSource.FreshRender);

        var currentGeneration = vm.LatestPreviewOutcomeGeneration;

        // A replacement-base refresh for an older generation is released late,
        // after the newer render already won.
        var staleRefresh = BitmapConversionService.ConvertToBitmap(source)!;
        var staleMask = new ClippingMask(
            4,
            3,
            ClippingOverlaySide.DisplayFloor,
            new byte[12]);
        vm.ApplyPreviewRefresh(
            image,
            staleRefresh,
            new HistogramData(),
            hasHistogram: false,
            rawHistogram: null,
            generation: currentGeneration - 1,
            clipping: new ClippingStats(
                ChannelClip.Empty,
                ChannelClip.Empty,
                0,
                1,
                IsHighAvailable: false),
            isRawSource: false,
            clippingMask: staleMask);

        Assert.Same(newerPreview, vm.PreviewImage);
        Assert.False(vm.IsClippingStatsAvailable);
        Assert.Throws<ObjectDisposedException>(() => _ = staleRefresh.PixelSize);
        Assert.Throws<ObjectDisposedException>(() => _ = staleMask.Flags.Length);

        await vm.DisposeAsync();
    }

    // A service-manufactured generation has no authority to advance the VM
    // surface. Only exact equality with the reserved generation is accepted.
    [AvaloniaFact]
    public async Task GreaterThanReservedRefreshIsRejected()
    {
        var vm = CreateDevelopViewModel(out var image);
        using var source = new MagickImage(MagickColors.Red, 4, 3);

        var olderPreview = BitmapConversionService.ConvertToBitmap(source)!;
        vm.ReplacePreviewImage(olderPreview, PreviewPaintSource.FreshRender);

        var currentGeneration = vm.LatestPreviewOutcomeGeneration;

        var freshRefresh = BitmapConversionService.ConvertToBitmap(source)!;
        vm.ApplyPreviewRefresh(
            image,
            freshRefresh,
            new HistogramData(),
            hasHistogram: false,
            rawHistogram: null,
            generation: currentGeneration + 1);

        Assert.Same(olderPreview, vm.PreviewImage);
        Assert.Throws<ObjectDisposedException>(() => _ = freshRefresh.PixelSize);

        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task ExactReservedRefreshIsApplied()
    {
        var vm = CreateDevelopViewModel(out var image);
        using var source = new MagickImage(MagickColors.Red, 4, 3);

        var previous = BitmapConversionService.ConvertToBitmap(source)!;
        vm.ReplacePreviewImage(previous, PreviewPaintSource.FreshRender);
        var fresh = BitmapConversionService.ConvertToBitmap(source)!;
        vm.ApplyPreviewRefresh(
            image,
            fresh,
            new HistogramData(),
            hasHistogram: true,
            rawHistogram: null,
            generation: vm.LatestPreviewOutcomeGeneration);

        Assert.Same(fresh, vm.PreviewImage);
        Assert.NotNull(vm.Histogram);

        await vm.DisposeAsync();
    }

    private MainWindowViewModel CreateDevelopViewModel(out ImageFile image)
    {
        _catalog = new CatalogService(Path.Combine(_root.Path, "catalog"));
        var vm = new MainWindowViewModel(
            _catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally))
        {
            IsDevelopMode = true
        };
        image = new ImageFile(Path.Combine(_root.Path, "photo.jpg"));
        // Selecting the image starts an async preview load that is left
        // suspended (never pumped), so its render never clobbers the state
        // the test installs directly below.
        vm.SelectedImage = image;
        return vm;
    }

    public void Dispose()
    {
        _catalog?.Dispose();
        _root.Dispose();
    }
}
