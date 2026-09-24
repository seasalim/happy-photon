using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class LoadingLabelDwellTests
{
    [WindowsTheory]
    [InlineData("loupe")]
    [InlineData("develop")]
    public async Task Measure(string surface)
    {
        var results = Environment.GetEnvironmentVariable("LOADING_LABEL_RESULTS");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(results), "Set LOADING_LABEL_RESULTS to opt in.");
        var gates = CullPerfFiles.Read<CullPerfGateFile>(
            Path.Combine(CullPerfFiles.Root, "Tests", "CullPerfGates.dwell.json"));
        var frozen = gates.Workloads.Single(w => w.Surface == surface && w.Pattern == "dwell");
        var countText = Environment.GetEnvironmentVariable("LOADING_LABEL_STEPS");
        var count = countText == null ? frozen.Actions : int.Parse(countText);
        Assert.InRange(count, 1, frozen.Actions);
        var property = Environment.GetEnvironmentVariable($"LOADING_LABEL_{surface.ToUpperInvariant()}_PROPERTY") ??
            (surface == "loupe" ? "ShowLoadingMessage" : "ShowDevelopLoadingMessage");
        var fixtures = CullPerfFiles.Fixtures();
        Assert.Equal(gates.FixtureHashes.OrderBy(p => p.Key), fixtures.Hashes.OrderBy(p => p.Key));
        await CullPerfFiles.PrepareCatalogAsync(fixtures.Root);
        Directory.CreateDirectory(results!);
        using var observer = new LoadingLabelObserver(surface, property);
        // A new catalog/cache for every invocation; retain the original harness's full setup and cadence.
        using var diagnostics = new LoadingLabelDiagnostics(results!, surface);
        var evidence = await CullPerfWorkloadTests.RunWorkloadAsync(gates, frozen with { Actions = count }, Path.Combine(results!, surface), observer);
        var measurement = observer.Snapshot();
        var control = await PositiveControlAsync(surface, property);
        CullPerfFiles.WriteNew(Path.Combine(results!, $"loading-label-{surface}.json"), new
        {
            workload = frozen.Id, stepCount = count, frozenStepCount = frozen.Actions,
            smokeTest = count != frozen.Actions, intervalMs = frozen.IntervalMs,
            fixtureHash = fixtures.Hashes[frozen.Fixture], cacheCondition = frozen.CacheCondition,
            warming = true, measurement, positiveControl = new { passed = true, expectedActivations = 1, measurement = control },
            dwellEvidence = evidence.Fragment
        });
        // Fresh-render cadence is a separate foreground qualification. Preserve its failures
        // in the report; gate 4 measures every label interval even when fresh rendering is late.
        Assert.DoesNotContain(evidence.WorkloadFailures, f => !f.StartsWith("Dwell input "));
        Assert.Equal(0, evidence.Fragment.LostEvents);
        Assert.Equal(count, measurement.Steps.Length);
        Assert.Equal(0, measurement.IncompleteIntervals);
        Assert.Equal(0, measurement.UncorrelatedActivations);
    }

    private static async Task<LoadingLabelObserver.Report> PositiveControlAsync(string surface, string property)
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var image = new ImageFile(Path.Combine(directory.Path, "held.jpg"));
        TestImages.WriteJpeg(image.FilePath);
        await image.EnsureCatalogIdAsync(catalog);
        using var loader = new SignalHeldLoader();
        var vm = new MainWindowViewModel(catalog, loader, loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        using var observer = new LoadingLabelObserver(surface, property);
        try
        {
            vm.ImageService.Previews.AdjacentWarmEnabled = false;
            vm.Browse.SetImages([image]);
            vm.IsDevelopMode = surface == "develop";
            observer.Attach(vm);
            observer.BeginStep(1);
            vm.SelectedImage = image;
            if (surface == "loupe") vm.EnterLoupeCommand.Execute(null);
            await loader.Entered.Task.WaitAsync(TestWaits.Condition);
            // The timer supplies the workload's held-decode stimulus. Assertions wait on observed
            // activation, not a sleep. Use the real clock/continuation path, exactly as in the walk.
            var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var timer = TimeProvider.System.CreateTimer(_ => held.TrySetResult(), null,
                TimeSpan.FromMilliseconds(350), Timeout.InfiniteTimeSpan);
            await held.Task.WaitAsync(TestWaits.Condition);
            await TestWaits.UntilAsync(() => observer.ActivationCount > 0);
            Assert.True(observer.HasOpenInterval);
            loader.Release.Set();
            await TestWaits.UntilAsync(() => !observer.HasOpenInterval &&
                vm.InitialPreviewActivityCount == 0 && vm.LoupeLoadingTask.IsCompleted);
            observer.Dispose();
            var report = observer.Snapshot();
            var interval = Assert.Single(report.Intervals);
            Assert.Equal("condition-false", interval.EndReason);
            Assert.True(interval.DurationMs >= 320);
            Assert.Single(report.Activations);
            Assert.Equal(interval.Id, report.Activations[0].IntervalId);
            return report;
        }
        finally
        {
            loader.Release.Set();
            await vm.DisposeAsync();
        }
    }

    private sealed class SignalHeldLoader : IBaseImageLoader, IDisposable
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new();
        private readonly StandardBaseLoader _inner = new();
        public bool CanLoad(ImageFile file) => _inner.CanLoad(file);
        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(ImageFile file, BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            if (!Release.Wait(TestWaits.Condition, cancellationToken)) throw new TimeoutException("Held decode was not released.");
            return _inner.LoadPreviewBaseWithOutcome(file, decode, cancellationToken);
        }
        public BaseImage? LoadFullBase(ImageFile file, BaseDecodeSettings decode, CancellationToken cancellationToken) =>
            _inner.LoadFullBase(file, decode, cancellationToken);
        public void Dispose() => Release.Dispose();
    }
}
