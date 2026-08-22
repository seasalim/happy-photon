using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PromotionOutcomeTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-promotion-outcomes-{Guid.NewGuid():N}")).FullName;

    [AvaloniaFact]
    public async Task RejectedRenderDisposesLeaseWithoutStartingPromotion()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "rejected"));
        await catalog.InitializeAsync();
        var vm = CreateViewModel(catalog);
        vm.SelectedImage = EditedImage("rejected.dng");
        var started = new[] { NewSignal(), NewSignal() };
        var release = new[] { NewSignal(), NewSignal() };
        var gateIndex = -1;

        try
        {
            await TestWaits.UntilAsync(() => vm.PreviewImage != null);
            await TestWaits.UntilAsync(() =>
                vm.ImageService.Previews.RenderedThumbnailTaskCount == 0);
            var promotions = 0;
            var promoted = NewSignal();
            vm.ImageService.Previews.RenderedThumbnailCreated += () =>
            {
                Interlocked.Increment(ref promotions);
                promoted.TrySetResult();
            };
            vm.ImageService.Previews.RenderGateAsync = () =>
            {
                var index = Interlocked.Increment(ref gateIndex);
                started[index].TrySetResult();
                return release[index].Task;
            };

            vm.Exposure = 1;
            await started[0].Task.WaitAsync(TestWaits.Condition);
            vm.Exposure = 2;
            await started[1].Task.WaitAsync(TestWaits.Condition);
            release[0].TrySetResult();
            release[1].TrySetResult();
            await promoted.Task.WaitAsync(TestWaits.Condition);

            Assert.Equal(1, Volatile.Read(ref promotions));
        }
        finally
        {
            release[0].TrySetResult();
            release[1].TrySetResult();
            await vm.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task ChannelCloseRejectsCompletedOutcomeWithoutPromotion()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "shutdown"));
        await catalog.InitializeAsync();
        var vm = CreateViewModel(catalog);
        vm.SelectedImage = EditedImage("shutdown.dng");
        var started = NewSignal();
        var release = NewSignal();
        var promotions = 0;

        await TestWaits.UntilAsync(() => vm.PreviewImage != null);
        await TestWaits.UntilAsync(() =>
            vm.ImageService.Previews.RenderedThumbnailTaskCount == 0);
        vm.ImageService.Previews.RenderedThumbnailCreated += () =>
            Interlocked.Increment(ref promotions);
        vm.ImageService.Previews.RenderGateAsync = () =>
        {
            started.TrySetResult();
            return release.Task;
        };
        vm.Exposure = 1;
        await started.Task.WaitAsync(TestWaits.Condition);

        var dispose = vm.DisposeAsync().AsTask();
        release.TrySetResult();
        await dispose.WaitAsync(TestWaits.Condition);

        Assert.Equal(0, Volatile.Read(ref promotions));
    }

    [AvaloniaFact]
    public async Task ClippingRenderDoesNotRevokeAcceptedSliderPromotion()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "clipping-race"));
        await catalog.InitializeAsync();
        var vm = CreateViewModel(catalog);
        vm.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.SelectedImage = EditedImage("clipping-race.dng");
        var sliderStarted = NewSignal();
        var releaseSlider = NewSignal();
        var gateCalls = 0;

        try
        {
            await TestWaits.UntilAsync(() => vm.PreviewImage != null);
            await TestWaits.UntilAsync(() =>
                vm.ImageService.Previews.RenderedThumbnailTaskCount == 0);
            var promoted = NewSignal();
            vm.ImageService.Previews.RenderedThumbnailCreated += () =>
                promoted.TrySetResult();
            vm.ImageService.Previews.RenderGateAsync = () =>
            {
                if (Interlocked.Increment(ref gateCalls) == 1)
                {
                    sliderStarted.TrySetResult();
                    return releaseSlider.Task;
                }
                return Task.CompletedTask;
            };

            vm.Exposure = 1;
            await sliderStarted.Task.WaitAsync(TestWaits.Condition);
            vm.ToggleClippingOverlayCommand.Execute(null);
            await TestWaits.UntilAsync(() => vm.PreviewClippingMask != null);
            releaseSlider.TrySetResult();
            await promoted.Task.WaitAsync(TestWaits.Condition);

            Assert.True(gateCalls >= 2);
        }
        finally
        {
            releaseSlider.TrySetResult();
            await vm.DisposeAsync();
        }
    }

    [AvaloniaTheory]
    [InlineData("before")]
    [InlineData("hover")]
    [InlineData("crop")]
    public async Task TransientRenderNeverPromotes(string transition)
    {
        using var catalog = new CatalogService(Path.Combine(_root, transition));
        await catalog.InitializeAsync();
        var vm = CreateViewModel(catalog);
        vm.SelectedImage = EditedImage($"{transition}.dng");

        try
        {
            await TestWaits.UntilAsync(() => vm.PreviewImage != null);
            await TestWaits.UntilAsync(() =>
                vm.ImageService.Previews.RenderedThumbnailTaskCount == 0);
            var promotions = 0;
            var converted = NewSignal();
            vm.ImageService.Previews.RenderedThumbnailCreated += () =>
                Interlocked.Increment(ref promotions);
            vm.ImageService.Previews.PreviewConverted += () =>
                converted.TrySetResult();

            switch (transition)
            {
                case "before":
                    await vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
                    break;
                case "hover":
                    await vm.PresetService.UseDirectoryAsync(
                        Path.Combine(_root, "presets"));
                    var preset = await vm.PresetService.SaveUserPresetAsync(
                        "Hover",
                        new EditSettings { Exposure = 1.5 });
                    await vm.PreviewPresetHoverAsync(preset.Id);
                    break;
                case "crop":
                    vm.IsCropMode = true;
                    vm.Exposure = 1;
                    await converted.Task.WaitAsync(TestWaits.Condition);
                    break;
            }

            Assert.Equal(0, Volatile.Read(ref promotions));
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task BeforeAfterPreservesLegacyLensDecodeIdentity()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "legacy-before"));
        await catalog.InitializeAsync();
        var loader = new GrayRawLoader();
        var vm = CreateViewModel(catalog, loader);
        var image = EditedImage("legacy-before.dng");
        image.EditSettings.Lens = LensSettings.Legacy();
        var expectedDecodeKey = BaseDecodeSettings.From(image.EditSettings).CacheKey;
        vm.SelectedImage = image;

        try
        {
            await TestWaits.UntilAsync(() => vm.PreviewImage != null);
            await TestWaits.UntilAsync(() =>
                vm.ImageService.Previews.RenderedThumbnailTaskCount == 0);

            await vm.ToggleBeforeAfterCommand.ExecuteAsync(null);

            Assert.True(vm.IsShowingOriginal);
            var identity = vm.ImageService.Previews.TryGetPreviewRenderIdentity(
                vm.PreviewImage!);
            Assert.NotNull(identity);
            Assert.Equal(expectedDecodeKey, identity.DecodeKey);
            Assert.NotEmpty(loader.Decodes);
            Assert.All(loader.Decodes, decode =>
                Assert.Equal(expectedDecodeKey, decode.CacheKey));
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    private MainWindowViewModel CreateViewModel(
        CatalogService catalog,
        GrayRawLoader? loader = null) =>
        new(
            catalog,
            loader ?? new GrayRawLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally))
        {
            IsDevelopMode = true
        };

    private ImageFile EditedImage(string name) =>
        new(Path.Combine(_root, name))
        {
            EditSettings = new EditSettings { Exposure = 0.5 },
            HasEdits = true
        };

    private static TaskCompletionSource NewSignal() => new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class GrayRawLoader : IBaseImageLoader
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<
            BaseDecodeSettings> _decodes = new();

        internal IReadOnlyList<BaseDecodeSettings> Decodes => _decodes.ToArray();

        public bool CanLoad(ImageFile file) => true;

        public BaseImage LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            _decodes.Enqueue(decode);
            return new(
                new MagickImage(MagickColors.Gray, 64, 48),
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
                    64,
                    48));
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
}
