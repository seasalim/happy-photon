using Avalonia;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class ViewportRestingPreviewTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"HappyPhotonRestingPreview_{Guid.NewGuid():N}");
    private CatalogService _catalog = null!;
    private ImageFile _image = null!;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "source.jpg");
        await File.WriteAllBytesAsync(path, [0]);
        _image = new ImageFile(path);
        _catalog = new CatalogService(_directory);
        await _catalog.InitializeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _catalog.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [WindowsFact]
    public async Task RestingRender_IsDisplayOnlyAndKeepsQ90AtInteractiveSize()
    {
        var loader = new CountingPairLoader();
        var cache = new PreviewCacheService(_catalog);
        await using (var service = new PreviewService(
            _catalog,
            loader,
            new RenderPipeline(),
            cache,
            new RenderedThumbnailCacheService(_catalog),
            createRenderedThumbnail: false))
        {
            var (interactive, _) = await service.ApplyEditsToPreviewAsync(
                _image,
                new EditSettings(),
                skipHistogram: true);
            Assert.NotNull(interactive);
            var identity = service.TryGetPreviewRenderIdentity(interactive!);
            Assert.NotNull(identity);
            Assert.Equal(new PixelSize(400, 200), identity!.OriginalImageSize);
            Assert.Equal(new PixelSize(400, 200), identity.OriginalViewSize);

            using var resting = await service.RenderRestingPreviewAsync(
                _image,
                new EditSettings(),
                240,
                identity!,
                CancellationToken.None);
            Assert.NotNull(resting);
            Assert.Equal(240, resting!.RenderedLongEdge);
            Assert.Equal(1, loader.DecodeCount);
            Assert.True(service.TransferCurrentRenderedBitmap(
                interactive!,
                identity!));
            service.ClearPreviewCache();
        }

        await using var verifier = new PreviewCacheService(_catalog);
        using var cached = verifier.LoadRenderedPreview(_image);
        Assert.NotNull(cached);
        Assert.Equal(160u, cached!.Image.Width);
        Assert.Equal(80u, cached.Image.Height);
    }

    [WindowsFact]
    public async Task NewInteractiveGeneration_SupersedesRestingRender()
    {
        var loader = new CountingPairLoader();
        await using var service = CreateService(loader);
        var (interactive, _) = await service.ApplyEditsToPreviewAsync(
            _image,
            new EditSettings(),
            skipHistogram: true);
        using (interactive)
        {
            var identity = service.TryGetPreviewRenderIdentity(interactive!);
            Assert.NotNull(identity);
            using var started = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            service.RestingStageStarted = stage =>
            {
                if (stage != "pipeline") return;
                started.Set();
                release.Wait(TestWaits.Condition);
            };

            var restingTask = service.RenderRestingPreviewAsync(
                _image,
                new EditSettings(),
                240,
                identity!,
                CancellationToken.None);
            Assert.True(started.Wait(TestWaits.Condition));
            var newerTask = service.ApplyEditsToPreviewAsync(
                _image,
                new EditSettings { Exposure = 0.25 },
                skipHistogram: true);
            release.Set();

            using var resting = await restingTask;
            var (newer, _) = await newerTask;
            using (newer)
            {
                Assert.Null(resting);
                Assert.NotNull(newer);
                Assert.Equal(1, loader.DecodeCount);
            }
        }
    }

    [WindowsFact]
    public async Task DisposeAsync_WaitsForCancelledRestingRender()
    {
        var loader = new CountingPairLoader();
        await using var service = CreateService(loader);
        var (interactive, _) = await service.ApplyEditsToPreviewAsync(
            _image,
            new EditSettings(),
            skipHistogram: true);
        using (interactive)
        {
            var identity = service.TryGetPreviewRenderIdentity(interactive!);
            Assert.NotNull(identity);
            using var started = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var cancellation = new CancellationTokenSource();
            service.RestingStageStarted = stage =>
            {
                if (stage != "pipeline") return;
                started.Set();
                release.Wait(TestWaits.Condition);
            };

            var restingTask = service.RenderRestingPreviewAsync(
                _image,
                new EditSettings(),
                240,
                identity!,
                cancellation.Token);
            Assert.True(started.Wait(TestWaits.Condition));
            cancellation.Cancel();
            var disposeTask = service.DisposeAsync().AsTask();
            Assert.False(disposeTask.IsCompleted);

            release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => restingTask);
            await disposeTask;
        }
    }

    [WindowsFact]
    public async Task ViewModel_SettlesAfterHistogramWithoutAnotherDecode()
    {
        var clock = new TestTimeProvider();
        var loader = new CountingPairLoader();
        var viewModel = new MainWindowViewModel(
            _catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            timeProvider: clock);
        viewModel.PublishRequiredDeviceLongEdge(240);
        viewModel.SelectedImage = _image;
        viewModel.IsDevelopMode = true;

        await TestWaits.UntilAsync(() => viewModel.PreviewImage != null);
        Assert.Equal(160, viewModel.PreviewImage!.PixelSize.Width);
        clock.Advance(TimeSpan.FromMilliseconds(300));
        await TestWaits.UntilAsync(() => viewModel.Histogram != null);
        await TestWaits.UntilAsync(() => viewModel.HasArmedRestingRender);
        clock.Advance(TimeSpan.FromMilliseconds(75));
        await TestWaits.UntilAsync(() =>
            viewModel.PreviewImage != null &&
            viewModel.PreviewImage.PixelSize.Width == 240);

        Assert.Equal(1, viewModel.RestingPaintCount);
        Assert.Equal(1, loader.DecodeCount);

        viewModel.PublishRequiredDeviceLongEdge(242);
        clock.Advance(TimeSpan.FromMilliseconds(75));

        Assert.Equal(1, viewModel.RestingPaintCount);
        Assert.Equal(240, viewModel.PreviewImage!.PixelSize.Width);

        viewModel.PublishRequiredDeviceLongEdge(300);
        clock.Advance(TimeSpan.FromMilliseconds(75));
        await TestWaits.UntilAsync(() =>
            viewModel.PreviewImage?.PixelSize.Width == 300);
        viewModel.PublishRequiredDeviceLongEdge(500);
        clock.Advance(TimeSpan.FromMilliseconds(75));
        await TestWaits.UntilAsync(() =>
            viewModel.PreviewImage?.PixelSize.Width == 320);
        viewModel.PublishRequiredDeviceLongEdge(600);
        clock.Advance(TimeSpan.FromMilliseconds(75));

        Assert.Equal(3, viewModel.RestingPaintCount);
        Assert.Equal(1, loader.DecodeCount);
        await viewModel.DisposeAsync();
    }

    [WindowsFact]
    public async Task ZoomSettle_RestsAtCapAndIgnoresPanAndZoomOut()
    {
        var clock = new TestTimeProvider();
        var loader = new CountingPairLoader();
        var viewModel = new MainWindowViewModel(
            _catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            timeProvider: clock);
        viewModel.PublishRequiredDeviceLongEdge(165);
        viewModel.SelectedImage = _image;
        viewModel.IsDevelopMode = true;

        await TestWaits.UntilAsync(() => viewModel.PreviewImage != null);
        Assert.Equal(new PixelSize(400, 200), viewModel.OriginalViewPixelSize);
        clock.Advance(TimeSpan.FromMilliseconds(300));
        await TestWaits.UntilAsync(() => viewModel.Histogram != null);
        await TestWaits.UntilAsync(() => viewModel.HasArmedRestingRender);

        viewModel.ApplyManualZoom(1.25);
        viewModel.PublishRequiredDeviceLongEdge(500);
        clock.Advance(TimeSpan.FromMilliseconds(75));
        await TestWaits.UntilAsync(() =>
            viewModel.PreviewImage?.PixelSize.Width == 320);

        Assert.Equal(1.25, viewModel.ManualZoomLevel);
        Assert.Equal(1, viewModel.RestingPaintCount);
        viewModel.PublishNavigatorVisibleRegion(new Rect(0.1, 0.1, 0.5, 0.5));
        clock.Advance(TimeSpan.FromMilliseconds(75));
        viewModel.ApplyManualZoom(0.5);
        viewModel.PublishRequiredDeviceLongEdge(200);
        clock.Advance(TimeSpan.FromMilliseconds(75));

        Assert.Equal(1, viewModel.RestingPaintCount);
        Assert.Equal(1, loader.DecodeCount);
        await viewModel.DisposeAsync();
    }

    [WindowsFact]
    public async Task EditInput_CancelsPendingRestingSettle()
    {
        var clock = new TestTimeProvider();
        var loader = new CountingPairLoader();
        var viewModel = new MainWindowViewModel(
            _catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            timeProvider: clock);
        viewModel.PublishRequiredDeviceLongEdge(240);
        viewModel.SelectedImage = _image;
        viewModel.IsDevelopMode = true;

        await TestWaits.UntilAsync(() => viewModel.PreviewImage != null);
        clock.Advance(TimeSpan.FromMilliseconds(300));
        await TestWaits.UntilAsync(() => viewModel.Histogram != null);
        await TestWaits.UntilAsync(() => viewModel.HasArmedRestingRender);
        var settledInteractive = viewModel.PreviewImage;

        viewModel.Exposure = 0.5;
        clock.Advance(TimeSpan.FromMilliseconds(150));
        await TestWaits.UntilAsync(() =>
            viewModel.PreviewImage != null &&
            !ReferenceEquals(viewModel.PreviewImage, settledInteractive));

        Assert.Equal(0, viewModel.RestingPaintCount);
        Assert.Equal(160, viewModel.PreviewImage!.PixelSize.Width);
        Assert.Equal(1, loader.DecodeCount);
        await viewModel.DisposeAsync();
    }

    [WindowsFact]
    public async Task DevelopIdle_DoesNotScheduleDuplicateHistogramRender()
    {
        var clock = new TestTimeProvider();
        var viewModel = new MainWindowViewModel(
            _catalog,
            new CountingPairLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            timeProvider: clock);
        viewModel.PublishRequiredDeviceLongEdge(240);
        viewModel.SelectedImage = _image;
        viewModel.IsDevelopMode = true;
        await TestWaits.UntilAsync(() => viewModel.PreviewImage != null);

        using var histogramStarted = new ManualResetEventSlim();
        var releaseHistogram = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.ImageService.Previews.RenderGateAsync = async () =>
        {
            histogramStarted.Set();
            await releaseHistogram.Task;
        };
        clock.Advance(TimeSpan.FromMilliseconds(300));
        Assert.False(histogramStarted.Wait(TimeSpan.FromMilliseconds(100)));
        Assert.Equal(1, viewModel.RestingPaintCount);
        Assert.Equal(240, viewModel.PreviewImage!.PixelSize.Width);
        releaseHistogram.SetResult();
        await viewModel.DisposeAsync();
    }

    [WindowsTheory]
    [InlineData("selection")]
    [InlineData("mode")]
    [InlineData("crop")]
    [InlineData("before-after")]
    [InlineData("preset-hover")]
    public async Task TransientOrSupersedingState_CancelsPendingRestingRender(
        string transition)
    {
        var clock = new TestTimeProvider();
        var viewModel = new MainWindowViewModel(
            _catalog,
            new CountingPairLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            timeProvider: clock);
        await viewModel.InitializeAsync();
        _image.EditSettings = new EditSettings { Exposure = 0.1 };
        viewModel.PublishRequiredDeviceLongEdge(240);
        viewModel.SelectedImage = _image;
        viewModel.IsDevelopMode = true;
        await TestWaits.UntilAsync(() => viewModel.PreviewImage != null);
        clock.Advance(TimeSpan.FromMilliseconds(300));
        await TestWaits.UntilAsync(() => viewModel.Histogram != null);
        await TestWaits.UntilAsync(() => viewModel.HasArmedRestingRender);

        switch (transition)
        {
            case "selection":
                var secondPath = Path.Combine(_directory, "second.jpg");
                await File.WriteAllBytesAsync(secondPath, [0]);
                viewModel.SelectedImage = new ImageFile(secondPath);
                break;
            case "mode":
                viewModel.IsDevelopMode = false;
                break;
            case "crop":
                viewModel.ToggleCropModeCommand.Execute(null);
                break;
            case "before-after":
                await viewModel.ToggleBeforeAfterCommand.ExecuteAsync(null);
                break;
            case "preset-hover":
                var preset = await viewModel.PresetService.SaveUserPresetAsync(
                    "Resting hover",
                    new EditSettings { Contrast = 20 });
                await viewModel.PreviewPresetHoverAsync(preset.Id);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(transition));
        }

        clock.Advance(TimeSpan.FromMilliseconds(75));

        Assert.Equal(0, viewModel.RestingPaintCount);
        await viewModel.DisposeAsync();
    }

    private PreviewService CreateService(CountingPairLoader loader) =>
        new(
            _catalog,
            loader,
            new RenderPipeline(),
            new PreviewCacheService(_catalog),
            new RenderedThumbnailCacheService(_catalog),
            createRenderedThumbnail: false);

}
