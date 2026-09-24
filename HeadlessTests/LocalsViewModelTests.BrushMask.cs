using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaTheory]
    [InlineData("off")] [InlineData("pending")] [InlineData("cached")]
    public async Task BrushMaskPinsBeforeStateAndRendersOnceAtRelease(string start)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.SelectedImage!.EditSettings.Locals = [new() { Type = "brush", Exposure = 1,
            Strokes = [new() { Points = [new(8192, 8192)] }] }];
        vm.SelectedLocal = vm.Locals[0];
        var renders = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.LocalMaskRenderGateAsync = () => { renders++; return start == "pending" && renders == 1 ? release.Task : Task.CompletedTask; };
        try
        {
            if (start != "off") vm.ShowLocalMask = true;
            if (start == "cached") await vm.PendingLocalMaskTask;
            var cached = vm.LocalRangeMask;
            Assert.True(vm.BeginBrushStroke(new(.2, .2)));
            for (var i = 0; i < 10; i++) Assert.True(vm.ExtendBrushStroke(new(.1 + i * .05, .7), 1000));
            Assert.Equal(1, renders);
            release.TrySetResult(); await vm.PendingLocalMaskTask;
            Assert.NotNull(vm.LocalRangeMask);
            if (start == "cached") Assert.Same(cached, vm.LocalRangeMask);
            var pinned = vm.LocalRangeMask;
            // Turning on the user's toggle changes visibility policy, never the pinned identity.
            vm.ShowLocalMask = true;
            Assert.Same(pinned, vm.LocalRangeMask); Assert.Equal(1, renders);
            await vm.CompleteLocalsGestureAsync(); await vm.PendingLocalMaskTask;
            Assert.Equal(2, renders); Assert.NotNull(vm.LocalRangeMask); Assert.NotSame(pinned, vm.LocalRangeMask);
        }
        finally { release.TrySetResult(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task PendingBrushMaskCannotPublishAfterDiscardOrNavigation(bool navigate)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.LocalMaskRenderGateAsync = () => { entered.TrySetResult(); return release.Task; };
        try
        {
            vm.AddBrushCommand.Execute(null); Assert.True(vm.BeginBrushStroke(new(.2, .2)));
            await entered.Task.WaitAsync(TestWaits.Condition);
            Assert.True(vm.IsLocalRangeMaskUpdating);
            if (navigate) vm.SelectedImage = new ImageFile(_fixture.Path("next.jpg"));
            else vm.EscapeLocals();
            release.TrySetResult(); await vm.PendingLocalMaskTask;
            Assert.Null(vm.LocalRangeMask); Assert.False(vm.IsLocalRangeMaskUpdating); Assert.Empty(vm.Locals);
        }
        finally { release.TrySetResult(); }
    }
    [AvaloniaFact]
    public async Task MonochromeBrushIgnoresStoredHueWithoutAcquiringAMaskBase()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, mono: true, raw: true);
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.SelectedImage!.EditSettings.Locals = [new() { Type = "brush",
            Hue = new() { Enabled = true }, Strokes = [new() { Points = [new(8192, 8192)] }] }];
        vm.SelectedLocal = vm.Locals[0];
        Assert.False(vm.IsSelectedLocalRangeRestricted);
        await vm.PendingLocalMaskTask;
        Assert.NotNull(vm.LocalRangeMask); Assert.False(vm.IsLocalRangeMaskUpdating);
    }

    [AvaloniaFact]
    public async Task FirstBrushStrokePinsEmptyMaskWithToggleOffThroughRelease()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        var renders = 0;
        vm.LocalMaskRenderGateAsync = () => { renders++; return Task.CompletedTask; };
        vm.AddBrushCommand.Execute(null); vm.BeginBrushStroke(new(.2, .2));
        await vm.PendingLocalMaskTask;
        Assert.Equal(1, renders); Assert.NotNull(vm.LocalRangeMask);
        using (var empty = ((Avalonia.Media.Imaging.WriteableBitmap)vm.LocalRangeMask!).Lock())
        {
            var bytes = new byte[empty.RowBytes * empty.Size.Height];
            System.Runtime.InteropServices.Marshal.Copy(empty.Address, bytes, 0, bytes.Length);
            Assert.All(bytes, value => Assert.Equal(0, value));
        }
        vm.ExtendBrushStroke(new(.6, .6), 1000);
        Assert.Equal(1, renders); Assert.False(vm.ShowLocalMask);
        await vm.CompleteLocalsGestureAsync(); await vm.PendingLocalMaskTask;
        Assert.Equal(2, renders); Assert.False(vm.ShowLocalMask); Assert.True(vm.IsLocalMaskVisible);
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task BrushMaskStaysPinnedWhenInteractivePreviewReplacesRestingSurface(bool restricted)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.SelectedImage!.EditSettings.Locals = [new() { Type = "brush", Exposure = 1,
            Luminance = restricted ? new() { Enabled = true, Lower = .2 } : null,
            Strokes = [new() { Points = [new(8192, 8192)] }] }];
        vm.SelectedLocal = vm.Locals[0];
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renders = 0;
        vm.LocalMaskRenderGateAsync = () => { renders++; return release.Task; };
        vm.ShowLocalMask = true;
        var size = vm.PreviewImage!.PixelSize;
        Assert.True(vm.BeginBrushStroke(new(.2, .2)));
        await vm.PendingPreviewDebounceTask!;
        var surface = vm.PreviewImage;
        using var replacement = new Avalonia.Media.Imaging.WriteableBitmap(new Avalonia.PixelSize(32, 24),
            new Avalonia.Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
        try
        {
            vm.PreviewImage = replacement;
            Assert.Equal(1, renders);
            release.TrySetResult(); await vm.PendingLocalMaskTask;
            Assert.Equal(size, vm.LocalRangeMask!.PixelSize);
            vm.ExtendBrushStroke(new(.6, .6), 1000); Assert.Equal(1, renders);
        }
        finally { release.TrySetResult(); vm.PreviewImage = surface; vm.DiscardLocalsGesture(); }
    }

}
