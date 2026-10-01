using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Reactive;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class CompactSliderEntryTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImmediateDigitAfterTabOnlyRunsShortcutAtEndOfList(bool endOfList)
    {
        await using var s = await Session.Create();
        var contrast = s.Slider("Contrast");
        var scroll = contrast.GetVisualAncestors().OfType<ScrollViewer>().First();
        var sliders = scroll.GetVisualDescendants().OfType<CompactSlider>()
            .Where(slider => slider.IsEffectivelyVisible && slider.IsEffectivelyEnabled && slider.IsValueEntryEnabled)
            .ToArray();
        var source = endOfList ? sliders[^1] : contrast;
        var highlights = s.Slider("Highlights");
        var entry = s.Open(source);
        var image = s.Vm.SelectedImage!;
        NumericEntryKeyInput.Type(s.Window, Key.D3, "3");
        NumericEntryKeyInput.Type(s.Window, Key.D5, "5");
        Assert.Equal("35", entry.Text);
        var jobRan = false;
        Dispatcher.UIThread.Post(() => jobRan = true, DispatcherPriority.Input);
        PressWithoutDispatcherJobs(s.Window, Key.Tab, "\t");
        PressWithoutDispatcherJobs(s.Window, Key.D1, "1");
        Assert.False(jobRan);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(35, source.Value);
        Assert.Equal(endOfList ? 1 : 0, image.Rating);
        Assert.Equal(ColorLabel.None, image.ColorLabel);
        Assert.Equal(0, (int)image.Flag);
        Assert.Same(image, s.Vm.SelectedImage);
        Assert.True(s.Vm.IsDevelopMode);
        Assert.False(s.Vm.IsCropMode || s.Vm.IsLocalsMode || s.Vm.IsSpotsMode);

        if (endOfList)
        {
            Assert.Null(s.Window.FocusManager!.GetFocusedElement());
            Assert.DoesNotContain(sliders, slider => slider.IsEditingValue);
        }
        else
        {
            Assert.Equal(35, s.Vm.Contrast);
            Assert.True(highlights.IsEditingValue);
            var next = highlights.FindControl<NumericEntryBox>("ValueEntry")!;
            Assert.True(next.IsFocused);
            Assert.Equal("1", next.Text);
            PressValueKey(s, Key.Escape);
        }

        await s.Drain();
    }

    private static void PressWithoutDispatcherJobs(
        Window window, Key key, string symbol, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        // HeadlessWindowExtensions pumps jobs around each event. Call its internal
        // platform interface directly to preserve real key bindings without pumping.
        var platform = window.PlatformImpl!;
        var headless = platform.GetType().GetInterface("Avalonia.Headless.IHeadlessWindow")!;
        KeyEventArgs? down = null;
        using var subscription = InputElement.KeyDownEvent.RouteFinished.Subscribe(new AnonymousObserver<RoutedEventArgs>(input =>
        {
            if (input is KeyEventArgs { Route: RoutingStrategies.Bubble } args) down = args;
        }));
        headless.GetMethod("KeyPress")!.Invoke(platform, [key, modifiers, PhysicalKey.None, symbol]);
        Assert.NotNull(down);
        if (!down.Handled) headless.GetMethod("TextInput")!.Invoke(platform, [symbol]);

        headless.GetMethod("KeyRelease")!.Invoke(platform, [key, modifiers, PhysicalKey.None, symbol]);
    }

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
