using System.Collections.Concurrent;
using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class AdjacentPreviewPerformanceTests
{
    private const long MiB = 1024 * 1024;
    private readonly AvaloniaTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public AdjacentPreviewPerformanceTests(
        AvaloniaTestFixture fixture,
        ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [WindowsFact]
    public async Task AdjacentSelectionGates_WhenEnabled()
    {
        _fixture.RequireWindows();
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to run adjacent-preview performance diagnostics.");

        var jpegFailure = await Record.ExceptionAsync(() => MeasureFixtureAsync(
            "JPEG",
            GoldenTestPaths.Asset("display-p3-reference.jpg"),
            peakBudgetMiB: 95,
            settledBudgetMiB: 20));
        // RAW warms plateau higher: LibRaw and Magick buffers freed on the
        // short-lived warm workers stay with the allocator for reuse.
        var rawFailure = await Record.ExceptionAsync(() => MeasureFixtureAsync(
            "RAW",
            GoldenTestPaths.Asset("canon-eos-350d.cr2"),
            peakBudgetMiB: 300,
            settledBudgetMiB: 50));
        Assert.Null(jpegFailure);
        Assert.Null(rawFailure);
    }

    private async Task MeasureFixtureAsync(
        string label,
        string fixturePath,
        int peakBudgetMiB,
        int settledBudgetMiB)
    {
        var disabled = new List<AdjacentSample>();
        var enabled = new List<AdjacentSample>();
        for (var sample = 0; sample < 3; sample++)
        {
            disabled.Add(await MeasureAsync(fixturePath, prefetch: false));
            enabled.Add(await MeasureAsync(fixturePath, prefetch: true));
        }

        var disabledPaint = Median(disabled, value => value.FirstPaintMs);
        var enabledPaint = Median(enabled, value => value.FirstPaintMs);
        var disabledPriority = Median(disabled, value => value.PriorityPaintMs);
        var enabledPriority = Median(enabled, value => value.PriorityPaintMs);
        var disabledFreshMemory = Median(disabled, value => value.FreshDeltaBytes);
        var warmPeak = Median(enabled, value => value.WarmPeakDeltaBytes);
        var warmSettled = Median(enabled, value => value.WarmSettledDeltaBytes);

        _output.WriteLine(
            $"{label}: adjacent disabled={disabledPaint:F1} ms, " +
            $"warm={enabledPaint:F1} ms, active-warm third={enabledPriority:F1} ms " +
            $"(disabled {disabledPriority:F1} ms), peak={warmPeak / (double)MiB:F1} MiB, " +
            $"settled={warmSettled / (double)MiB:F1} MiB, " +
            $"disabled fresh={disabledFreshMemory / (double)MiB:F1} MiB");
        CullPerfMigratedEvidence.Write(label, disabled.ToArray(), enabled.ToArray());
        Assert.True(enabledPaint <= 100, $"{label} warm paint was {enabledPaint:F1} ms.");
        Assert.True(
            enabledPaint <= disabledPaint * 0.30,
            $"{label} warm paint was {enabledPaint / disabledPaint:P1} of disabled.");
        Assert.True(
            enabledPriority <= disabledPriority * 1.10,
            $"{label} active-warm foreground was " +
            $"{enabledPriority / disabledPriority:P1} of disabled.");
        Assert.True(warmPeak <= peakBudgetMiB * MiB,
            $"{label} warm peak was {warmPeak / (double)MiB:F1} MiB.");
        Assert.True(warmSettled <= settledBudgetMiB * MiB,
            $"{label} warm settled was {warmSettled / (double)MiB:F1} MiB.");
        Assert.All(enabled, sample =>
        {
            Assert.Equal(1, sample.RetainedPairCount);
            Assert.Equal(1, sample.ThirdDecodeCount);
            Assert.Equal(0, sample.FinalActivityCount);
        });
    }

    internal static async Task<AdjacentSample> MeasureAsync(
        string fixturePath,
        bool prefetch, bool recording = true, bool measureAtNotification = false)
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-adjacent-perf-{Guid.NewGuid():N}")).FullName;
        try
        {
            var extension = Path.GetExtension(fixturePath);
            // Eight copies: the walk-ahead warms five past the selection, so
            // the priority target and a fresh warm candidate must sit beyond it.
            var paths = Enumerable.Range(0, 8)
                .Select(index => Path.Combine(root, $"image-{index}{extension}"))
                .ToArray();
            if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                using var source = new MagickImage(fixturePath);
                // Calibrated to reproduce the approved 30.5 MiB disabled fresh-load delta.
                source.Resize(2050, 0);
                source.Quality = 90;
                source.Write(paths[0]);
                foreach (var path in paths.Skip(1)) File.Copy(paths[0], path);
            }
            else
            {
                foreach (var path in paths) File.Copy(fixturePath, path);
            }

            using var catalog = new CatalogService(Path.Combine(root, "catalog"));
            await catalog.InitializeAsync();
            var images = paths.Select(path => new ImageFile(path)).ToArray();
            foreach (var image in images) await image.EnsureCatalogIdAsync(catalog);
            var loader = new CountingLoader(new BaseLoaderRouter(
                new RawBaseLoader(),
                new StandardBaseLoader()));
            var clock = new TestTimeProvider();
            var vm = new MainWindowViewModel(
                catalog,
                loader,
                loadMetadataAsync: _ => Task.CompletedTask,
                availabilityService: new TestSourceAvailabilityService(
                    SourceAvailability.AvailableLocally),
                timeProvider: clock)
            {
                IsDevelopMode = true
            };
            var recorder = recording ? new CullPerfRecorder() : null;
            vm.ImageService.Previews.CullPerf = recorder;
            vm.Browse.SetImages(images);
            vm.ImageService.Previews.AdjacentWarmEnabled = prefetch;
            var warmStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            vm.ImageService.Previews.AdjacentWarmWorkStarted += () =>
                warmStarted.TrySetResult();
            var adjacentIdleDelay = TimeSpan.FromMilliseconds(75);

            try
            {
                vm.SelectedImage = images[0];
                await TestWaits.UntilAsync(() =>
                    vm.PreviewImage != null && vm.Histogram != null &&
                    vm.InitialPreviewActivityCount == 0 &&
                    !vm.RawProfilePickerState.IsLoading &&
                    vm.ImageService.Previews.PreviewActivityCount == 0);
                ForceCollection();
                var process = Process.GetCurrentProcess();
                process.Refresh();
                var beforeWarm = process.PrivateMemorySize64;
                clock.Advance(adjacentIdleDelay);
                long warmPeak = beforeWarm;
                if (prefetch)
                {
                    await warmStarted.Task.WaitAsync(TestWaits.Condition);
                    var warmDeadline = DateTime.UtcNow + TestWaits.Condition;
                    do
                    {
                        process.Refresh();
                        warmPeak = Math.Max(warmPeak, process.PrivateMemorySize64);
                        Assert.True(
                            DateTime.UtcNow < warmDeadline,
                            "Adjacent preview warm work did not settle.");
                        await Task.Delay(
                            10,
                            TestContext.Current.CancellationToken);
                    }
                    // Activity dips to zero between the walk's workers, so
                    // keep sampling until every neighbor in reach has decoded.
                    while (vm.ImageService.Previews.PreviewActivityCount > 0 ||
                           Enumerable.Range(1, 5).Any(
                               offset => loader.Count(paths[offset]) == 0));
                    await TestWaits.UntilAsync(() =>
                        vm.ImageService.Previews.PreviewActivityCount == 0 &&
                        vm.ImageService.Previews.PendingCacheWrites == 0 &&
                        vm.ImageService.Previews.AdjacentWarmEntryCount == 0);
                }
                ForceCollection();
                process.Refresh();
                var warmSettled = process.PrivateMemorySize64;

                var firstPaint = await CullPerfPublication.MeasureAsync(vm, images[1], recorder, measureAtNotification);
                process.Refresh();
                var freshDelta = Math.Max(
                    0,
                    process.PrivateMemorySize64 - beforeWarm);

                await TestWaits.UntilAsync(() =>
                    vm.InitialPreviewActivityCount == 0 &&
                    !vm.RawProfilePickerState.IsLoading &&
                    vm.ImageService.Previews.PreviewActivityCount == 0);
                warmStarted = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                vm.ImageService.Previews.AdjacentWarmWorkStarted += () =>
                    warmStarted.TrySetResult();
                clock.Advance(adjacentIdleDelay);
                if (prefetch)
                {
                    await warmStarted.Task.WaitAsync(TestWaits.Condition);
                }
                var priorityPaint = await CullPerfPublication.MeasureAsync(vm, images[7], recorder, measureAtNotification);
                vm.ImageService.Previews.InvalidateAdjacentWarm();
                await TestWaits.UntilAsync(() =>
                    vm.ImageService.Previews.PreviewActivityCount == 0);

                return new AdjacentSample(
                    firstPaint.Milliseconds,
                    priorityPaint.Milliseconds,
                    freshDelta,
                    Math.Max(0, warmPeak - beforeWarm),
                    Math.Max(0, warmSettled - beforeWarm),
                    vm.ImageService.Previews.RetainedBasePairCount,
                    loader.Count(paths[7]),
                    vm.ImageService.Previews.PreviewActivityCount,
                    firstPaint.BracketMilliseconds, priorityPaint.BracketMilliseconds,
                    recorder?.LostEvents ?? 0);
            }
            finally
            {
                await vm.DisposeAsync();
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static double Median(
        IEnumerable<AdjacentSample> samples,
        Func<AdjacentSample, double> selector) =>
        samples.Select(selector).Order().ElementAt(1);

    private static long Median(
        IEnumerable<AdjacentSample> samples,
        Func<AdjacentSample, long> selector) =>
        samples.Select(selector).Order().ElementAt(1);

    // Aggressive mode compacts the large-object heap and decommits freed
    // regions, so a settled sample reads live memory, not lingering segments.
    private static void ForceCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
    }

    internal sealed record AdjacentSample(
        double FirstPaintMs,
        double PriorityPaintMs,
        long FreshDeltaBytes,
        long WarmPeakDeltaBytes,
        long WarmSettledDeltaBytes,
        int RetainedPairCount,
        int ThirdDecodeCount,
        int FinalActivityCount,
        double FirstPollingBracketMs,
        double PriorityPollingBracketMs,
        long LostEvents);

    private sealed class CountingLoader(IBaseImageLoader inner) : IBaseImageLoader
    {
        private readonly ConcurrentDictionary<string, int> _counts =
            new(StringComparer.OrdinalIgnoreCase);

        public int Count(string path) => _counts.GetValueOrDefault(path);
        public bool CanLoad(ImageFile file) => inner.CanLoad(file);

        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            _counts.AddOrUpdate(file.FilePath, 1, (_, count) => count + 1);
            return inner.LoadPreviewBaseWithOutcome(file, decode, cancellationToken);
        }

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            inner.LoadFullBase(file, decode, cancellationToken);
    }
}
