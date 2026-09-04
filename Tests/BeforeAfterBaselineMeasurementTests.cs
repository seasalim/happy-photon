using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed partial class BeforeAfterBaselineMeasurementTests
{
    private const int RunCount = 5;
    private static readonly TimeSpan EditDebounce = TimeSpan.FromMilliseconds(150);
    private readonly AvaloniaTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public BeforeAfterBaselineMeasurementTests(
        AvaloniaTestFixture fixture,
        ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [WindowsFact]
    public async Task BeforeAfterDecodeAndLatencyBaselines_WhenEnabled()
    {
        _fixture.RequireWindows();
        if (Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1")
        {
            return;
        }

        var raw = await MeasureFixtureAsync(
            "RAW",
            GoldenTestPaths.Asset("canon-eos-350d.cr2"),
            measureHighlightReset: true);
        var jpeg = await MeasureFixtureAsync(
            "JPEG",
            GoldenTestPaths.Asset("display-p3-reference.jpg"),
            measureHighlightReset: false);

        WriteSummary("RAW", raw);
        WriteSummary("JPEG", jpeg);
    }

    private async Task<List<FixtureSample>> MeasureFixtureAsync(
        string label,
        string fixturePath,
        bool measureHighlightReset)
    {
        var samples = new List<FixtureSample>();
        for (var run = 1; run <= RunCount; run++)
        {
            var sample = await MeasureRunAsync(fixturePath, measureHighlightReset);
            samples.Add(sample);
            _output.WriteLine(
                $"{label} run {run}: B={Format(sample.DefaultToggle)}, " +
                $"parent={Format(sample.ParentBefore)}->{Format(sample.ParentAfter)}, " +
                $"B-after-Blend={Format(sample.HighlightToggle)}, " +
                $"tone={Format(sample.ToneEdit)}, next={Format(sample.Selection)}, " +
                $"Y={Format(sample.SplitEntry)}, " +
                $"Y-parent={Format(sample.SplitParentBefore)}->{Format(sample.SplitParentAfter)}, " +
                $"split-tone={Format(sample.SplitToneEdit)}, " +
                $"split-loupe={Format(sample.SplitLoupe)}, " +
                $"split-next={Format(sample.SplitSelection)}");
        }
        return samples;
    }

    private static async Task<FixtureSample> MeasureRunAsync(
        string fixturePath,
        bool measureHighlightReset)
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-before-after-baseline-{Guid.NewGuid():N}")).FullName;
        try
        {
            var extension = Path.GetExtension(fixturePath);
            var firstPath = Path.Combine(root, $"first{extension}");
            var secondPath = Path.Combine(root, $"second{extension}");
            var thirdPath = Path.Combine(root, $"third{extension}");
            File.Copy(fixturePath, firstPath);
            File.Copy(fixturePath, secondPath);
            File.Copy(fixturePath, thirdPath);

            using var catalog = new CatalogService(Path.Combine(root, "catalog"));
            await catalog.InitializeAsync();
            var first = new ImageFile(firstPath)
            {
                EditSettings = new EditSettings { Exposure = 0.25 }
            };
            var second = new ImageFile(secondPath)
            {
                EditSettings = new EditSettings { Exposure = 0.25 }
            };
            var third = new ImageFile(thirdPath)
            {
                EditSettings = new EditSettings { Exposure = 0.25 }
            };
            await first.EnsureCatalogIdAsync(catalog);
            await second.EnsureCatalogIdAsync(catalog);
            await third.EnsureCatalogIdAsync(catalog);

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
            vm.ImageService.Previews.AdjacentWarmEnabled = false;
            vm.Browse.SetImages([first, second, third]);

            try
            {
                vm.SelectedImage = first;
                await WaitForSettledPaintAsync(vm);

                var parentBefore = RestingParentGeneration(vm);
                var defaultToggle = await MeasureToggleAsync(vm, loader);
                var parentAfter = RestingParentGeneration(vm);
                await ToggleBackToEditedAsync(vm);

                OperationSample? highlightToggle = null;
                if (measureHighlightReset)
                {
                    await ApplyEditAsync(
                        vm,
                        clock,
                        () => vm.HlReconstruction = HlReconstructionMode.Blend);
                    highlightToggle = await MeasureToggleAsync(vm, loader);
                    await ToggleBackToEditedAsync(vm);
                }

                var toneEdit = await MeasureEditAsync(
                    vm,
                    loader,
                    clock,
                    () => vm.Exposure = 0.5);
                var selection = await MeasureSelectionAsync(vm, loader, second);
                var splitParentBefore = RestingParentGeneration(vm);
                var splitEntry = await MeasureSplitEntryAsync(vm, loader);
                var splitParentAfter = RestingParentGeneration(vm);
                var splitToneEdit = await MeasureSplitToneEditAsync(
                    vm,
                    loader,
                    clock,
                    () => vm.Exposure = 0.75);
                var splitLoupe = await MeasureSplitLoupeAsync(vm, loader);
                var splitSelection = await MeasureSplitSelectionAsync(
                    vm,
                    loader,
                    third);

                return new FixtureSample(
                    defaultToggle,
                    highlightToggle,
                    toneEdit,
                    selection,
                    parentBefore,
                    parentAfter,
                    splitEntry,
                    splitToneEdit,
                    splitLoupe,
                    splitSelection,
                    splitParentBefore,
                    splitParentAfter);
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

    private static async Task<OperationSample> MeasureToggleAsync(
        MainWindowViewModel vm,
        CountingLoader loader)
    {
        var previous = vm.PreviewImage;
        var observer = new OperationObserver(vm, loader);
        var stopwatch = Stopwatch.StartNew();
        var operation = vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
        await observer.ObserveUntilAsync(() => operation.IsCompleted);
        await operation;
        Assert.True(vm.IsShowingOriginal);
        Assert.NotSame(previous, vm.PreviewImage);
        stopwatch.Stop();
        await observer.ObserveUntilAsync(() => IsSettled(vm));
        return observer.Complete(stopwatch.Elapsed.TotalMilliseconds);
    }

    private static async Task ToggleBackToEditedAsync(MainWindowViewModel vm)
    {
        var previous = vm.PreviewImage;
        await vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
        Assert.False(vm.IsShowingOriginal);
        Assert.NotSame(previous, vm.PreviewImage);
        await TestWaits.UntilAsync(() => IsSettled(vm));
    }

    private static async Task<OperationSample> MeasureSplitEntryAsync(
        MainWindowViewModel vm,
        CountingLoader loader)
    {
        Assert.Null(vm.BeforeAfterPreviewImage);
        var observer = new OperationObserver(vm, loader);
        var stopwatch = Stopwatch.StartNew();
        var operation = vm.ToggleBeforeAfterSplitCommand.ExecuteAsync(null);
        await observer.ObserveUntilAsync(() => vm.BeforeAfterPreviewImage != null);
        stopwatch.Stop();
        await operation;
        Assert.True(vm.IsBeforeAfterSplit);
        await observer.ObserveUntilAsync(() => IsSettled(vm));
        return observer.Complete(stopwatch.Elapsed.TotalMilliseconds);
    }

    private static async Task ApplyEditAsync(
        MainWindowViewModel vm,
        TestTimeProvider clock,
        Action edit)
    {
        var previous = vm.PreviewImage;
        edit();
        clock.Advance(EditDebounce);
        await TestWaits.UntilAsync(() =>
            !ReferenceEquals(previous, vm.PreviewImage) && IsSettled(vm));
    }

    private static async Task<OperationSample> MeasureEditAsync(
        MainWindowViewModel vm,
        CountingLoader loader,
        TestTimeProvider clock,
        Action edit)
    {
        var previous = vm.PreviewImage;
        var observer = new OperationObserver(vm, loader);
        edit();
        clock.Advance(EditDebounce);
        await observer.ObserveUntilAsync(() =>
            !ReferenceEquals(previous, vm.PreviewImage) && IsSettled(vm));
        return observer.Complete();
    }

    private static async Task<OperationSample> MeasureSplitToneEditAsync(
        MainWindowViewModel vm,
        CountingLoader loader,
        TestTimeProvider clock,
        Action edit)
    {
        var before = vm.BeforeAfterPreviewImage;
        var sample = await MeasureEditAsync(vm, loader, clock, edit);
        Assert.Same(before, vm.BeforeAfterPreviewImage);
        return sample;
    }

    private static async Task<OperationSample> MeasureSplitLoupeAsync(
        MainWindowViewModel vm,
        CountingLoader loader)
    {
        var before = vm.BeforeAfterPreviewImage;
        var observer = new OperationObserver(vm, loader);
        vm.PublishBeforeAfterRequiredDeviceLongEdge(
            BaseImage.LargePreviewMaxDimension);
        await observer.ObserveUntilAsync(() =>
            vm.BeforeAfterPreviewImage != null &&
            !ReferenceEquals(before, vm.BeforeAfterPreviewImage));
        return observer.Complete();
    }

    private static async Task<OperationSample> MeasureSelectionAsync(
        MainWindowViewModel vm,
        CountingLoader loader,
        ImageFile next)
    {
        var observer = new OperationObserver(vm, loader);
        vm.SelectedImage = next;
        await observer.ObserveUntilAsync(() =>
            ReferenceEquals(vm.SelectedImage, next) &&
            vm.PreviewImage != null &&
            IsSettled(vm));
        return observer.Complete();
    }

    private static async Task<OperationSample> MeasureSplitSelectionAsync(
        MainWindowViewModel vm,
        CountingLoader loader,
        ImageFile next)
    {
        var before = vm.BeforeAfterPreviewImage;
        var observer = new OperationObserver(vm, loader);
        vm.SelectedImage = next;
        await observer.ObserveUntilAsync(() =>
            ReferenceEquals(vm.SelectedImage, next) &&
            vm.PreviewImage != null &&
            vm.BeforeAfterPreviewImage != null &&
            !ReferenceEquals(before, vm.BeforeAfterPreviewImage) &&
            IsSettled(vm));
        return observer.Complete();
    }

    private static async Task WaitForSettledPaintAsync(MainWindowViewModel vm) =>
        await TestWaits.UntilAsync(() =>
            vm.PreviewImage != null &&
            vm.Histogram != null &&
            !vm.RawProfilePickerState.IsLoading &&
            vm.InitialPreviewActivityCount == 0 &&
            IsSettled(vm));

    private static bool IsSettled(MainWindowViewModel vm) =>
        vm.ImageService.Previews.PreviewActivityCount == 0 &&
        vm.ImageService.Previews.RenderedThumbnailTaskCount == 0 &&
        vm.ImageService.Previews.PendingCacheWrites == 0;

    private static int DecodeTaskCount(MainWindowViewModel vm)
    {
        var field = typeof(PreviewService).GetField(
            "_baseCoordinator",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!;
        return ((PreviewBaseCoordinator)field.GetValue(
            vm.ImageService.Previews)!).DecodeTaskCount;
    }

    private static long? RestingParentGeneration(MainWindowViewModel vm)
    {
        var field = typeof(MainWindowViewModel).GetField(
            "_restingParent",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!;
        return ((PreviewRenderIdentity?)field.GetValue(vm))?.Generation;
    }

    private void WriteSummary(string label, IReadOnlyList<FixtureSample> samples)
    {
        _output.WriteLine(
            $"{label} B latency median of {RunCount}: " +
            $"{Median(samples.Select(value => value.DefaultToggle.ElapsedMs)):F3} ms; " +
            $"samples=[{string.Join(", ", samples.Select(value =>
                value.DefaultToggle.ElapsedMs.ToString("F3")))}]");
        WriteOperationSummary(label, "B default", samples.Select(value => value.DefaultToggle));
        if (samples.All(value => value.HighlightToggle != null))
        {
            WriteOperationSummary(
                label,
                "B after Blend",
                samples.Select(value => value.HighlightToggle!));
        }
        WriteOperationSummary(label, "tone edit", samples.Select(value => value.ToneEdit));
        WriteOperationSummary(label, "next selection", samples.Select(value => value.Selection));
        _output.WriteLine(
            $"{label} Y latency median of {RunCount}: " +
            $"{Median(samples.Select(value => value.SplitEntry.ElapsedMs)):F3} ms; " +
            $"samples=[{string.Join(", ", samples.Select(value =>
                value.SplitEntry.ElapsedMs.ToString("F3")))}]");
        WriteOperationSummary(label, "Y entry", samples.Select(value => value.SplitEntry));
        WriteOperationSummary(label, "split tone edit", samples.Select(value => value.SplitToneEdit));
        WriteOperationSummary(label, "split loupe", samples.Select(value => value.SplitLoupe));
        WriteOperationSummary(label, "split next selection", samples.Select(value => value.SplitSelection));
        _output.WriteLine(
            $"{label} resting parent generations B default: [" +
            string.Join(", ", samples.Select(value =>
                $"{Format(value.ParentBefore)}->{Format(value.ParentAfter)}")) + "]");
        _output.WriteLine(
            $"{label} resting parent generations Y entry: [" +
            string.Join(", ", samples.Select(value =>
                $"{Format(value.SplitParentBefore)}->{Format(value.SplitParentAfter)}")) + "]");
    }

    private void WriteOperationSummary(
        string label,
        string operation,
        IEnumerable<OperationSample> source)
    {
        var samples = source.ToArray();
        _output.WriteLine(
            $"{label} {operation}, {RunCount} runs: decode deltas=[" +
            string.Join(", ", samples.Select(value => value.DecodeDelta)) +
            $"], median={Median(samples.Select(value => value.DecodeDelta))}; " +
            "peak DecodeTaskCount=[" +
            string.Join(", ", samples.Select(value => value.PeakDecodeTasks)) +
            "]; peak PreviewActivityCount=[" +
            string.Join(", ", samples.Select(value => value.PeakPreviewActivity)) +
            "]; final RetainedBasePairCount=[" +
            string.Join(", ", samples.Select(value => value.RetainedPairs)) + "]");
    }

    private static string Format(OperationSample? sample) => sample == null
        ? "n/a"
        : $"decodes={sample.DecodeDelta},decodePeak={sample.PeakDecodeTasks}," +
          $"activityPeak={sample.PeakPreviewActivity},retained={sample.RetainedPairs}," +
          $"elapsed={sample.ElapsedMs:F3}ms";

    private static string Format(long? generation) =>
        generation?.ToString() ?? "none";

    private static double Median(IEnumerable<double> values) =>
        values.Order().ElementAt(RunCount / 2);

    private static int Median(IEnumerable<int> values) =>
        values.Order().ElementAt(RunCount / 2);

}
