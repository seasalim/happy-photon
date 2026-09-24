using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaTheory]
    [InlineData("escape")] [InlineData("complete")] [InlineData("navigation")]
    public async Task StoppingBrushPreviewRejectsHeldRenderAndItsTrailingDispatch(string action)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.AddBrushCommand.Execute(null);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renders = 0;
        vm.ImageService.Previews.RenderGateAsync = () =>
        {
            if (Interlocked.Increment(ref renders) != 1) return Task.CompletedTask;
            entered.TrySetResult();
            return release.Task;
        };
        var painted = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.PreviewImage) && vm.PreviewImage is { } bitmap)
                painted.Add(vm.ImageService.Previews.TryGetPreviewRenderIdentity(bitmap)?.SettingsHash);
        };
        try
        {
            Assert.True(vm.BeginBrushStroke(new(.2, .2)));
            var heldHash = RenderSettingsHash.Compute(vm.SelectedImage!.EditSettings);
            await entered.Task.WaitAsync(TestWaits.Condition);
            Assert.True(vm.ExtendBrushStroke(new(.7, .7), 1000));
            clock.Advance(TimeSpan.FromMilliseconds(120));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, renders);
            if (action == "complete") await vm.CompleteLocalsGestureAsync();
            else if (action == "navigation") vm.SelectedImage = null;
            else vm.EscapeLocals();
            Assert.False(vm.IsBrushStrokeActive);
            clock.Advance(TimeSpan.FromMilliseconds(60));
            Dispatcher.UIThread.RunJobs();
            release.TrySetResult();
            await vm.PendingPreviewDebounceTask!.WaitAsync(TestWaits.Condition);
            Assert.DoesNotContain(heldHash, painted);
            Assert.Equal(action == "navigation" ? 1 : 2, renders);
        }
        finally
        {
            vm.ImageService.Previews.RenderGateAsync = null;
            vm.DiscardLocalsGesture();
            release.TrySetResult();
        }
    }

    [AvaloniaFact]
    public async Task DisposalCancelsPendingBrushPreferenceSave()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        var vm = CreateVm(catalog, clock);
        var saves = 0;
        vm.PersistAppSettingsAsync = () => { saves++; return Task.CompletedTask; };
        try { vm.BrushSize = 80; }
        finally { await vm.DisposeAsync(); }
        clock.Advance(TimeSpan.FromMilliseconds(250));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, saves);
    }
}
