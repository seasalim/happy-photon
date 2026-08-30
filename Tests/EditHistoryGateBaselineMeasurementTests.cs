using System.Diagnostics;
using System.Reflection;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class EditHistoryGateBaselineMeasurementTests
{
    private const int DefaultRunCount = 5;
    private readonly AvaloniaTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public EditHistoryGateBaselineMeasurementTests(
        AvaloniaTestFixture fixture,
        ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [WindowsFact]
    public async Task Gate3_PerEditCommitBaseline_WhenEnabled()
    {
        _fixture.RequireWindows();
        if (!IsEnabled()) return;

        var runCount = GetRunCount();
        var runs = new List<CommitRun>(runCount);
        for (var run = 1; run <= runCount; run++)
        {
            var sample = await MeasureCommitRunAsync(run);
            runs.Add(sample);
            _output.WriteLine(
                $"Gate 3 run {run}: overall={sample.OverallMedianMs:F3} ms, " +
                $"commits 1-10={sample.FirstTenMedianMs:F3} ms, " +
                $"commits 91-100={sample.LastTenMedianMs:F3} ms");
        }

        var overall = Median(runs.Select(run => run.OverallMedianMs));
        var firstTen = Median(runs.Select(run => run.FirstTenMedianMs));
        var lastTen = Median(runs.Select(run => run.LastTenMedianMs));
        _output.WriteLine(
            $"Gate 3 median of {runCount} runs: " +
            $"overall={overall:F3} ms, " +
            $"commits 1-10={firstTen:F3} ms, " +
            $"commits 91-100={lastTen:F3} ms");
        Assert.True(overall <= 9.2,
            $"Gate 3 overall median {overall:F3} ms exceeded 9.2 ms.");
        Assert.True(lastTen <= firstTen + 5,
            $"Gate 3 grew from {firstTen:F3} ms to {lastTen:F3} ms.");
    }

    [WindowsFact]
    public async Task Gate4_DevelopSubjectHistoryLoad_WhenEnabled()
    {
        _fixture.RequireWindows();
        if (!IsEnabled()) return;

        var runCount = GetRunCount();
        var runs = new List<SelectionRun>(runCount);
        for (var run = 1; run <= runCount; run++)
        {
            var sample = await MeasureSelectionRunAsync(run);
            runs.Add(sample);
            _output.WriteLine(
                $"Gate 4 run {run}: A (future 0 rows)={sample.ImageAMs:F3} ms, " +
                $"B (future 50 rows)={sample.ImageBMs:F3} ms");
        }

        var imageA = Median(runs.Select(run => run.ImageAMs));
        var imageB = Median(runs.Select(run => run.ImageBMs));
        _output.WriteLine(
            $"Gate 4 median of {runCount} runs: " +
            $"A (future 0 rows)={imageA:F3} ms, " +
            $"B (future 50 rows)={imageB:F3} ms");
        Assert.True(imageA <= 11.1,
            $"Gate 4 zero-row load {imageA:F3} ms exceeded 11.1 ms.");
        Assert.True(imageB <= 11.1,
            $"Gate 4 50-row load {imageB:F3} ms exceeded 11.1 ms.");
    }

    private static async Task<CommitRun> MeasureCommitRunAsync(int run)
    {
        using var fixture = new CatalogVmFixture($"edit-history-gate3-{run}");
        using var catalog = await fixture.CreateCatalogAsync();
        var image = await CreateImageAsync(fixture, catalog, "commit.jpg");
        var vm = fixture.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.ImageService.Previews.AdjacentWarmEnabled = false;
        vm.Browse.SetImages([image]);
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;

        try
        {
            await TestWaits.UntilAsync(() =>
                vm.PreviewImage != null &&
                vm.ImageService.Previews.PreviewActivityCount == 0);

            var samples = new double[100];
            for (var commit = 1; commit <= samples.Length; commit++)
            {
                image.EditSettings.Exposure = commit / 100d;
                var stopwatch = Stopwatch.StartNew();
                await SaveEditSettings(vm, image);
                stopwatch.Stop();
                samples[commit - 1] = stopwatch.Elapsed.TotalMilliseconds;
            }

            return new CommitRun(
                Median(samples),
                Median(samples.Take(10)),
                Median(samples.Skip(90)));
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    private static async Task<SelectionRun> MeasureSelectionRunAsync(int run)
    {
        using var fixture = new CatalogVmFixture($"edit-history-gate4-{run}");
        using var catalog = await fixture.CreateCatalogAsync();
        var imageA = await CreateImageAsync(fixture, catalog, "a.jpg");
        var imageB = await CreateImageAsync(fixture, catalog, "b.jpg");
        imageA.EditSettings.Exposure = 0.25;
        imageA.EditSettings.Highlights = 11;
        imageA.EditSettings.HorizonRotation = 0.1;
        imageB.EditSettings.Exposure = 0.5;
        imageB.EditSettings.Highlights = 22;
        imageB.EditSettings.HorizonRotation = 0.2;
        var entries = Enumerable.Range(0, 50).Select(index =>
            new CatalogEditHistoryEntry(
                index,
                index == 0 ? "Original" : $"Exposure {index}",
                new EditSettings { Exposure = index / 100d }))
            .ToArray();
        await catalog.SaveEditSettingsWithHistoryAsync(
            imageB.CatalogId,
            imageB.EditSettings,
            new CatalogEditHistoryMutation(-1, entries, 49));

        var vm = fixture.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.ImageService.Previews.AdjacentWarmEnabled = false;
        vm.Browse.SetImages([imageA, imageB]);
        vm.IsDevelopMode = true;

        try
        {
            var imageAMs = await MeasureSelectionToHistoryLoadedAsync(vm, imageA);
            var imageBMs = await MeasureSelectionToHistoryLoadedAsync(vm, imageB);
            return new SelectionRun(imageAMs, imageBMs);
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    private static async Task<double> MeasureSelectionToHistoryLoadedAsync(
        MainWindowViewModel vm,
        ImageFile image)
    {
        var loaded = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        System.ComponentModel.PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.IsHistoryLoaded) &&
                vm.IsHistoryLoaded)
                loaded.TrySetResult();
        };
        vm.PropertyChanged += handler;
        var stopwatch = Stopwatch.StartNew();
        vm.SelectedImage = image;
        if (!vm.IsHistoryLoaded)
            await loaded.Task.WaitAsync(TestWaits.Condition);
        stopwatch.Stop();
        vm.PropertyChanged -= handler;

        Assert.Same(image, vm.SelectedImage);
        Assert.Equal(image.EditSettings.Exposure, vm.Exposure);
        Assert.Equal(image.EditSettings.Highlights, vm.Highlights);
        Assert.Equal(image.EditSettings.HorizonRotation, vm.HorizonRotation);
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private static async Task<ImageFile> CreateImageAsync(
        CatalogVmFixture fixture,
        CatalogService catalog,
        string name)
    {
        var path = fixture.Path(name);
        File.Copy(GoldenTestPaths.Asset("display-p3-reference.jpg"), path);
        var image = new ImageFile(path);
        await image.EnsureCatalogIdAsync(catalog);
        return image;
    }

    private static readonly Func<MainWindowViewModel, ImageFile, Task>
        SaveEditSettings = CreateSaveDelegate();

    private static Func<MainWindowViewModel, ImageFile, Task> CreateSaveDelegate()
    {
        var method = typeof(MainWindowViewModel).GetMethod(
            "SaveEditSettingsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(ImageFile)],
            modifiers: null) ?? throw new MissingMethodException(
                typeof(MainWindowViewModel).FullName,
                "SaveEditSettingsAsync(ImageFile)");
        return method.CreateDelegate<Func<MainWindowViewModel, ImageFile, Task>>();
    }

    private static bool IsEnabled() =>
        Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") == "1";

    private static int GetRunCount() =>
        int.TryParse(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF_RUNS"),
            out var value) && value is >= 1 and <= DefaultRunCount
            ? value
            : DefaultRunCount;

    private static double Median(IEnumerable<double> source)
    {
        var values = source.Order().ToArray();
        var middle = values.Length / 2;
        return values.Length % 2 == 0
            ? (values[middle - 1] + values[middle]) / 2
            : values[middle];
    }

    private sealed record CommitRun(
        double OverallMedianMs,
        double FirstTenMedianMs,
        double LastTenMedianMs);

    private sealed record SelectionRun(double ImageAMs, double ImageBMs);
}
