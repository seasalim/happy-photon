using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaFact]
    public async Task BrushShiftClickDoesNotQueueAnUnchangedTrailingPreview()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.SelectedImage!.EditSettings.Locals = [new() { Type = "brush", Exposure = 1,
            Strokes = [new() { Points = [new(8192, 8192)] }] }];
        vm.SelectedLocal = vm.Locals[0];
        var renders = 0;
        vm.ImageService.Previews.RenderGateAsync = () => { renders++; return Task.CompletedTask; };
        try
        {
            Assert.True(vm.BeginBrushStroke(new(.7, .7), straight: true));
            Assert.Equal(2, vm.LiveBrushStroke!.Points.Count);
            await vm.PendingPreviewDebounceTask!.WaitAsync(TestWaits.Condition);
            clock.Advance(TimeSpan.FromMilliseconds(120));
            Dispatcher.UIThread.RunJobs();
            await vm.PendingPreviewDebounceTask!.WaitAsync(TestWaits.Condition);
            Assert.Equal(1, renders);
        }
        finally { vm.ImageService.Previews.RenderGateAsync = null; vm.DiscardLocalsGesture(); }
    }
    [AvaloniaFact]
    public async Task SlowBrushPreviewsPaintDuringContinuousInputAndRenderTheFinalState()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.SelectedImage!.EditSettings.Locals = [new() { Type = "brush", Exposure = 1, Strokes = [] }];
        vm.SelectedLocal = vm.Locals[0];
        var history = vm.HistoryEntries.Count;
        var releases = Enumerable.Range(0, 3).Select(_ =>
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var renders = 0;
        var inFlight = 0;
        var maximumInFlight = 0;
        var painted = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.PreviewImage) && vm.PreviewImage is { } bitmap)
                painted.Add(vm.ImageService.Previews.TryGetPreviewRenderIdentity(bitmap)?.SettingsHash);
        };
        vm.ImageService.Previews.RenderGateAsync = async () =>
        {
            var index = Interlocked.Increment(ref renders) - 1;
            var active = Interlocked.Increment(ref inFlight);
            maximumInFlight = Math.Max(maximumInFlight, active);
            try { if (index < releases.Length) await releases[index].Task; }
            finally { Interlocked.Decrement(ref inFlight); }
        };
        try
        {
            Assert.True(vm.BeginBrushStroke(new(.1, .1)));
            var firstHash = RenderSettingsHash.Compute(vm.SelectedImage.EditSettings);
            await TestWaits.UntilAsync(() => Volatile.Read(ref renders) == 1);
            AddPoints(0);
            Assert.Equal(1, Volatile.Read(ref renders));
            releases[0].TrySetResult();
            await TestWaits.UntilAsync(() => painted.Contains(firstHash) && Volatile.Read(ref renders) == 2);
            Assert.True(vm.IsBrushStrokeActive);
            Assert.Equal(history, vm.HistoryEntries.Count);

            var secondHash = RenderSettingsHash.Compute(vm.SelectedImage.EditSettings);
            AddPoints(1);
            var finalHash = RenderSettingsHash.Compute(vm.SelectedImage.EditSettings);
            Assert.NotEqual(secondHash, finalHash);
            Assert.Equal(2, Volatile.Read(ref renders));
            releases[1].TrySetResult();
            await TestWaits.UntilAsync(() => painted.Contains(secondHash) && Volatile.Read(ref renders) == 3);
            // The last render remains held past two throttle intervals, with no more input.
            clock.Advance(TimeSpan.FromMilliseconds(120));
            Dispatcher.UIThread.RunJobs();
            releases[2].TrySetResult();
            await TestWaits.UntilAsync(() => painted.Contains(finalHash));
            await vm.PendingPreviewDebounceTask!;
            Assert.Equal(finalHash, vm.ImageService.Previews.TryGetPreviewRenderIdentity(vm.PreviewImage!)?.SettingsHash);
            Assert.Equal(1, maximumInFlight);
            Assert.Equal(3, renders);
            Assert.True(vm.IsBrushStrokeActive);
            Assert.Equal(history, vm.HistoryEntries.Count);
        }
        finally
        {
            vm.ImageService.Previews.RenderGateAsync = null;
            vm.DiscardLocalsGesture();
            foreach (var release in releases) release.TrySetResult();
        }

        void AddPoints(int batch)
        {
            for (var i = 0; i < 12; i++)
            {
                Assert.True(vm.ExtendBrushStroke(new(i % 2 == 0 ? .8 : .2, .3 + batch * .2), 1000));
                clock.Advance(TimeSpan.FromMilliseconds(10));
                Dispatcher.UIThread.RunJobs();
            }
        }
    }

    [AvaloniaFact]
    public async Task BrushPreferenceBurstSavesOnceAfterTheLastChange()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        var saves = new List<AppSettings>();
        vm.PersistAppSettingsAsync = () =>
        {
            var settings = new AppSettings();
            vm.CaptureBrushPreferences(settings);
            saves.Add(settings);
            return Task.CompletedTask;
        };
        for (var i = 0; i < 12; i++)
        {
            vm.BrushSize = 70 + i;
            vm.BrushFeather = 20 + i;
            vm.BrushFlow = 40 + i;
            clock.Advance(TimeSpan.FromMilliseconds(10));
            Dispatcher.UIThread.RunJobs();
        }
        vm.BrushMode = "erase";
        clock.Advance(TimeSpan.FromMilliseconds(249));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(saves);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await TestWaits.UntilAsync(() => saves.Count == 1);
        var saved = Assert.Single(saves);
        Assert.Equal(81, saved.BrushSize);
        Assert.Equal(31, saved.BrushFeather);
        Assert.Equal(51, saved.BrushFlow);
        Assert.Equal("erase", saved.BrushMode);
    }
}
