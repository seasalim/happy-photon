using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class CompactSliderEntryTests
{
    [AvaloniaFact]
    public async Task RapidCommitsRemainSeparateAndUndoIndividually()
    {
        await using var s = await Session.Create();
        s.Open(s.Slider("Contrast"));
        s.Window.KeyTextInput("35");
        s.Key(Key.Enter);
        s.Open(s.Slider("Exposure"));
        s.Window.KeyTextInput("0.35");
        s.Key(Key.Enter);
        await s.Drain();

        Assert.Equal(2, s.Steps);
        await s.Vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(35, s.Vm.Contrast);
        Assert.Equal(0, s.Vm.Exposure);
        await s.Vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(0, s.Vm.Contrast);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PhotoNavigationCommitsOnButtonFocusAndDiscardsOnCommand(bool command)
    {
        await using var s = await Session.Create();
        var outgoing = s.Vm.SelectedImage!;
        var slider = s.Slider("Contrast");
        s.Open(slider);
        s.Window.KeyTextInput("35");

        if (command) s.Vm.SelectNextImageCommand.Execute(null);
        else s.Click(s.Center(s.Window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NextImageButton")));

        await s.Vm.PendingHistoryLoadTask!.WaitAsync(TestWaits.Condition);
        await s.Drain();
        Assert.False(slider.IsEditingValue);
        Assert.NotSame(outgoing, s.Vm.SelectedImage);
        Assert.Equal(command ? 0 : 35, outgoing.EditSettings.Contrast);
        Assert.Equal(0, s.Vm.Contrast);
        Assert.Equal(0, s.Vm.SelectedImage!.EditSettings.Contrast);
        Assert.Equal(0, s.Steps);
        var saved = await s.Catalog.LoadEditHistoryAsync(outgoing.CatalogId);
        Assert.Equal(command ? 0 : 1, saved.Entries.Count(entry => entry.Label != "Original"));
    }

    [AvaloniaTheory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Escape)]
    public async Task EndingClearsFocusAndRightArrowNavigates(Key key)
    {
        await using var s = await Session.Create();
        var outgoing = s.Vm.SelectedImage;
        s.Open(s.Slider("Contrast"));
        s.Window.KeyTextInput("35");
        s.Key(key);
        Assert.Null(s.Window.FocusManager!.GetFocusedElement());
        s.Key(Key.Right);
        Assert.NotSame(outgoing, s.Vm.SelectedImage);
        await s.Drain();
    }

    [AvaloniaFact]
    public async Task TabAndClickAwayCommitAndShiftWheelCommitsBeforeStepping()
    {
        await using var s = await Session.Create();
        var slider = s.Slider("Contrast");
        s.Open(slider);
        s.Window.KeyTextInput("35");
        s.Key(Key.Tab);
        await s.Drain();
        Assert.Equal(35, slider.Value);
        Assert.False(slider.IsEditingValue);
        Assert.Equal(1, s.Steps);
        s.Open(slider);
        s.Window.KeyTextInput("40");
        s.Open(s.Slider("Exposure"));
        Assert.Equal(40, slider.Value);
        s.Key(Key.Escape);
        await s.Drain();
        Assert.Equal(2, s.Steps);
        s.Open(slider);
        s.Window.KeyTextInput("50");
        slider.WheelTimeProvider = s.Clock;
        s.Window.MouseWheel(s.Center(slider), new Vector(0, 1), RawInputModifiers.Shift);
        await s.Drain();
        Assert.False(slider.IsEditingValue);
        Assert.Equal(51, slider.Value);
        Assert.Equal(4, s.Steps);
    }

    [AvaloniaTheory]
    [InlineData("tool")]
    [InlineData("collapse")]
    [InlineData("detach")]
    [InlineData("disable")]
    public async Task TeardownDiscards(string reason)
    {
        await using var s = await Session.Create();
        var slider = s.Slider("Contrast");
        s.Open(slider);
        s.Window.KeyTextInput("35");

        switch (reason)
        {
            case "tool":
                await s.Vm.ToggleLocalsModeCommand.ExecuteAsync(null);
                break;

            case "collapse":
                s.Vm.AdjustmentsGroup.IsExpanded = false;
                break;

            case "detach":
                ((Panel)slider.Parent!).Children.Remove(slider);
                break;

            case "disable":
                slider.IsEnabled = false;
                break;
        }

        Dispatcher.UIThread.RunJobs();
        await s.Drain();
        Assert.False(slider.IsEditingValue);
        Assert.Equal(0, s.Vm.Contrast);
        Assert.Equal(0, s.Steps);
    }

    [AvaloniaFact]
    public async Task UnchangedKelvinKeepsAsShotAndClickingTrackCommits()
    {
        await using var s = await Session.Create();
        s.Open(s.Slider("Kelvin"));
        s.Key(Key.Enter);
        await s.Drain();
        Assert.Equal("As Shot", s.Vm.SelectedWhiteBalanceMode);
        Assert.Equal(0, s.Steps);
        var slider = s.Slider("Contrast");
        s.Open(slider);
        s.Window.KeyTextInput("35");
        s.Click(slider.TranslatePoint(new Point(100, 10), s.Window)!.Value);
        await s.Drain();
        Assert.False(slider.IsEditingValue);
        Assert.Equal(35, s.Vm.Contrast);
        Assert.Equal(1, s.Steps);
    }

    [AvaloniaFact]
    public async Task EntryShadowsWindowShortcutsAndZoomOptsOut()
    {
        await using var s = await Session.Create();
        var image = s.Vm.SelectedImage!;
        var slider = s.Slider("Contrast");
        var entry = s.Open(slider);

        foreach (var key in new[] { Key.D0, Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7,
                     Key.D8, Key.D9, Key.D, Key.G, Key.P, Key.X, Key.Delete })
        {
            s.Key(key);
            Assert.Same(image, s.Vm.SelectedImage);
            Assert.True(s.Vm.IsDevelopMode);
            Assert.False(s.Vm.IsLocalsMode);
            Assert.False(s.Vm.IsSpotsMode);
            Assert.Equal(0, image.Rating);
            Assert.Equal(ColorLabel.None, image.ColorLabel);
            Assert.Equal(0, (int)image.Flag);
            Assert.True(entry.IsFocused);
        }

        s.Window.KeyTextInput("abc");
        s.Key(Key.Enter);
        Assert.True(s.Vm.IsDevelopMode);
        Assert.Equal(0, s.Steps);
        s.Window.Width = 1800;
        var zoom = s.Slider("Zoom");
        s.Click(s.Center(zoom.FindControl<TextBlock>("ValueText")!));
        Assert.False(zoom.IsEditingValue);
        Assert.False(zoom.IsValueEntryEnabled);
    }
}
