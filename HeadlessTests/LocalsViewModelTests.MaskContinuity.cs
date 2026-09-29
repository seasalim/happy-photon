using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaFact]
    public async Task MaskStartsOnAndOTogglesOnlySessionState()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        Assert.True(vm.ShowLocalMask);
        await PrepareShortcutLocal(vm, catalog);
        vm.SelectedImage!.EditSettings.Locals = [new() { Type = "brush", Strokes = [] }];
        vm.SelectedLocal = vm.Locals[0];
        var window = new MainWindow { Focusable = true };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        Assert.True(window.Focus());
        var before = await ShortcutSnapshot(vm, catalog);
        Assert.True(vm.IsLocalMaskVisible);
        ShortcutPress(window, Key.O);
        Assert.False(vm.ShowLocalMask);

        foreach (var exposure in new[] { 0d, 1d, 0d })
        {
            vm.LocalExposure = exposure;
            Assert.False(vm.IsLocalMaskVisible);
        }

        ShortcutPress(window, Key.O);
        Assert.True(vm.ShowLocalMask);

        foreach (var exposure in new[] { 0d, 1d, 0d })
        {
            vm.LocalExposure = exposure;
            Assert.True(vm.IsLocalMaskVisible);
        }

        await AssertShortcutSnapshot(vm, catalog, before);
        vm.ShowLocalMask = false;
        vm.RestoreBrushPreferences(new AppSettings());
        Assert.False(vm.ShowLocalMask);
        await using var nextSession = CreateVm(catalog, new TestTimeProvider());
        Assert.True(nextSession.ShowLocalMask);
    }

    [AvaloniaFact]
    public async Task BrushOverlayReadsAllocateNothingAndFollowStrokeChanges()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await PrepareBrushMask(vm, catalog);
        Assert.True(vm.BeginBrushStroke(new(.6, .6)));
        var initial = Assert.Single(vm.BrushOverlayStrokes);
        Assert.True(vm.ExtendBrushStroke(new(.8, .6), 1000));
        Assert.NotSame(initial, Assert.Single(vm.BrushOverlayStrokes));
        Assert.Same(vm.LiveBrushStroke, Assert.Single(vm.BrushOverlayStrokes));
        AssertOverlayReadsAllocateNothing(vm);
        vm.LocalMaskRenderGateAsync = () => Task.FromException(new InvalidOperationException("Injected mask failure"));
        await vm.CompleteLocalsGestureAsync();
        await vm.PendingLocalMaskTask.WaitAsync(TestWaits.Condition);
        Assert.Single(vm.BrushOverlayStrokes);
        AssertOverlayReadsAllocateNothing(vm);
        vm.ShowLocalMask = false;
        Assert.Empty(vm.BrushOverlayStrokes);
        Assert.Null(vm.LocalRangeMask);
        AssertOverlayReadsAllocateNothing(vm);
    }

    private static void AssertOverlayReadsAllocateNothing(MainWindowViewModel vm)
    {
        var mask = vm.LocalRangeMask;
        var strokes = vm.BrushOverlayStrokes;
        var allocated = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 1000; i++)
        {
            mask = vm.LocalRangeMask;
            strokes = vm.BrushOverlayStrokes;
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - allocated);
        GC.KeepAlive(mask);
        GC.KeepAlive(strokes);
    }

    [AvaloniaFact]
    public async Task ReplacementFailureKeepsShownMaskAndReleaseRibbon()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await PrepareBrushMask(vm, catalog);
        var shown = vm.LocalRangeMask;
        vm.LocalMaskRenderGateAsync = () => Task.FromException(new InvalidOperationException("Injected mask failure"));
        Assert.True(vm.BeginBrushStroke(new(.7, .6)));
        await vm.CompleteLocalsGestureAsync();
        await vm.PendingLocalMaskTask.WaitAsync(TestWaits.Condition);
        Assert.Same(shown, vm.LocalRangeMask);
        Assert.Single(vm.BrushOverlayStrokes);
        Assert.False(vm.IsLocalRangeMaskUpdating);
        vm.LocalMaskRenderGateAsync = null;
        Assert.True(vm.BeginBrushStroke(new(.8, .7)));
        Assert.Equal(2, vm.BrushOverlayStrokes.Count());
        await vm.CompleteLocalsGestureAsync();
        await vm.PendingLocalMaskTask.WaitAsync(TestWaits.Condition);
        Assert.NotSame(shown, vm.LocalRangeMask);
        Assert.Empty(vm.BrushOverlayStrokes);
    }

    [AvaloniaFact]
    public async Task SupersededRequestsKeepOneShownBitmapAndOneRenderThenRetireTheOldBitmap()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await PrepareBrushMask(vm, catalog);
        var shown = Assert.IsType<WriteableBitmap>(vm.LocalRangeMask);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        var inFlight = 0;
        var peak = 0;
        vm.LocalMaskRenderGateAsync = async () =>
        {
            requests++;
            peak = Math.Max(peak, ++inFlight);
            entered.TrySetResult();
            await release.Task;
            inFlight--;
        };

        try
        {
            Assert.True(vm.BeginBrushStroke(new(.7, .6)));
            await vm.CompleteLocalsGestureAsync();
            await entered.Task.WaitAsync(TestWaits.Condition);

            for (var i = 0; i < 5; i++)
            {
                Assert.True(vm.BeginBrushStroke(new(.3 + i * .1, .4)));
                await vm.CompleteLocalsGestureAsync();
                Assert.Same(shown, vm.LocalRangeMask);
                Assert.True(vm.IsLocalRangeMaskUpdating);
                Assert.Equal(1, requests);
            }

            Assert.Equal(6, vm.BrushOverlayStrokes.Count());
            using (shown.Lock()) { }

            release.TrySetResult();
            await vm.PendingLocalMaskTask.WaitAsync(TestWaits.Condition);
            Assert.Equal(2, requests);
            Assert.Equal(1, peak);
            Assert.NotSame(shown, vm.LocalRangeMask);
            Assert.Empty(vm.BrushOverlayStrokes);
            Dispatcher.UIThread.RunJobs();
            Assert.ThrowsAny<Exception>(() => shown.Lock());
            var replacement = Assert.IsType<WriteableBitmap>(vm.LocalRangeMask);
            vm.ShowLocalMask = false;
            Dispatcher.UIThread.RunJobs();
            Assert.ThrowsAny<Exception>(() => replacement.Lock());
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [AvaloniaFact]
    public async Task TemporarilyMissingRangeBaseKeepsMaskWithoutClaimingAnUpdate()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await PrepareBrushMask(vm, catalog);
        await vm.ToggleLocalLuminanceCommand.ExecuteAsync(null);
        vm.LocalLuminanceLower = 20;
        await vm.PendingLocalMaskTask.WaitAsync(TestWaits.Condition);
        var shown = vm.LocalRangeMask;
        vm.LocalMaskRenderGateAsync = () => Task.FromException(new InvalidOperationException("Injected mask failure"));
        vm.LocalLuminanceLower = 30;
        await vm.PendingLocalMaskTask.WaitAsync(TestWaits.Condition);
        var original = vm.PreviewImage;
        using var unmatched = new WriteableBitmap(new(32, 24), new(96, 96));

        try
        {
            vm.PreviewImage = unmatched;
            Assert.Same(shown, vm.LocalRangeMask);
            Assert.False(vm.IsLocalRangeMaskUpdating);
            vm.LocalMaskRenderGateAsync = null;
            vm.PreviewImage = original;
            await vm.PendingLocalMaskTask.WaitAsync(TestWaits.Condition);
            Assert.NotSame(shown, vm.LocalRangeMask);
            Assert.NotNull(vm.LocalRangeMask);
        }
        finally
        {
            vm.PreviewImage = original;
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VisibilityEndInvalidatesPresentationBeforeNotification(bool fullscreen)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await PrepareBrushMask(vm, catalog);
        vm.ShowLocalMask = false;
        vm.IsLocalMaskHeld = true;
        await vm.PendingLocalMaskTask.WaitAsync(TestWaits.Condition);
        var shown = Assert.IsType<WriteableBitmap>(vm.LocalRangeMask);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.LocalMaskRenderGateAsync = () =>
        {
            entered.TrySetResult();

            return release.Task;
        };

        try
        {
            Assert.True(vm.BeginBrushStroke(new(.7, .7)));
            await vm.CompleteLocalsGestureAsync();
            await entered.Task.WaitAsync(TestWaits.Condition);
            Assert.Same(shown, vm.LocalRangeMask);
            Assert.Single(vm.BrushOverlayStrokes);
            vm.ToggleLocalHuePickCommand.Execute(null);
            Assert.True(vm.IsLocalHuePicking);
            vm.IsLocalMaskHeld = false;
            vm.ShowLocalMask = false;
            Assert.False(vm.ShowLocalMask);
            Assert.False(vm.IsLocalMaskHeld);
            Assert.True(vm.IsLocalMaskVisible);
            Assert.Same(shown, vm.LocalRangeMask);
            Assert.Single(vm.BrushOverlayStrokes);
            var hiddenNotifications = 0;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(vm.IsLocalMaskVisible) || vm.IsLocalMaskVisible) return;

                hiddenNotifications++;
                Assert.Null(vm.LocalRangeMask);
                Assert.Empty(vm.BrushOverlayStrokes);
            };

            if (fullscreen)
            {
                vm.IsFullScreenMode = true;
            }
            else
            {
                vm.EscapeLocals();
                Assert.False(vm.IsLocalHuePicking);
            }

            Assert.False(vm.IsLocalMaskVisible);
            Assert.Null(vm.LocalRangeMask);
            Assert.Empty(vm.BrushOverlayStrokes);
            Assert.True(hiddenNotifications > 0);
            Dispatcher.UIThread.RunJobs();
            Assert.ThrowsAny<Exception>(() => shown.Lock());
            release.TrySetResult();
            await vm.PendingLocalMaskTask.WaitAsync(TestWaits.Condition);
            Assert.Null(vm.LocalRangeMask);
            Assert.Empty(vm.BrushOverlayStrokes);
            AssertOverlayReadsAllocateNothing(vm);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    private async Task PrepareBrushMask(MainWindowViewModel vm, CatalogService catalog)
    {
        await Prepare(vm, catalog);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.AddBrushCommand.Execute(null);
        Assert.True(vm.BeginBrushStroke(new(.2, .2)));
        await vm.CompleteLocalsGestureAsync();
        await vm.PendingLocalMaskTask.WaitAsync(TestWaits.Condition);
        Assert.NotNull(vm.LocalRangeMask);
    }
}
