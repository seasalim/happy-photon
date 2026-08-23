using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class EditDebounceDisposalTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("edit-debounce-dispose");

    [AvaloniaFact]
    public async Task DisposeWaitsForTheInFlightDebouncedEditUpdate()
    {
        using var catalog = await _fx.CreateCatalogAsync();
        var vm = _fx.CreateViewModel(
            catalog,
            new SolidRawLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        Task? disposeTask = null;
        var renderEntered = NewSignal();
        var releaseRender = NewSignal();
        var drainStarted = NewSignal();
        var drainEnded = NewSignal();
        var updateDoneWhenDrainEnded = false;
        vm.PreviewDebounceDrainStarted += () => drainStarted.TrySetResult();

        try
        {
            vm.SelectedImage = new ImageFile(_fx.Path("photo.dng"));
            await TestWaits.UntilAsync(() => vm.PreviewImage != null);

            vm.ImageService.Previews.RenderGateAsync = () =>
            {
                renderEntered.TrySetResult();
                return releaseRender.Task;
            };

            vm.Exposure = 1.0;
            await renderEntered.Task.WaitAsync(TestWaits.Condition);
            var tracked = vm.PendingPreviewDebounceTask;
            Assert.NotNull(tracked);
            // The handler samples the invariant at the drain's completion
            // instant, so no assertion below depends on scheduling delays.
            vm.PreviewDebounceDrainCompleted += () =>
            {
                updateDoneWhenDrainEnded = tracked!.IsCompleted;
                drainEnded.TrySetResult();
            };

            disposeTask = vm.DisposeAsync().AsTask();
            // Without the drain, disposal never raises this and the wait
            // times out red; with it, disposal is awaiting the update that
            // is still parked at the render gate.
            await drainStarted.Task.WaitAsync(TestWaits.Condition);
            Assert.False(disposeTask.IsCompleted);

            releaseRender.TrySetResult();
            await drainEnded.Task.WaitAsync(TestWaits.Condition);
            Assert.True(updateDoneWhenDrainEnded);
        }
        finally
        {
            releaseRender.TrySetResult();
            await (disposeTask ?? vm.DisposeAsync().AsTask())
                .WaitAsync(TestWaits.Condition);
        }
    }

    [AvaloniaFact]
    public async Task DisposeWaitsForEverySupersededDebouncedEditUpdate()
    {
        using var catalog = await _fx.CreateCatalogAsync("superseded");
        var vm = _fx.CreateViewModel(
            catalog,
            new SolidRawLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        Task? disposeTask = null;
        var enteredFirst = NewSignal();
        var releaseFirst = NewSignal();
        var enteredSecond = NewSignal();
        var releaseSecond = NewSignal();
        var drainStarted = NewSignal();
        var drainEnded = NewSignal();
        var updatesDoneWhenDrainEnded = false;
        var renderEntries = 0;
        vm.PreviewDebounceDrainStarted += () => drainStarted.TrySetResult();

        try
        {
            vm.SelectedImage = new ImageFile(_fx.Path("photo.dng"));
            await TestWaits.UntilAsync(() => vm.PreviewImage != null);

            vm.ImageService.Previews.RenderGateAsync = () =>
            {
                if (Interlocked.Increment(ref renderEntries) == 1)
                {
                    enteredFirst.TrySetResult();
                    return releaseFirst.Task;
                }
                enteredSecond.TrySetResult();
                return releaseSecond.Task;
            };

            vm.Exposure = 1.0;
            await enteredFirst.Task.WaitAsync(TestWaits.Condition);
            // The second edit supersedes the first debounce while its update
            // is still in flight; disposal must drain both, not just the
            // newest one.
            vm.Exposure = 2.0;
            await enteredSecond.Task.WaitAsync(TestWaits.Condition);
            var tracked = vm.PendingPreviewDebounceTask;
            Assert.NotNull(tracked);
            vm.PreviewDebounceDrainCompleted += () =>
            {
                updatesDoneWhenDrainEnded = tracked!.IsCompleted;
                drainEnded.TrySetResult();
            };

            disposeTask = vm.DisposeAsync().AsTask();
            await drainStarted.Task.WaitAsync(TestWaits.Condition);
            Assert.False(disposeTask.IsCompleted);

            // Releasing only the newest update cannot finish the drain: the
            // superseded first update is still parked at its gate, keeping
            // the tracked chain incomplete.
            releaseSecond.TrySetResult();
            Assert.False(tracked!.IsCompleted);
            Assert.False(drainEnded.Task.IsCompleted);

            releaseFirst.TrySetResult();
            await drainEnded.Task.WaitAsync(TestWaits.Condition);
            Assert.True(updatesDoneWhenDrainEnded);
            await disposeTask.WaitAsync(TestWaits.Condition);
        }
        finally
        {
            releaseFirst.TrySetResult();
            releaseSecond.TrySetResult();
            await (disposeTask ?? vm.DisposeAsync().AsTask())
                .WaitAsync(TestWaits.Condition);
        }
    }

    public void Dispose() => _fx.Dispose();

    private static TaskCompletionSource NewSignal() => new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class SolidRawLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        public BaseImage LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            new(
                new MagickImage(MagickColors.Gray, 32, 24)
                {
                    ColorSpace = ColorSpace.RGB
                },
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
                    32,
                    24));

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.Loaded(
                LoadPreviewBase(file, decode, cancellationToken));

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
