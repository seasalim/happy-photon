using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task RangeEntryTypingOwnsWindowShortcuts(int endpoint)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await PrepareRangeEntry(vm, catalog);
        var window = new MainWindow { Width = 1200, Height = 900 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var entry = FocusRangeEntry(window, endpoint);
        var image = vm.SelectedImage!;
        foreach (var digit in "0123456789")
        {
            entry.SelectAll();
            PressRangeKey(window, Key.D0 + (digit - '0'));
            window.KeyTextInput(digit.ToString());
            Assert.Equal(digit.ToString(), entry.Text);
            Assert.Equal(0, image.Rating);
            Assert.Equal(ColorLabel.None, image.ColorLabel);
        }
        foreach (var key in new[] { Key.P, Key.X, Key.U, Key.G, Key.D, Key.E, Key.F, Key.R, Key.L })
        {
            PressRangeKey(window, key);
            window.KeyTextInput(key.ToString().ToLowerInvariant());
            Assert.True(vm.IsDevelopMode && vm.IsLocalsMode);
            Assert.False(vm.IsFullScreenMode || vm.IsCropMode);
            Assert.Equal(0, (int)image.Flag);
        }
        entry.Text = "47";
        window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.None, null);
        window.KeyRelease(Key.A, RawInputModifiers.Control, PhysicalKey.None, null);
        Assert.Equal("47", entry.SelectedText);
        entry.SelectionStart = entry.SelectionEnd = entry.CaretIndex = 0;
        PressRangeKey(window, Key.Delete);
        Assert.Equal("7", entry.Text);
        entry.CaretIndex = 1;
        PressRangeKey(window, Key.Back);
        Assert.Equal("", entry.Text);
        Assert.Same(image, vm.SelectedImage);
        PressRangeKey(window, Key.Escape);
        PressRangeKey(window, Key.D3);
        Assert.Equal(3, image.Rating); // Workspace shortcuts resume outside the entry.
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task RangeEntryEnterCommitsExactlyOneHistoryStep(int endpoint)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await PrepareRangeEntry(vm, catalog);
        var window = new MainWindow { Width = 1200, Height = 900 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var entry = FocusRangeEntry(window, endpoint);
        var track = entry.GetVisualAncestors().OfType<DualRangeTrack>().Single();
        var starts = 0; var ends = 0;
        track.AddHandler(CompactSlider.DragStartedEvent, (_, _) => starts++);
        track.AddHandler(CompactSlider.DragCompletedEvent, (_, _) => ends++);
        var count = vm.HistoryEntries.Count;
        entry.SelectAll(); window.KeyTextInput("47");
        PressRangeKey(window, Key.Enter);
        Assert.Equal(47, endpoint == 0 ? vm.LocalLuminanceLower : vm.LocalLuminanceUpper);
        Assert.False(entry.IsFocused);
        Assert.True(vm.IsLocalsMode);
        clock.Advance(TimeSpan.FromMilliseconds(200));
        await vm.PendingPreviewDebounceTask!;
        Assert.Equal(count + 1, vm.HistoryEntries.Count);
        Assert.Equal("Luminance Range", vm.HistoryEntries[0].Label);
        Assert.Equal(1, starts); Assert.Equal(1, ends);
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(endpoint == 0 ? 0 : 100, endpoint == 0 ? vm.LocalLuminanceLower : vm.LocalLuminanceUpper);
        await vm.RedoCommand.ExecuteAsync(null);
        Assert.Equal(47, endpoint == 0 ? vm.LocalLuminanceLower : vm.LocalLuminanceUpper);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task RangeEntryEscapeDiscardsDraftAcrossReopenAndFocusChange(int endpoint)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await PrepareRangeEntry(vm, catalog);
        var window = new MainWindow { Width = 1200, Height = 900 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var entry = FocusRangeEntry(window, endpoint);
        var track = entry.GetVisualAncestors().OfType<DualRangeTrack>().Single();
        var starts = 0;
        track.AddHandler(CompactSlider.DragStartedEvent, (_, _) => starts++);
        var count = vm.HistoryEntries.Count;
        var expected = entry.Text;
        entry.SelectAll(); window.KeyTextInput("47");
        PressRangeKey(window, Key.Escape);
        Assert.True(vm.IsLocalsMode);
        Assert.Equal(expected, entry.Text);
        Assert.False(entry.IsFocused);
        Assert.Equal(0, starts);
        vm.CloseLocalsCommand.Execute(null);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        entry = FocusRangeEntry(window, endpoint);
        Assert.Equal(expected, entry.Text);
        FocusRangeEntry(window, 1 - endpoint);
        clock.Advance(TimeSpan.FromMilliseconds(200));
        if (vm.PendingPreviewDebounceTask is { } pending) await pending;
        Assert.Equal(count, vm.HistoryEntries.Count);
        Assert.Equal(0, starts);
        Assert.Equal(endpoint == 0 ? 0 : 100, endpoint == 0 ? vm.LocalLuminanceLower : vm.LocalLuminanceUpper);
    }

    private async Task PrepareRangeEntry(MainWindowViewModel vm, HappyPhoton.Services.CatalogService catalog)
    {
        await Prepare(vm, catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        await vm.ToggleLocalLuminanceCommand.ExecuteAsync(null);
        vm.IsLocalLuminanceExpanded = true;
    }

    private static TextBox FocusRangeEntry(MainWindow window, int endpoint)
    {
        var track = window.GetVisualDescendants().OfType<DualRangeTrack>().Single();
        var entry = track.GetVisualDescendants().OfType<TextBox>().ElementAt(endpoint);
        entry.BringIntoView(); Dispatcher.UIThread.RunJobs();
        Assert.True(entry.Focus());
        return entry;
    }

    private static void PressRangeKey(MainWindow window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
    }
}
