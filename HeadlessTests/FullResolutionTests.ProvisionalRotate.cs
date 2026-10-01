using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class FullResolutionTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProvisionalRotationRetainsRealRestingPaintAtFitAndOneToOne(bool full)
    {
        using var fixture = new CatalogVmFixture();
        using var catalog = await fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = await Start(fixture, catalog, new FullLoader(), clock);
        var resting = vm.PreviewImage!;
        var size = vm.OriginalViewPixelSize;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            if (full)
            {
                vm.PublishRequiredDeviceLongEdge(400);
                await TestWaits.UntilAsync(() => vm.PreviewImage?.PixelSize.Width == 400);
                await vm.FullResolutionWork;
            }

            vm.ImageService.Previews.RenderGateAsync = () => release.Task;
            vm.RotateLeftCommand.Execute(null);
            Assert.NotSame(resting, vm.PreviewImage);
            Assert.Equal(new PixelSize(resting.PixelSize.Height, resting.PixelSize.Width), vm.PreviewImage!.PixelSize);
            Assert.Equal(new PixelSize(size.Height, size.Width), vm.OriginalViewPixelSize);
            var retained = typeof(MainWindowViewModel).GetField("_rotationRetainedBitmap",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm);
            Assert.Same(resting, retained);
            Assert.Null(vm.SpotDisplayMap);
            Assert.False(vm.HasArmedRestingRender);
        }
        finally
        {
            release.TrySetResult();
        }
    }
}
