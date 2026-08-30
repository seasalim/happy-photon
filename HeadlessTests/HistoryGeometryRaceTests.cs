using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class HistoryGeometryRaceTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("history-geometry-races");

    [AvaloniaFact]
    public async Task OutOfOrderRotationRendersCommitInClickOrder()
    {
        using var catalog = await _fixture.CreateCatalogAsync("rotate-order");
        await using var vm = CreateViewModel(catalog);
        var image = await CreateImageAsync(catalog, "rotate-order.jpg");
        vm.SelectedImage = image;
        await WaitForReadyPreviewAsync(vm);

        var firstStarted = NewSignal();
        var secondStarted = NewSignal();
        var releaseFirst = NewSignal();
        var releaseSecond = NewSignal();
        var render = 0;
        vm.ImageService.Previews.RenderGateAsync = () =>
        {
            switch (Interlocked.Increment(ref render))
            {
                case 1:
                    firstStarted.TrySetResult();
                    return releaseFirst.Task;
                case 2:
                    secondStarted.TrySetResult();
                    return releaseSecond.Task;
                default:
                    return Task.CompletedTask;
            }
        };

        vm.RotateRightCommand.Execute(null);
        await firstStarted.Task.WaitAsync(TestWaits.Condition);
        vm.RotateRightCommand.Execute(null);
        await secondStarted.Task.WaitAsync(TestWaits.Condition);
        releaseSecond.TrySetResult();
        await Task.Yield();
        Assert.Empty(vm.HistoryEntries);
        releaseFirst.TrySetResult();
        await Assert.IsAssignableFrom<Task>(vm.PendingHistoryCommitTask);

        var entries = vm.HistoryEntries.OrderBy(entry => entry.Sequence).ToArray();
        Assert.Equal(
            ["Original", "Rotate right", "Rotate right"],
            entries.Select(entry => entry.Label));
        Assert.Equal([0, 90, 180],
            entries.Select(entry => entry.Settings.Rotation));
        var persisted = (await catalog.LoadImageStatesAsync([image.FilePath]))
            [image.FilePath].Single().EditSettings;
        Assert.Equal(180, persisted.Rotation);
    }

    [AvaloniaFact]
    public async Task SliderDuringCropApplyCommitsAfterCrop()
    {
        using var catalog = await _fixture.CreateCatalogAsync("crop-slider-order");
        var clock = new TestTimeProvider();
        await using var vm = CreateViewModel(catalog, clock);
        var image = await CreateImageAsync(catalog, "crop-slider-order.jpg");
        vm.SelectedImage = image;
        await WaitForReadyPreviewAsync(vm);
        await vm.ToggleCropModeCommand.ExecuteAsync(null);
        await SettleDebounceAsync(vm, clock);
        vm.CurrentCrop = new CropRegion
        {
            Left = .1,
            Top = .1,
            Right = .9,
            Bottom = .9
        };

        var applyStarted = NewSignal();
        var sliderStarted = NewSignal();
        var releaseApply = NewSignal();
        var render = 0;
        vm.ImageService.Previews.RenderGateAsync = () =>
        {
            if (Interlocked.Increment(ref render) == 1)
            {
                applyStarted.TrySetResult();
                return releaseApply.Task;
            }
            sliderStarted.TrySetResult();
            return Task.CompletedTask;
        };

        var apply = vm.ApplyCropCommand.ExecuteAsync(null);
        await applyStarted.Task.WaitAsync(TestWaits.Condition);
        Assert.False(vm.RotateRightCommand.CanExecute(null));
        vm.Exposure = .4;
        clock.Advance(TimeSpan.FromMilliseconds(200));
        await sliderStarted.Task.WaitAsync(TestWaits.Condition);
        releaseApply.TrySetResult();
        await apply;
        if (vm.PendingPreviewDebounceTask is { } preview) await preview;
        if (vm.PendingHistoryCommitTask is { } commit) await commit;

        Assert.Equal(
            ["Exposure +0.40 (+0.40)", "Crop", "Original"],
            vm.HistoryEntries.Select(entry => entry.Label));
        Assert.Equal(.1, vm.HistoryEntries[1].Settings.Crop!.Left);
        Assert.Equal(0, vm.HistoryEntries[1].Settings.Exposure);
        var persisted = (await catalog.LoadImageStatesAsync([image.FilePath]))
            [image.FilePath].Single().EditSettings;
        Assert.Equal(.4, persisted.Exposure);
        Assert.Equal(.1, persisted.Crop!.Left);
        Assert.Equal(.4, image.EditSettings.Exposure);
        Assert.Equal(.1, image.EditSettings.Crop!.Left);
    }

    [AvaloniaFact]
    public async Task CachedOnlyFailedCropRenderDoesNotCommitDraft()
    {
        using var catalog = await _fixture.CreateCatalogAsync("cached-crop-failure");
        var clock = new TestTimeProvider();
        var image = await CreateImageAsync(
            catalog, "cached-crop-failure.jpg", writeSource: true);
        await SeedCacheAsync(catalog, image);
        await using var vm = CreateViewModel(catalog, clock);
        var initialStarted = NewSignal();
        var releaseInitial = NewSignal();
        var render = 0;
        vm.ImageService.Previews.RenderGateAsync = () =>
        {
            if (Interlocked.Increment(ref render) == 1)
            {
                initialStarted.TrySetResult();
                return releaseInitial.Task;
            }
            return Task.FromException(
                new InvalidOperationException("render failed"));
        };

        try
        {
            vm.SelectedImage = image;
            await TestWaits.UntilAsync(() => vm.PreviewImage != null);
            await initialStarted.Task.WaitAsync(TestWaits.Condition);
            await WaitForHistoryAsync(vm);
            await vm.ToggleCropModeCommand.ExecuteAsync(null);
            await SettleDebounceAsync(vm, clock);
            var draft = new CropRegion
            {
                Left = .2,
                Top = .2,
                Right = .8,
                Bottom = .8
            };
            vm.CurrentCrop = draft;

            await vm.ApplyCropCommand.ExecuteAsync(null);

            Assert.True(vm.IsCropMode);
            Assert.Same(draft, vm.CurrentCrop);
            Assert.Null(image.EditSettings.Crop);
            Assert.Empty(vm.HistoryEntries);
            var state = await catalog.LoadEditHistoryAsync(image.CatalogId);
            Assert.Equal(-1, state.Position);
            Assert.Empty(state.Entries);
            var persisted = (await catalog.LoadImageStatesAsync([image.FilePath]))
                [image.FilePath].Single().EditSettings;
            Assert.Null(persisted.Crop);
        }
        finally
        {
            releaseInitial.TrySetResult();
        }
    }

    [AvaloniaFact]
    public async Task CropSaveFailureRepaintsFullCropModeCanvas()
    {
        using var catalog = await _fixture.CreateCatalogAsync("crop-save-failure");
        var clock = new TestTimeProvider();
        await using var vm = CreateViewModel(catalog, clock);
        var image = await CreateImageAsync(catalog, "crop-save-failure.jpg");
        vm.SelectedImage = image;
        await WaitForReadyPreviewAsync(vm);
        await vm.ToggleCropModeCommand.ExecuteAsync(null);
        await SettleDebounceAsync(vm, clock);
        var draft = new CropRegion
        {
            Left = .25,
            Top = .25,
            Right = .75,
            Bottom = .75
        };
        vm.CurrentCrop = draft;
        var renders = 0;
        vm.ImageService.Previews.RenderStarted += () =>
            Interlocked.Increment(ref renders);
        catalog.EditHistoryWriteGateAsync = () =>
            Task.FromException(new IOException("save failed"));

        await Assert.ThrowsAsync<IOException>(() =>
            vm.ApplyCropCommand.ExecuteAsync(null));

        Assert.True(vm.IsCropMode);
        Assert.Same(draft, vm.CurrentCrop);
        Assert.Null(image.EditSettings.Crop);
        Assert.True(renders >= 2);
        Assert.Equal(64, vm.PreviewImage!.PixelSize.Width);
        Assert.Equal(48, vm.PreviewImage.PixelSize.Height);
    }

    private MainWindowViewModel CreateViewModel(
        CatalogService catalog,
        TimeProvider? clock = null)
    {
        var vm = _fixture.CreateViewModel(
            catalog,
            new TinyBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            timeProvider: clock);
        vm.IsDevelopMode = true;
        return vm;
    }

    private async Task<ImageFile> CreateImageAsync(
        CatalogService catalog,
        string name,
        bool writeSource = false)
    {
        var path = _fixture.Path(name);
        if (writeSource)
        {
            using var source = new MagickImage(MagickColors.Gray, 64, 48);
            source.Write(path);
        }
        var image = new ImageFile(path) { EditSettings = new EditSettings() };
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        return image;
    }

    private static async Task SeedCacheAsync(
        CatalogService catalog,
        ImageFile image)
    {
        await using var cache = new PreviewCacheService(catalog);
        using var cached = new MagickImage(MagickColors.Red, 64, 48);
        cache.QueueSaveToCache(
            image,
            cached,
            RenderSettingsHash.Compute(image.EditSettings));
    }

    private static async Task SettleDebounceAsync(
        MainWindowViewModel vm,
        TestTimeProvider clock)
    {
        clock.Advance(TimeSpan.FromMilliseconds(200));
        if (vm.PendingPreviewDebounceTask is { } preview) await preview;
        if (vm.PendingHistoryCommitTask is { } commit) await commit;
    }

    private static async Task WaitForReadyPreviewAsync(MainWindowViewModel vm)
    {
        await WaitForHistoryAsync(vm);
        await TestWaits.UntilAsync(() => vm.PreviewImage != null);
    }

    private static Task WaitForHistoryAsync(MainWindowViewModel vm) =>
        TestWaits.UntilAsync(() => vm.IsHistoryLoaded);

    private static TaskCompletionSource NewSignal() => new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public void Dispose() => _fixture.Dispose();

    private sealed class TinyBaseLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        public BaseImage LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) => Create(decode);

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.Loaded(Create(decode));

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) => Create(decode);

        private static BaseImage Create(BaseDecodeSettings decode) =>
            new(
                new MagickImage(MagickColors.Gray, 64, 48)
                {
                    ColorSpace = ColorSpace.RGB
                },
                new BaseImageInfo(
                    BaseSourceKind.Standard,
                    false,
                    decode,
                    null,
                    null,
                    6504,
                    0,
                    false,
                    null,
                    1,
                    64,
                    48));
    }
}
