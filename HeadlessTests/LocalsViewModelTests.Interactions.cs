using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaTheory]
    [InlineData("escape")]
    [InlineData("capture-loss")]
    [InlineData("navigation")]
    [InlineData("fullscreen")]
    [InlineData("original")]
    [InlineData("split")]
    [InlineData("crop")]
    [InlineData("preset")]
    [InlineData("preset-remove")]
    [InlineData("paste")]
    [InlineData("history-hover")]
    [InlineData("version")]
    public async Task ReplacingAndTransientCommandsDiscardGeometry(string route)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        var image = vm.SelectedImage!;
        var before = vm.SelectedLocal! with { };
        await vm.PresetService.UseDirectoryAsync(_fixture.Path("presets"));
        var preset = await vm.PresetService.SaveUserPresetAsync("Test", new EditSettings { Exposure = .5 });
        if (route == "preset-remove") await vm.ApplyPresetAsync(preset.Id);
        if (route == "paste") vm.CopyEditSettingsCommand.Execute(null);
        Assert.True(vm.BeginLocalsGesture(LocalHandle.Center, new(.5, .5)));
        vm.MoveLocalsGesture(new(.8, .7), 100);
        switch (route)
        {
            case "escape": await vm.HandleEscapeCommand.ExecuteAsync(null); break;
            case "capture-loss": vm.DiscardLocalsGesture(); break;
            case "navigation":
                vm.SelectedImage = new ImageFile(_fixture.Path("next.jpg"));
                break;
            case "fullscreen": vm.ToggleFullScreenCommand.Execute(null); break;
            case "original": await vm.ToggleBeforeAfterCommand.ExecuteAsync(null); break;
            case "split": await vm.ToggleBeforeAfterSplitCommand.ExecuteAsync(null); break;
            case "crop": await vm.ToggleCropModeCommand.ExecuteAsync(null); break;
            case "preset": case "preset-remove": await vm.ApplyPresetAsync(preset.Id); break;
            case "paste": await vm.PasteEditSettingsCommand.ExecuteAsync(null); break;
            case "history-hover": await vm.PreviewHistoryHoverAsync(vm.HistoryEntries.Last()); break;
            case "version": await vm.NewVersionFromCurrentCommand.ExecuteAsync(null); break;
        }
        Assert.False(vm.IsLocalsGestureActive);
        Assert.Equal(before, Assert.Single(image.EditSettings.Locals!));
        if (route is "fullscreen" or "original" or "split" or "history-hover")
        {
            Assert.False(vm.CanEditLocals);
            Assert.False(vm.IsLocalMaskVisible);
            await vm.ResumeLocalEditingCommand.ExecuteAsync(null);
            Assert.True(vm.CanEditLocals);
            Assert.Equal(before.Id, vm.SelectedLocal!.Id);
        }
        if (route == "crop") Assert.False(vm.IsLocalsMode);
        if (route == "version")
        {
            Assert.NotSame(image.EditSettings.Locals![0], vm.SelectedImage!.EditSettings.Locals![0]);
            Assert.Equal(before, vm.SelectedImage.EditSettings.Locals[0]);
        }
    }

    [AvaloniaFact]
    public async Task CapDeletionAndSliderUseStableIdentityAndOneHistoryStep()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        for (var i = 0; i < 8; i++) await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        Assert.False(vm.CanAddLocal);
        Assert.Contains("8 of 8", vm.LocalsInstruction);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        Assert.Equal(8, vm.Locals.Count);
        var neighbor = vm.Locals[6].Id;
        await vm.DeleteLocalCommand.ExecuteAsync(vm.SelectedLocal);
        Assert.Equal(neighbor, vm.SelectedLocal!.Id);
        var count = vm.HistoryEntries.Count;
        vm.OnSliderEditStarted();
        vm.LocalExposure = 1;
        clock.Advance(TimeSpan.FromMilliseconds(200));
        await vm.PendingPreviewDebounceTask!;
        Assert.Equal(count, vm.HistoryEntries.Count);
        vm.LocalExposure = 2;
        vm.OnSliderEditCompleted();
        clock.Advance(TimeSpan.FromMilliseconds(200));
        await vm.PendingPreviewDebounceTask!;
        Assert.Equal(count + 1, vm.HistoryEntries.Count);
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(0, vm.LocalExposure);
        Assert.Equal(neighbor, vm.SelectedLocal.Id);
    }

    [AvaloniaFact]
    public async Task LateDraftRenderCannotMoveRestoredHandles()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        var before = vm.SelectedLocal! with { };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        vm.ImageService.Previews.RenderGateAsync = () =>
        {
            if (Interlocked.Increment(ref count) != 1) return Task.CompletedTask;
            started.TrySetResult();
            return release.Task;
        };
        try
        {
            vm.BeginLocalsGesture(LocalHandle.Center, new(.5, .5));
            vm.MoveLocalsGesture(new(.7, .6), 100);
            var pending = vm.PendingPreviewDebounceTask!;
            clock.Advance(TimeSpan.FromMilliseconds(100));
            await started.Task.WaitAsync(TestWaits.Condition);
            vm.DiscardLocalsGesture();
            clock.Advance(TimeSpan.FromMilliseconds(100));
            release.TrySetResult();
            await pending;
            await vm.PendingPreviewDebounceTask!;
            Assert.Equal(before, vm.SelectedLocal);
        }
        finally { release.TrySetResult(); }
    }
}
