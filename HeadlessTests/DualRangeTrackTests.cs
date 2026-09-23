using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DualRangeTrackTests
{
    [AvaloniaFact]
    public void EndpointsUseKeyboardAndTypedTransactionsAndDiscardStaleInput()
    {
        var track = new DualRangeTrack { Lower = 47, Upper = 50, EditIdentity = new object() };
        var window = new Window { Width = 300, Height = 150, Content = track };
        using var scope = new TestUiScope(window);
        var starts = 0; var ends = 0;
        track.AddHandler(CompactSlider.DragStartedEvent, (_, _) => starts++);
        track.AddHandler(CompactSlider.DragCompletedEvent, (_, _) => ends++);
        var buttons = track.GetVisualDescendants().OfType<Button>().ToArray();
        Assert.Equal(2, buttons.Length);
        Assert.All(buttons, b => Assert.Contains("Luminance", AutomationProperties.GetName(b)));
        buttons[0].Focus(); window.KeyPress(Key.Right, RawInputModifiers.Shift, PhysicalKey.None, null);
        Assert.Equal(50, track.Lower); Assert.Equal(50, track.Upper);
        buttons[1].Focus(); window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.None, null);
        Assert.Equal(50, track.Upper);
        var entries = track.GetVisualDescendants().OfType<TextBox>().ToArray();
        Assert.Equal(["Lower", "Upper"], track.GetVisualDescendants().OfType<TextBlock>()
            .Where(label => label.Text is "Lower" or "Upper").Select(label => label.Text));
        Assert.All(entries, entry => Assert.Equal(48, entry.Bounds.Width));
        var lowerRight = entries[0].TranslatePoint(new(entries[0].Bounds.Width, 0), track)!.Value.X;
        var upperLeft = entries[1].TranslatePoint(default, track)!.Value.X;
        Assert.True(upperLeft - lowerRight >= 12);
        entries[0].Focus(); entries[0].Text = "20";
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        Assert.Equal(20, track.Lower);
        entries[0].Focus(); entries[0].Text = "35"; window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        Assert.Equal("20", entries[0].Text); Assert.Equal(20, track.Lower);
        entries[0].Focus(); entries[0].Text = "45"; track.EditIdentity = new object(); buttons[0].Focus();
        Assert.Equal(20, track.Lower);
        var beforeCancel = starts;
        track.Lower = 20.25;
        entries[0].Focus(); entries[0].Text = "40";
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        Assert.True(buttons[0].IsFocused);
        Assert.Equal(20.25, track.Lower);
        Assert.Equal(beforeCancel, starts);
        Assert.Equal(starts, ends);
    }
}
