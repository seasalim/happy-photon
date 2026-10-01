using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task RangeEntryPlatformKeyTypingCommitsAndShadowsShortcuts(int endpoint)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await PrepareRangeEntry(vm, catalog);
        var window = new MainWindow { Width = 1200, Height = 900 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var entry = FocusRangeEntry(window, endpoint);
        var image = vm.SelectedImage!;
        var count = vm.HistoryEntries.Count;
        entry.SelectAll();
        NumericEntryKeyInput.Type(window, Key.D3, "3");
        NumericEntryKeyInput.Type(window, Key.D5, "5");
        Assert.Equal("35", entry.Text);
        PressRangeKey(window, Key.Enter);
        clock.Advance(TimeSpan.FromMilliseconds(200));
        await vm.PendingPreviewDebounceTask!.WaitAsync(TestWaits.Condition);
        Assert.Equal(35, endpoint == 0 ? vm.LocalLuminanceLower : vm.LocalLuminanceUpper);
        Assert.Equal(count + 1, vm.HistoryEntries.Count);
        entry = FocusRangeEntry(window, endpoint);

        foreach (var symbol in "0123456789dgpxr.-")
        {
            entry.SelectAll();
            NumericEntryKeyInput.Type(window, NumericEntryKeyInput.KeyFor(symbol), symbol.ToString());
            Assert.Equal(symbol.ToString(), entry.Text);
            Assert.Same(image, vm.SelectedImage);
            Assert.True(vm.IsDevelopMode && vm.IsLocalsMode);
            Assert.False(vm.IsCropMode || vm.IsSpotsMode);
            Assert.Equal(0, image.Rating);
            Assert.Equal(ColorLabel.None, image.ColorLabel);
            Assert.Equal(0, (int)image.Flag);
        }

        entry.SelectAll();
        PressRangeKey(window, Key.Delete);
        Assert.Equal("", entry.Text);
        PressRangeKey(window, Key.Escape);
        Assert.Equal(35, endpoint == 0 ? vm.LocalLuminanceLower : vm.LocalLuminanceUpper);
        Assert.Equal(count + 1, vm.HistoryEntries.Count);
    }
}
