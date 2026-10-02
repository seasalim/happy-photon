using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class AutoStraightenTests
{
    [AvaloniaFact]
    public async Task SelectionChangeDuringColdBaseDecodeDiscardsAutoSilently()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var loader = new TiltedLoader();
        await using var vm = CreateViewModel(catalog, loader);
        await OpenCropAsync(vm, catalog);
        await SettleAsync(vm);
        vm.ImageService.Previews.ClearPreviewCache();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sampled = false;
        vm.AutoStraightenGateAsync = () =>
        {
            sampled = true;

            return Task.CompletedTask;
        };
        loader.PreviewLoadOverride = token =>
        {
            using var registration = token.Register(() => cancelled.TrySetResult());
            started.TrySetResult();
            release.Task.WaitAsync(TestWaits.Condition).GetAwaiter().GetResult();
            token.ThrowIfCancellationRequested();

            return BaseImageLoadOutcome.Failed(BaseImageLoadFailure.DecodeFailed);
        };
        var detection = vm.AutoStraightenCommand.ExecuteAsync(null);

        try
        {
            await started.Task.WaitAsync(TestWaits.Condition);
            Assert.False(sampled);
            Assert.False(detection.IsCompleted);
            loader.PreviewLoadOverride = null;
            vm.SelectedImage = new ImageFile(_fixture.Path("second.jpg"));
            await cancelled.Task.WaitAsync(TestWaits.Condition);
            await TestWaits.UntilAsync(() => vm.IsHistoryLoaded);
            await SettleAsync(vm);
            var draft = vm.HorizonRotation;
            vm.TransientStatus = "Keep status";
            release.TrySetResult();
            await detection.WaitAsync(TestWaits.Condition);

            Assert.False(sampled);
            Assert.Equal(draft, vm.HorizonRotation);
            Assert.Equal("Keep status", vm.TransientStatus);
            Assert.False(vm.AutoStraightenCommand.IsRunning);
        }
        finally
        {
            loader.PreviewLoadOverride = null;
            release.TrySetResult();
            await detection.WaitAsync(TestWaits.Condition);
        }
    }

    [AvaloniaFact]
    public async Task MissingPreviewBaseDiscardsAutoWithoutStatusOrDraftChange()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var loader = new TiltedLoader();
        await using var vm = CreateViewModel(catalog, loader);
        await OpenCropAsync(vm, catalog);
        vm.HorizonRotation = .75;
        await SettleAsync(vm);
        vm.ImageService.Previews.ClearPreviewCache();
        loader.PreviewLoadOverride = _ =>
            BaseImageLoadOutcome.Failed(BaseImageLoadFailure.DecodeFailed);
        vm.TransientStatus = "Keep status";
        var sampled = false;
        vm.AutoStraightenGateAsync = () =>
        {
            sampled = true;

            return Task.CompletedTask;
        };

        await vm.AutoStraightenCommand.ExecuteAsync(null);

        Assert.False(sampled);
        Assert.Equal(.75, vm.HorizonRotation);
        Assert.Equal("Keep status", vm.TransientStatus);
        Assert.True(vm.AutoStraightenCommand.CanExecute(null));
        Assert.Empty(vm.HistoryEntries);
    }
}
