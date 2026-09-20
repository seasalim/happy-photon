using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class CullPerfWorkloadTests
{
    [WindowsFact]
    public async Task PrepareFixtures()
    {
        RequireRunner();
        var fixtures = CullPerfFiles.Fixtures();
        CullPerfFiles.WriteNew(Path.Combine(CullPerfFiles.Run, "fixtures.json"),
            new { fixtures.Root, fixtures.Hashes });
        var expected = CullPerfFiles.Read<CullPerfGateFile>(CullPerfFiles.GatePath).FixtureHashes;
        Assert.Equal(expected.OrderBy(pair => pair.Key), fixtures.Hashes.OrderBy(pair => pair.Key));
        await CullPerfFiles.PrepareCatalogAsync(fixtures.Root);
    }

    [WindowsFact]
    public async Task QualifiedWorkload()
    {
        RequireRunner();
        var gates = CullPerfFiles.Read<CullPerfGateFile>(CullPerfFiles.GatePath);
        var id = Environment.GetEnvironmentVariable("CULL_PERF_CASE");
        var workload = gates.Workloads.Single(item => item.Id == id);
        var fixtures = CullPerfFiles.Fixtures();
        var directory = Path.Combine(CullPerfFiles.Run, workload.Id);
        Directory.CreateDirectory(directory);
        var recorder = new CullPerfRecorder();
        var raw = new RawBaseLoader { CullPerf = recorder };
        var standard = new StandardBaseLoader { CullPerf = recorder };
        CullPerfFiles.CopyCatalog(fixtures.Root, Path.Combine(directory, "catalog"));
        using var catalog = new CatalogService(Path.Combine(directory, "catalog"));
        await catalog.InitializeAsync();
        var images = Directory.GetFiles(fixtures.Root, "image-*").Order()
            .Select(path => new ImageFile(path)).ToArray();
        var states = await catalog.LoadImageStatesAsync(images.Select(image => image.FilePath).ToArray());
        foreach (var image in images) image.CatalogId = states[image.FilePath][0].CatalogId;
        var extension = workload.Fixture switch { "canon" => ".cr2", "fuji" => ".raf", _ => ".jpg" };
        var active = images.Where(image => image.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (workload.Exposure != 0)
            foreach (var image in active) image.EditSettings = new EditSettings { Exposure = workload.Exposure };
        var loader = new CullPerfAheadLoader(new BaseLoaderRouter(raw, standard));
        var vm = new MainWindowViewModel(catalog, loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        var ledger = new List<CullPerfSubmission>();
        var failures = new List<string>();
        Dictionary<string, double> counters = [];
        try
        {
            await CullPerfPreparation.PrepareAsync(catalog, vm, active, workload);
            vm.Browse.SetImages(workload.Pattern == "picks" ? images : active);
            if (workload.Pattern == "picks") vm.RestoreShowCapturePairs(true);
            vm.ImageService.Previews.AdjacentWarmEnabled = true;
            vm.IsDevelopMode = workload.Surface == "develop";
            vm.SelectedImage = active[0];
            if (workload.Surface == "loupe") vm.EnterLoupeCommand.Execute(null);
            if (workload.Surface == "fullscreen") vm.ToggleFullScreenCommand.Execute(null);
            await IdleAsync(vm);
            vm.ImageService.Previews.CullPerf = recorder;
            if (workload.Pattern == "ahead")
            {
                // Remove only the selected warm target's rendered entry; the gate names this condition.
                var cachePath = catalog.GetPreviewPath(active[2].CatalogId);
                if (File.Exists(cachePath)) File.Delete(cachePath);
                var metadataPath = Path.ChangeExtension(cachePath, ".meta");
                if (File.Exists(metadataPath)) File.Delete(metadataPath);
                loader.Target = active[2].CatalogId;
                if (!vm.ImageService.Previews.TryStartAdjacentWarm(active[2]))
                    throw new InvalidOperationException("The ahead worker was not admitted.");
                await loader.Started.Task.WaitAsync(TestWaits.Condition);
            }
            var replacements = 0;
            vm.Browse.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(vm.Browse.VisibleImages)) replacements++;
            };
            using var sampler = new CullPerfProcessSampler(vm);
            var pending = new List<Task>();
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < workload.Actions; i++)
            {
                var due = CullPerfPreparation.DueMilliseconds(workload, i);
                var remaining = due - Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                // Fixed input cadence is the measured workload, not a correctness wait.
                if (remaining > 0) await Task.Delay(TimeSpan.FromMilliseconds(remaining));
                if (workload.Pattern == "picks")
                {
                    ledger.Add(new(i + 1, Stopwatch.GetTimestamp(), vm.SelectedImage!.CatalogId, true));
                    pending.Add((i % 2 == 0 ? vm.TogglePickedImageCommand : vm.UnpickImageCommand).ExecuteAsync(null));
                }
                else
                {
                    var reverse = workload.Pattern == "reversal" && i % (workload.ReversalLength * 2) >= workload.ReversalLength;
                    var target = reverse ? vm.Browse.PreviousVisible(vm.SelectedImage) : vm.Browse.NextVisible(vm.SelectedImage);
                    ledger.Add(new(i + 1, Stopwatch.GetTimestamp(), target?.CatalogId ?? 0, false));
                    (reverse ? vm.SelectPreviousImageCommand : vm.SelectNextImageCommand).Execute(null);
                }
            }
            await Task.WhenAll(pending).WaitAsync(TestWaits.Condition);
            await IdleAsync(vm);
            if (workload.Pattern == "picks")
            {
                var stem = Path.GetFileNameWithoutExtension(vm.SelectedImage!.FilePath);
                var pair = images.Where(image => Path.GetFileNameWithoutExtension(image.FilePath) == stem).ToArray();
                var saved = await catalog.LoadAssessmentSnapshotsAsync(pair.Select(image => image.CatalogId).ToArray());
                if (pair.Length != 2 || saved.Count != 2 || saved.Any(state => state.Flag != ImageFlag.Unflagged))
                    failures.Add("Final capture pair catalog flags differ from final input.");
            }
            counters = await sampler.FinishAsync(vm);
            counters["visible-collection-replacements"] = replacements;
            if (workload.Pattern == "ahead")
            {
                counters["ahead-started-timestamp"] = loader.StartedAt;
                counters["ahead-returned-timestamp"] = loader.ReturnedAt;
                if (ledger[0].Timestamp < loader.StartedAt || ledger[0].Timestamp > loader.ReturnedAt)
                    failures.Add("The foreground input did not overlap the real ahead decode.");
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception.ToString());
        }
        finally
        {
            await vm.DisposeAsync();
        }
        var events = recorder.Snapshot();
        var accounting = CullPerfLedger.Reconcile(ledger, events, workload.CacheCondition == "matched");
        foreach (var pair in CullPerfCounters.Derive(events)) counters[pair.Key] = pair.Value;
        var fragment = new CullPerfFragment(workload.Id, CullPerfFiles.Hash(CullPerfFiles.GatePath),
            Environment.GetEnvironmentVariable("CULL_PERF_MACHINE") ?? "", fixtures.Hashes,
            workload.CacheCondition, true, false, recorder.LostEvents, ledger.Count,
            accounting.Completed, accounting.Cancelled, accounting.Superseded, accounting.NoOp,
            failures.Concat(accounting.Failures).ToArray(), accounting.Samples, counters,
            workload.Metrics.Where(id => !accounting.Samples.ContainsKey(id) &&
                gates.Metrics.Any(metric => metric.Id == id && metric.Maximum == null && !metric.Foreground)).ToArray());
        CullPerfFiles.WriteNew(Path.Combine(directory, "events.json"), new { ledger, accounting.Operations, events });
        CullPerfFiles.WriteNew(Path.Combine(directory, "fragment.json"), fragment);
        Assert.Empty(failures);
    }

    [Fact]
    public void EvaluateEvidence()
    {
        RequireRunner();
        var gates = CullPerfFiles.Read<CullPerfGateFile>(CullPerfFiles.GatePath);
        var fragments = Directory.GetFiles(CullPerfFiles.Run, "fragment.json", SearchOption.AllDirectories)
            .Select(CullPerfFiles.Read<CullPerfFragment>).ToArray();
        var baselinePath = Environment.GetEnvironmentVariable("CULL_PERF_BASELINE");
        var baseline = string.IsNullOrEmpty(baselinePath) ? null
            : CullPerfFiles.ReadBaselineFragments(baselinePath);
        var evaluation = CullPerfEvaluator.Evaluate(gates, CullPerfFiles.Hash(CullPerfFiles.GatePath), fragments, baseline);
        CullPerfFiles.WriteNew(Path.Combine(CullPerfFiles.Run, "evaluation.json"), new { evaluation, fragments });
    }

    private static Task IdleAsync(MainWindowViewModel vm) => TestWaits.UntilAsync(() =>
        vm.InitialPreviewActivityCount == 0 && !vm.RawProfilePickerState.IsLoading &&
        vm.ImageService.Previews.PreviewActivityCount == 0 &&
        vm.ImageService.Previews.PendingCacheWrites == 0 && vm.LoupeLoadingTask.IsCompleted);

    private static void RequireRunner() => Assert.SkipWhen(
        Environment.GetEnvironmentVariable("CULL_PERF_RUN") == null,
        "Use scripts/cull-perf.ps1 for isolated qualification.");
}

