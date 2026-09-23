using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaFact]
    public async Task ViewerShortcutRepeatDuringCropCancelCannotReopenClosedLocals()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var window = new MainWindow { Focusable = true, Width = 1200, Height = 900 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        Assert.True(window.Focus());
        await vm.ToggleCropModeCommand.ExecuteAsync(null);
        Assert.True(vm.IsCropMode);
        Assert.False(vm.PendingPreviewDebounceTask!.IsCompleted);
        var before = await ShortcutSnapshot(vm, catalog);

        ShortcutPress(window, Key.W, RawInputModifiers.Shift);
        var first = Assert.IsAssignableFrom<Task>(vm.ToggleLocalsModeCommand.ExecutionTask);
        Assert.False(first.IsCompleted);
        Assert.False(vm.ToggleLocalsModeCommand.CanExecute(null));
        ShortcutPress(window, Key.W, RawInputModifiers.Shift);
        var latest = Assert.IsAssignableFrom<Task>(vm.ToggleLocalsModeCommand.ExecutionTask);
        try
        {
            // Finish the crop preview. A duplicate command can then remain blocked on
            // the new preview scheduled by the first crop cancellation.
            clock.Advance(TimeSpan.FromMilliseconds(200));
            await (await Task.WhenAny(first, latest).WaitAsync(TestWaits.Condition));
            Assert.True(vm.IsLocalsMode);
            Assert.False(vm.IsCropMode);
            Dispatcher.UIThread.RunJobs();
            var close = window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == "CloseLocalsButton");
            Assert.True(close.IsEffectivelyVisible);
            Assert.True(close.IsEffectivelyEnabled);
            var point = close.TranslatePoint(new(close.Bounds.Width / 2, close.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
            Assert.False(vm.IsLocalsMode);

            clock.Advance(TimeSpan.FromMilliseconds(200));
            await Task.WhenAll(first, latest).WaitAsync(TestWaits.Condition);
            await vm.PendingPreviewDebounceTask!.WaitAsync(TestWaits.Condition);
            Assert.False(vm.IsLocalsMode);
            Assert.Same(first, latest);
            await AssertShortcutSnapshot(vm, catalog, before);
        }
        finally
        {
            clock.Advance(TimeSpan.FromMilliseconds(200));
            await Task.WhenAll(first, latest).WaitAsync(TestWaits.Condition);
        }
    }
}
