using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using HappyPhoton.Services;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailedBrushPreviewDiscardsStrokeWithoutChangingCommittedPaint(bool cleared)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.AddBrushCommand.Execute(null);
        Assert.True(vm.BeginBrushStroke(new(.2, .2)));
        await vm.CompleteLocalsGestureAsync();
        if (cleared) await vm.ClearBrushStrokesCommand.ExecuteAsync(null);
        var image = vm.SelectedImage!;
        var before = EditSettingsJson.Serialize(image.EditSettings);
        var history = vm.HistoryEntries.Count;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var getterErrors = new List<Exception>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(vm.LiveBrushStroke)) return;
            try { _ = vm.LiveBrushStroke; }
            catch (Exception error) { getterErrors.Add(error); }
        };
        vm.ImageService.Previews.RenderGateAsync = async () =>
        {
            entered.TrySetResult();
            await release.Task;
            throw new InvalidOperationException("test brush preview failure");
        };
        try
        {
            Assert.True(vm.BeginBrushStroke(new(.4, .4)));
            var preview = vm.PendingPreviewDebounceTask!;
            await entered.Task.WaitAsync(TestWaits.Condition);
            Assert.True(vm.ExtendBrushStroke(new(.7, .7), 1000));
            release.TrySetResult();
            await preview.WaitAsync(TestWaits.Condition);
            Assert.Empty(getterErrors);
            Assert.False(vm.IsLocalsGestureActive);
            Assert.Null(vm.LiveBrushStroke);
            Assert.False(vm.ExtendBrushStroke(new(.9, .9), 1000));
            await vm.CompleteLocalsGestureAsync();
            Assert.Equal(before, EditSettingsJson.Serialize(image.EditSettings));
            Assert.Equal(history, vm.HistoryEntries.Count);
        }
        finally
        {
            vm.ImageService.Previews.RenderGateAsync = null;
            vm.DiscardLocalsGesture();
            release.TrySetResult();
        }
    }

    [AvaloniaTheory]
    [InlineData("paint", "Paint")] [InlineData("erase", "Erase")]
    public async Task ClickingSelectedBrushModeKeepsItsToggleChecked(string mode, string label)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.AddBrushCommand.Execute(null);
        vm.BrushMode = mode;
        var section = new LocalsEditSection { DataContext = vm };
        var window = new Window { Content = section, Width = 250, Height = 600 };
        using var scope = new TestUiScope(window);
        var button = section.GetVisualDescendants().OfType<ToggleButton>().Single(b => Equals(b.Content, label));
        var history = vm.HistoryEntries.Count;
        Assert.True(button.IsChecked);
        var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Assert.Equal(mode, vm.BrushMode);
        Assert.True(button.IsChecked);
        Assert.Equal(history, vm.HistoryEntries.Count);
    }
}
