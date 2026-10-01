using Avalonia.Headless.XUnit;
using Avalonia.Input;
using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class CompactSliderEntryTests
{
    [AvaloniaFact]
    public async Task PlatformKeyTypingCommitsOnceAndKeepsShortcutsInsideEntry()
    {
        await using var s = await Session.Create();
        var slider = s.Slider("Contrast");
        var entry = s.Open(slider);
        var image = s.Vm.SelectedImage!;
        var history = s.Steps;
        NumericEntryKeyInput.Type(s.Window, Key.D3, "3");
        NumericEntryKeyInput.Type(s.Window, Key.D5, "5");
        Assert.Equal("35", entry.Text);
        Assert.Equal(0, slider.Value);
        s.Key(Key.Enter);
        await s.Drain();
        Assert.Equal(35, slider.Value);
        Assert.Equal(history + 1, s.Steps);
        entry = s.Open(slider);

        foreach (var symbol in "0123456789dgpxr.-")
        {
            entry.SelectAll();
            NumericEntryKeyInput.Type(s.Window, NumericEntryKeyInput.KeyFor(symbol), symbol.ToString());
            Assert.Equal(symbol.ToString(), entry.Text);
            Assert.Same(image, s.Vm.SelectedImage);
            Assert.True(s.Vm.IsDevelopMode);
            Assert.False(s.Vm.IsCropMode || s.Vm.IsLocalsMode || s.Vm.IsSpotsMode);
            Assert.Equal(0, image.Rating);
            Assert.Equal(ColorLabel.None, image.ColorLabel);
            Assert.Equal(0, (int)image.Flag);
        }

        entry.SelectAll();
        s.Key(Key.Delete);
        Assert.Equal("", entry.Text);
        s.Key(Key.Escape);
        Assert.Equal(35, slider.Value);
        Assert.Equal(history + 1, s.Steps);
        s.Key(Key.D3);
        Assert.Equal(3, image.Rating);
    }
}
