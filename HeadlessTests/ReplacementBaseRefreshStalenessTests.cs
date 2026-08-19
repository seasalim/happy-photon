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
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-refresh-staleness-{Guid.NewGuid():N}")).FullName;
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

        // A newer render generation settled and was recorded by the outcome
        // path, exactly as a fresh edit render would report it.
        vm.ApplyPreviewLoadOutcome(new PreviewLoadOutcome(
            image,
            generation: 100,
            BaseImageLoadFailure.None));

        // A replacement-base refresh for an older generation is released late,
        // after the newer render already won.
        var staleRefresh = BitmapConversionService.ConvertToBitmap(source)!;
        vm.ApplyPreviewRefresh(
            image,
            staleRefresh,
            new HistogramData(),
            hasHistogram: false,
            rawHistogram: null,
            generation: 50);

        Assert.Same(newerPreview, vm.PreviewImage);
        Assert.Throws<ObjectDisposedException>(() => _ = staleRefresh.PixelSize);

        await vm.DisposeAsync();
    }

    // A refresh whose generation is newer than the last one applied must still
    // be installed, so the guard does not reject legitimate replacement bases.
    [AvaloniaFact]
    public async Task FreshRefreshForNewerGenerationIsApplied()
    {
        var vm = CreateDevelopViewModel(out var image);
        using var source = new MagickImage(MagickColors.Red, 4, 3);

        var olderPreview = BitmapConversionService.ConvertToBitmap(source)!;
        vm.ReplacePreviewImage(olderPreview, PreviewPaintSource.FreshRender);

        vm.ApplyPreviewLoadOutcome(new PreviewLoadOutcome(
            image,
            generation: 100,
            BaseImageLoadFailure.None));

        var freshRefresh = BitmapConversionService.ConvertToBitmap(source)!;
        vm.ApplyPreviewRefresh(
            image,
            freshRefresh,
            new HistogramData(),
            hasHistogram: false,
            rawHistogram: null,
            generation: 101);

        Assert.Same(freshRefresh, vm.PreviewImage);

        await vm.DisposeAsync();
    }

    private MainWindowViewModel CreateDevelopViewModel(out ImageFile image)
    {
        _catalog = new CatalogService(Path.Combine(_root, "catalog"));
        var vm = new MainWindowViewModel(
            _catalog,
            new NullLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally))
        {
            IsDevelopMode = true
        };
        image = new ImageFile(Path.Combine(_root, "photo.jpg"));
        // Selecting the image starts an async preview load that is left
        // suspended (never pumped), so its render never clobbers the state
        // the test installs directly below.
        vm.SelectedImage = image;
        return vm;
    }

    public void Dispose()
    {
        _catalog?.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class NullLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.FromImage(
                LoadPreviewBase(file, decode, cancellationToken),
                BaseImageLoadFailure.DecodeFailed);

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
