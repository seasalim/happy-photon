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

    [AvaloniaFact]
    public async Task ExactReservedRefreshIsApplied()
    {
        var vm = CreateDevelopViewModel(out var image);
        using var source = new MagickImage(MagickColors.Red, 4, 3);
        var previous = BitmapConversionService.ConvertToBitmap(source)!;
        vm.ReplacePreviewImage(previous, PreviewPaintSource.FreshRender);
        var currentGeneration = vm.LatestPreviewOutcomeGeneration;

        // Do not pump between checks: isolate refresh admission from the pending selection.
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

        Assert.Same(previous, vm.PreviewImage);
        Assert.False(vm.IsClippingStatsAvailable);
        Assert.Throws<ObjectDisposedException>(() => _ = staleRefresh.PixelSize);
        Assert.Throws<ObjectDisposedException>(() => _ = staleMask.Flags.Length);

        var unreservedRefresh = BitmapConversionService.ConvertToBitmap(source)!;
        vm.ApplyPreviewRefresh(
            image,
            unreservedRefresh,
            new HistogramData(),
            hasHistogram: false,
            rawHistogram: null,
            generation: currentGeneration + 1);

        Assert.Same(previous, vm.PreviewImage);
        Assert.Throws<ObjectDisposedException>(() => _ = unreservedRefresh.PixelSize);

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
