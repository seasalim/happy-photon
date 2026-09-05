using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class BeforeAfterSplitHeadlessTests
{
    [AvaloniaFact]
    public async Task RenderSetupWaitsForHeldScopePublicationBeforeSnapshot()
    {
        using var catalog = await _fx.CreateCatalogAsync("held-scope-publication");
        await using var vm = _fx.CreateViewModel(
            catalog,
            new GrayLoader(),
            _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            timeProvider: new TestTimeProvider());
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.IsDevelopMode = true;
        vm.SelectedImage = new ImageFile(_fx.Path("held-scope.jpg"));
        await TestWaits.UntilAsync(() => vm.PreviewImage != null);
        Assert.NotNull(vm.EffectiveWaveform);
        var initialHistogram = vm.Histogram;
        using var renderSetup = new RenderSetup(vm);
        var started = NewSignal();
        var release = NewSignal();
        var requests = 0;
        try
        {
            vm.ImageService.Previews.RenderGateAsync = () =>
            {
                if (Interlocked.Increment(ref requests) != 1)
                    return Task.CompletedTask;
                started.TrySetResult();
                return release.Task;
            };
            vm.SelectedScope = ScopeView.Waveform;
            await started.Task.WaitAsync(TestWaits.Condition);
            vm.ToggleClippingOverlayCommand.Execute(null);
            await TestWaits.UntilAsync(() => vm.PreviewClippingMask != null);

            async Task<(object? Histogram, object? Waveform, object? RawHistogram,
                object? Clipping, object? Mask)> SnapshotAsync()
            {
                await renderSetup.WaitAsync();
                return (vm.Histogram, vm.EffectiveWaveform, vm.RawHistogram,
                    vm.DisplayClippingStats, vm.PreviewClippingMask);
            }

            var snapshotTask = SnapshotAsync();
            // Settle before release: the original non-null wait can finish here,
            // because the initial Develop paint already supplied a waveform.
            Drain();
            await Task.Yield();
            release.TrySetResult();
            var snapshot = await snapshotTask.WaitAsync(TestWaits.Condition);
            await TestWaits.UntilAsync(() =>
                !ReferenceEquals(initialHistogram, vm.Histogram));
            Drain();

            Assert.Equal(2, Volatile.Read(ref requests));
            Assert.Same(snapshot.Histogram, vm.Histogram);
            Assert.Same(snapshot.Waveform, vm.EffectiveWaveform);
            Assert.Same(snapshot.RawHistogram, vm.RawHistogram);
            Assert.Same(snapshot.Clipping, vm.DisplayClippingStats);
            Assert.Same(snapshot.Mask, vm.PreviewClippingMask);
        }
        finally
        {
            release.TrySetResult();
            vm.ImageService.Previews.RenderGateAsync = null;
        }
    }

    private sealed class RenderSetup : IDisposable
    {
        private readonly MainWindowViewModel _vm;
        private readonly HistogramData? _initialHistogram;
        private readonly long _generation;
        private int _completed;

        public RenderSetup(MainWindowViewModel vm)
        {
            _vm = vm;
            _initialHistogram = vm.Histogram;
            _generation = vm.LatestPreviewOutcomeGeneration + 1;
            vm.ImageService.Previews.RenderRequestCompleted += OnCompleted;
        }

        private void OnCompleted(long generation)
        {
            if (generation == _generation)
                Interlocked.Increment(ref _completed);
        }

        public async Task WaitAsync()
        {
            Assert.Equal(_generation, _vm.LatestPreviewOutcomeGeneration);
            // Scope and clipping share a generation but are separate requests.
            await TestWaits.UntilAsync(() => Volatile.Read(ref _completed) == 2);
            // Service completion precedes caller publication. Observe both outcomes
            // on the UI thread; the initial waveform alone proves nothing.
            await TestWaits.UntilAsync(() =>
                _vm.Histogram != null &&
                !ReferenceEquals(_initialHistogram, _vm.Histogram) &&
                _vm.EffectiveWaveform != null && _vm.PreviewClippingMask != null);
            Drain();
        }

        public void Dispose() =>
            _vm.ImageService.Previews.RenderRequestCompleted -= OnCompleted;
    }

    [AvaloniaFact]
    public async Task SmallerSameSettingsRequestDoesNotSupersedeSourceCappedRefinement()
    {
        using var catalog = await _fx.CreateCatalogAsync("source-capped-refinement");
        await using var vm = _fx.CreateViewModel(
            catalog,
            new GrayLoader(),
            _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        vm.SelectedImage = new ImageFile(_fx.Path("source-capped-refinement.jpg"));
        await TestWaits.UntilAsync(() => vm.PreviewImage != null);

        await vm.ToggleBeforeAfterSplitCommand.ExecuteAsync(null);
        await TestWaits.UntilAsync(() => vm.BeforeAfterPreviewImage != null);
        vm.PublishBeforeAfterRequiredDeviceLongEdge(3200);
        await TestWaits.UntilAsync(() =>
            vm.BeforeAfterPreviewImage is { } bitmap &&
            Math.Max(bitmap.PixelSize.Width, bitmap.PixelSize.Height) == 1280);
        var refined = vm.BeforeAfterPreviewImage;
        var renderSerial = SideSurfaceRenderSerial(vm);

        vm.PublishBeforeAfterRequiredDeviceLongEdge(1600);

        Assert.Equal(renderSerial, SideSurfaceRenderSerial(vm));
        Assert.Same(refined, vm.BeforeAfterPreviewImage);
    }

    private static long SideSurfaceRenderSerial(MainWindowViewModel vm) =>
        (long)typeof(PreviewService).GetField(
            "_sideSurfaceSerial",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!
            .GetValue(vm.ImageService.Previews)!;

    private static bool BeforeAfterRenderIsIdle(MainWindowViewModel vm) =>
        typeof(MainWindowViewModel).GetField(
            "_beforeAfterRenderCts",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!.GetValue(vm) == null;
}
