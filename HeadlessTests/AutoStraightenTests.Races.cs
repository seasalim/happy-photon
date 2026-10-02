using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class AutoStraightenTests
{
    [AvaloniaTheory]
    [InlineData("selection", false)]
    [InlineData("session", false)]
    [InlineData("horizon", false)]
    [InlineData("base", false)]
    [InlineData("decode-pending", false)]
    [InlineData("exposure", true)]
    [InlineData("geometry", true)]
    [InlineData("crop", true)]
    public async Task HeldDetectionHonorsOnlyItsStalenessKey(string change, bool keep)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var loader = new TiltedLoader();
        await using var vm = CreateViewModel(catalog, loader);
        await OpenCropAsync(vm, catalog);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.AutoStraightenGateAsync = () =>
        {
            started.TrySetResult();

            return release.Task;
        };
        var detection = vm.AutoStraightenCommand.ExecuteAsync(null);

        try
        {
            await started.Task.WaitAsync(TestWaits.Condition);
            Assert.False(vm.AutoStraightenCommand.CanExecute(null));

            switch (change)
            {
                case "selection":
                    vm.SelectedImage = new ImageFile(_fixture.Path("second.jpg"));
                    await TestWaits.UntilAsync(() => vm.IsHistoryLoaded);
                    break;

                case "session":
                    await vm.CancelCropCommand.ExecuteAsync(null).WaitAsync(TestWaits.Condition);
                    await vm.ToggleCropModeCommand.ExecuteAsync(null).WaitAsync(TestWaits.Condition);
                    Assert.True(vm.IsCropMode);
                    Assert.False(detection.IsCompleted);
                    break;

                case "horizon":
                    vm.HorizonRotation = 1;
                    break;

                case "base":
                case "decode-pending":
                    vm.LensProfileOverride = "changed profile";

                    if (change == "base")
                    {
                        await SettleAsync(vm);
                    }

                    break;

                case "exposure":
                    vm.Exposure = .4;
                    await SettleAsync(vm);
                    break;

                case "geometry":
                    vm.GeometryAspect = 100;
                    await SettleAsync(vm);
                    break;

                case "crop":
                    vm.CurrentCrop = new CropRegion { Left = .2, Right = .8 };
                    break;
            }

            var draft = vm.HorizonRotation;
            var status = vm.StatusMessage;
            release.TrySetResult();
            await detection.WaitAsync(TestWaits.Condition);

            Assert.Equal(keep ? loader.Expected : draft, vm.HorizonRotation);
            Assert.Equal(status, vm.StatusMessage);

            if (change == "crop") Assert.Equal(.2, vm.CurrentCrop!.Left);
            if (change == "geometry") Assert.Equal(100, vm.GeometryAspect);
            if (change == "exposure") Assert.Equal(.4, vm.Exposure);
        }
        finally
        {
            release.TrySetResult();
            await detection.WaitAsync(TestWaits.Condition);
        }
    }

    [AvaloniaFact]
    public async Task ShutdownCancelsHeldDetectionBeforeServiceDisposal()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var vm = CreateViewModel(catalog, new TiltedLoader());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? detection = null;
        var disposed = false;

        try
        {
            await OpenCropAsync(vm, catalog);
            vm.AutoStraightenGateAsync = () =>
            {
                started.TrySetResult();

                return release.Task;
            };
            detection = vm.AutoStraightenCommand.ExecuteAsync(null);
            await started.Task.WaitAsync(TestWaits.Condition);
            await vm.DisposeAsync().AsTask().WaitAsync(TestWaits.Condition);
            disposed = true;
            await detection.WaitAsync(TestWaits.Condition);

            Assert.False(release.Task.IsCompleted);
            Assert.Equal(0, vm.HorizonRotation);
            Assert.False(vm.AutoStraightenCommand.CanExecute(null));
        }
        finally
        {
            release.TrySetResult();
            if (detection != null) await detection.WaitAsync(TestWaits.Condition);
            if (!disposed) await vm.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task CommandIsDisabledOutsideCropAndForOnlineOnlyImage()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateViewModel(catalog, new TiltedLoader());
        Assert.False(vm.AutoStraightenCommand.CanExecute(null));
        var image = await OpenCropAsync(vm, catalog);
        Assert.True(vm.AutoStraightenCommand.CanExecute(null));
        var notifications = 0;
        vm.AutoStraightenCommand.CanExecuteChanged += (_, _) => notifications++;
        image.SourceRequiresHydration = true;
        Assert.False(vm.AutoStraightenCommand.CanExecute(null));
        image.SourceRequiresHydration = false;
        await vm.CancelCropCommand.ExecuteAsync(null);
        Assert.True(notifications > 0);
        Assert.False(vm.AutoStraightenCommand.CanExecute(null));
    }
}
