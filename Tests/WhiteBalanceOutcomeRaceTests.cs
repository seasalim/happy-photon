using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WhiteBalanceOutcomeRaceTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-wb-races-{Guid.NewGuid():N}")).FullName;

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SamplingAfterSelectionChangeCannotCommit(bool eyedropper)
    {
        using var catalog = new CatalogService(_root);
        await catalog.InitializeAsync();
        var vm = CreateViewModel(catalog, new GradientLoader());
        var first = new ImageFile(Path.Combine(_root, "first.dng"));
        var second = new ImageFile(Path.Combine(_root, "second.dng"));
        vm.SelectedImage = first;
        var started = NewSignal();
        var release = NewSignal();

        try
        {
            await TestWaits.UntilAsync(() => vm.IsWhiteBalanceReady);
            vm.ImageService.Previews.WhiteBalanceSampleGateAsync = () =>
            {
                started.TrySetResult();
                return release.Task;
            };
            var sample = eyedropper
                ? vm.ApplyWhiteBalancePickAsync(0.5, 0.5)
                : vm.AutoWhiteBalanceCommand.ExecuteAsync(null);
            await started.Task.WaitAsync(TestWaits.Condition);

            vm.SelectedImage = second;
            release.TrySetResult();
            await sample;

            Assert.Same(second, vm.SelectedImage);
            Assert.Equal(WbMode.AsShot, second.EditSettings.Wb.Mode);
            Assert.Equal("As Shot", vm.SelectedWhiteBalanceMode);
        }
        finally
        {
            release.TrySetResult();
            await vm.DisposeAsync();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SamplingAfterNewerSameImageEditCannotCommit(bool eyedropper)
    {
        using var catalog = new CatalogService(_root);
        await catalog.InitializeAsync();
        var vm = CreateViewModel(catalog, new GradientLoader());
        var image = new ImageFile(Path.Combine(_root, "same.dng"));
        vm.SelectedImage = image;
        var started = NewSignal();
        var release = NewSignal();

        try
        {
            await TestWaits.UntilAsync(() => vm.IsWhiteBalanceReady);
            vm.ImageService.Previews.WhiteBalanceSampleGateAsync = () =>
            {
                started.TrySetResult();
                return release.Task;
            };
            var sample = eyedropper
                ? vm.ApplyWhiteBalancePickAsync(0.5, 0.5)
                : vm.AutoWhiteBalanceCommand.ExecuteAsync(null);
            await started.Task.WaitAsync(TestWaits.Condition);

            vm.Exposure = 1;
            release.TrySetResult();
            await sample;

            Assert.Equal(1, vm.Exposure);
            Assert.NotEqual("Picked", vm.SelectedWhiteBalanceMode);
            Assert.Equal(WbMode.AsShot, image.EditSettings.Wb.Mode);
        }
        finally
        {
            release.TrySetResult();
            await vm.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task FailedSampleDoesNotRejectPendingSliderRender()
    {
        using var catalog = new CatalogService(_root);
        await catalog.InitializeAsync();
        var vm = CreateViewModel(catalog, new SolidLoader(MagickColors.Black));
        vm.SelectedImage = new ImageFile(Path.Combine(_root, "black.dng"));

        try
        {
            await TestWaits.UntilAsync(() => vm.IsWhiteBalanceReady);
            var previous = vm.PreviewImage;
            var renderStarted = NewSignal();
            var releaseRender = NewSignal();
            vm.ImageService.Previews.RenderGateAsync = () =>
            {
                renderStarted.TrySetResult();
                return releaseRender.Task;
            };

            vm.Exposure = 1;
            await vm.AutoWhiteBalanceCommand.ExecuteAsync(null);
            await renderStarted.Task.WaitAsync(TestWaits.Condition);
            releaseRender.TrySetResult();
            await TestWaits.UntilAsync(() =>
                vm.PreviewImage != null &&
                !ReferenceEquals(previous, vm.PreviewImage));

            Assert.Equal(1, vm.Exposure);
            Assert.NotNull(vm.Histogram);
        }
        finally
        {
            vm.ImageService.Previews.RenderGateAsync = null;
            await vm.DisposeAsync();
        }
    }

    [Fact]
    public async Task SampleBaseTokenIsRejectedAfterReplacementBaseInstalls()
    {
        using var catalog = new CatalogService(_root);
        await catalog.InitializeAsync();
        await using var service = new PreviewService(
            catalog,
            new GradientLoader(),
            new RenderPipeline());
        var image = new ImageFile(Path.Combine(_root, "replacement.dng"));
        var initial = new EditSettings();
        using (await service.ApplyEditsToPreviewArtifactsAsync(
            image,
            initial,
            ThumbnailSizeRequest.For(BrowseThumbnailSize.Medium),
            skipHistogram: true,
            ClippingOverlaySide.None))
        {
        }
        var sample = Assert.IsType<WhiteBalanceSample>(
            await service.GetAutoWhiteBalanceSampleAsync(image, initial));
        var replacement = new EditSettings
        {
            HlReconstruction = HlReconstructionMode.Blend
        };
        using (await service.ApplyEditsToPreviewArtifactsAsync(
            image,
            replacement,
            ThumbnailSizeRequest.For(BrowseThumbnailSize.Medium),
            skipHistogram: true,
            ClippingOverlaySide.None))
        {
        }

        Assert.False(await service.IsWhiteBalanceBaseCurrentAsync(
            image,
            initial,
            sample.BaseToken));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
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

    private static TaskCompletionSource NewSignal() => new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class GradientLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        public BaseImage LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            return CreateBase(
                new MagickImage(MagickColors.Gray, 64, 48),
                decode);
        }

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.Loaded(LoadPreviewBase(
                file,
                decode,
                cancellationToken));

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class SolidLoader(MagickColor color) : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        public BaseImage LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            CreateBase(new MagickImage(color, 64, 48), decode);

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.Loaded(LoadPreviewBase(
                file,
                decode,
                cancellationToken));

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static BaseImage CreateBase(
        MagickImage pixels,
        BaseDecodeSettings decode) =>
        new(
            pixels,
            new BaseImageInfo(
                BaseSourceKind.RawLibRaw,
                true,
                decode,
                null,
                null,
                5500,
                0,
                false,
                null,
                1,
                checked((int)pixels.Width),
                checked((int)pixels.Height)));
}
