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
    public async Task TypedTabKeepsPlatformTextOutOfNextField()
    {
        await using var s = await Session.Create();
        var contrast = s.Slider("Contrast");
        var highlights = s.Slider("Highlights");
        var entry = s.Open(contrast);

        NumericEntryKeyInput.Type(s.Window, Key.D3, "3");
        NumericEntryKeyInput.Type(s.Window, Key.D5, "5");
        Assert.Equal("35", entry.Text);

        NumericEntryKeyInput.Press(s.Window, Key.Tab, "\t", expectTextInput: false);
        Assert.Equal(35, contrast.Value);
        AssertOpenField(highlights);

        entry = highlights.FindControl<NumericEntryBox>("ValueEntry")!;
        NumericEntryKeyInput.Type(s.Window, Key.D1, "1");
        NumericEntryKeyInput.Type(s.Window, Key.D2, "2");
        Assert.Equal("12", entry.Text);
        PressValueKey(s, Key.Enter);
        await s.Drain();

        Assert.Equal(35, s.Vm.Contrast);
        Assert.Equal(12, s.Vm.Highlights);
        Assert.Equal(2, s.Steps);
        var saved = await s.Catalog.LoadEditHistoryAsync(s.Vm.SelectedImage!.CatalogId);
        Assert.Equal(2, saved.Entries.Count(item => item.Label != "Original"));
    }

    [AvaloniaTheory]
    [InlineData("Exposure", "0.00", Key.Up, false, .05)]
    [InlineData("Exposure", "0.00", Key.Up, true, .50)]
    [InlineData("Exposure", "0.00", Key.Down, false, -.05)]
    [InlineData("Exposure", "0.00", Key.Down, true, -.50)]
    [InlineData("Temperature", "5500", Key.Up, false, 5550)]
    [InlineData("Temperature", "5500", Key.Up, true, 6000)]
    [InlineData("Temperature", "5500", Key.Down, false, 5450)]
    [InlineData("Temperature", "5500", Key.Down, true, 5000)]
    [InlineData("Contrast", "35", Key.Up, false, 36)]
    [InlineData("Contrast", "abc", Key.Up, false, 1)]
    [InlineData("Contrast", "", Key.Down, false, -1)]
    [InlineData("Contrast", "100", Key.Up, false, 100)]
    [InlineData("Contrast", "-100", Key.Down, false, -100)]
    public async Task FieldArrowsApplyDisplayedStepsAndEscapeKeepsThem(
        string label, string text, Key key, bool shift, double expected)
    {
        await using var s = await Session.Create();
        var slider = s.Slider(label);
        var entry = s.Open(slider);
        TypeValue(s, text);
        PressValueKey(s, key, shift ? RawInputModifiers.Shift : RawInputModifiers.None);

        Assert.True(slider.IsEditingValue);
        Assert.True(entry.IsFocused);
        Assert.Equal(expected, slider.ValueToDisplay?.Invoke(slider.Value) ?? slider.Value, 7);
        Assert.Equal(slider.FindControl<TextBlock>("ValueText")!.Text!.TrimEnd('K'), entry.Text);
        Assert.Equal(entry.Text, entry.SelectedText);
        TypeValue(s, "42");
        PressValueKey(s, Key.Escape);
        await s.Drain();
        Assert.Equal(expected, slider.ValueToDisplay?.Invoke(slider.Value) ?? slider.Value, 7);
        Assert.False(slider.IsEditingValue);
    }

    [AvaloniaFact]
    public async Task FieldArrowWrapsLikeTypedCommit()
    {
        await using var s = await Session.Create();
        var slider = s.Slider("Contrast");
        slider.WrapValue = true;
        s.Open(slider);
        TypeValue(s, "100");
        PressValueKey(s, Key.Up);
        Assert.Equal(-99, slider.Value);
        Assert.True(slider.IsEditingValue);
        PressValueKey(s, Key.Escape);
        await s.Drain();
    }

    [AvaloniaFact]
    public async Task TabOpensAdjacentFieldsAndSkipsCollapsedGroupsIntoView()
    {
        await using var s = await Session.Create();
        var contrast = s.Slider("Contrast");
        var highlights = s.Slider("Highlights");
        s.Open(contrast);
        TypeValue(s, "35");
        PressValueKey(s, Key.Tab);
        Assert.Equal(35, contrast.Value);
        Assert.False(contrast.IsEditingValue);
        AssertOpenField(highlights);
        PressValueKey(s, Key.Tab, RawInputModifiers.Shift);
        AssertOpenField(contrast);
        PressValueKey(s, Key.Escape);

        var presence = s.Slider("Texture").GetVisualAncestors().OfType<DevelopGroup>().First();
        presence.IsExpanded = false;
        var blacks = s.Slider("Blacks");
        s.Open(blacks);
        var hue = s.Slider("Hue");
        var scroll = blacks.GetVisualAncestors().OfType<ScrollViewer>().First();
        scroll.Offset = new Vector(0, 0);
        Dispatcher.UIThread.RunJobs();
        var before = scroll.Offset.Y;
        PressValueKey(s, Key.Tab);
        AssertOpenField(hue);
        var field = hue.FindControl<NumericEntryBox>("ValueEntry")!;
        ShowcaseTestHelper.Settle(() =>
            VisibleBounds(field).Y == 0 && VisibleBounds(field).Height == field.Bounds.Height,
            "Tab destination scrolled into view");
        Assert.True(scroll.Offset.Y > before);
        PressValueKey(s, Key.Escape);
        await s.Drain();
    }

    [AvaloniaTheory]
    [InlineData(0, "-10", false, "Midpoint")]
    [InlineData(-10, "0", false, "Grain")]
    [InlineData(0, "-10", true, "Chroma NR")]
    [InlineData(-10, "0", true, "Chroma NR")]
    public async Task TabUsesEligibilityAfterVignetteCommit(
        int initial, string text, bool backwards, string destination)
    {
        await using var s = await Session.Create(new EditSettings { Effects = new EffectsSettings { Vignette = initial } });
        var vignette = s.Slider("Vignette");
        var midpoint = s.Slider("Midpoint");
        var target = s.Slider(destination);
        Assert.Equal(initial != 0, midpoint.IsEffectivelyEnabled);
        s.Open(vignette);
        TypeValue(s, text);
        PressWithoutDispatcherJobs(s.Window, Key.Tab, "\t", backwards ? RawInputModifiers.Shift : RawInputModifiers.None);

        Assert.Equal(initial == 0 ? -10 : 0, s.Vm.Vignette);
        Assert.Equal(initial == 0, midpoint.IsEffectivelyEnabled);
        Assert.False(vignette.IsEditingValue);
        AssertOpenField(target);
        PressValueKey(s, Key.Escape);
        await s.Drain();
        Assert.Equal(1, s.Steps);

        s.Open(s.Slider("Grain"));
        PressValueKey(s, Key.Tab, RawInputModifiers.Shift);
        AssertOpenField(initial == 0 ? midpoint : vignette);
        PressValueKey(s, Key.Escape);
        await s.Drain();
        Assert.Equal(1, s.Steps);
    }

    [AvaloniaFact]
    public async Task TabSkipsDisabledHiddenAndOptedOutSliders()
    {
        await using var s = await Session.Create();
        var contrast = s.Slider("Contrast");
        var saturation = s.Window.GetVisualDescendants().OfType<CompactSlider>().Single(slider => slider.Name == "SaturationSlider");
        var vibrance = s.Slider("Vibrance");
        var shadows = s.Slider("Shadows");
        saturation.IsEnabled = false;
        vibrance.IsVisible = false;
        shadows.IsValueEntryEnabled = false;
        s.Open(contrast);
        PressValueKey(s, Key.Tab);
        AssertOpenField(s.Slider("Highlights"));
        Assert.False(saturation.IsEditingValue || vibrance.IsEditingValue || shadows.IsEditingValue);
        PressValueKey(s, Key.Escape);
        await s.Drain();
    }

    [AvaloniaFact]
    public async Task ThreeTabCommitsMakeThreeIndividuallyUndoableSteps()
    {
        await using var s = await Session.Create();
        s.Open(s.Slider("Contrast"));

        foreach (var text in new[] { "35", "20", "10" })
        {
            TypeValue(s, text);
            PressValueKey(s, Key.Tab);
        }

        PressValueKey(s, Key.Escape);
        await s.Drain();
        Assert.Equal(3, s.Steps);
        Assert.Equal(35, s.Vm.Contrast);
        Assert.Equal(20, s.Vm.Highlights);
        Assert.Equal(10, s.Vm.Shadows);
        var saved = await s.Catalog.LoadEditHistoryAsync(s.Vm.SelectedImage!.CatalogId);
        Assert.Equal(3, saved.Entries.Count(entry => entry.Label != "Original"));
        await s.Vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(0, s.Vm.Shadows);
        Assert.Equal(20, s.Vm.Highlights);
        await s.Vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(0, s.Vm.Highlights);
        Assert.Equal(35, s.Vm.Contrast);
        await s.Vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(0, s.Vm.Contrast);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TabAtScrollHostBoundaryCommitsWithoutWrapping(bool backwards)
    {
        await using var s = await Session.Create();
        var scroll = s.Slider("Contrast").GetVisualAncestors().OfType<ScrollViewer>().First();
        var sliders = scroll.GetVisualDescendants().OfType<CompactSlider>()
            .Where(slider => slider.IsEffectivelyVisible && slider.IsEffectivelyEnabled && slider.IsValueEntryEnabled)
            .ToArray();
        var last = backwards ? sliders[0] : sliders[^1];
        s.Open(last);
        TypeValue(s, backwards ? "5600" : "10");
        PressValueKey(s, Key.Tab, backwards ? RawInputModifiers.Shift : RawInputModifiers.None);
        await s.Drain();
        Assert.Equal(1, s.Steps);
        Assert.DoesNotContain(s.Window.GetVisualDescendants().OfType<CompactSlider>(), slider => slider.IsEditingValue);
        Assert.Null(s.Window.FocusManager!.GetFocusedElement());
    }

    [AvaloniaFact]
    public async Task FieldArrowBurstMatchesFocusedSliderAtDebounceBoundary()
    {
        var focused = await MeasureArrowBurst(false);
        var field = await MeasureArrowBurst(true);
        Assert.Equal(focused, field);
        Assert.Equal((1, 1, 1, 1, 5, 5, "Original:0;Contrast +5 (+5):5"), field);
        baselineOutput.WriteLine($"G3b focused={focused}; field={field}");
    }

    private static async Task<(int History, int Requests, int Renders, int Saves, int Starts, int Ends, string Grouping)>
        MeasureArrowBurst(bool field)
    {
        await using var s = await Session.Create();
        var slider = s.Slider("Contrast");
        s.Reveal(slider);
        await s.Drain();
        if (field) s.Open(slider);
        else Assert.True(slider.Focus());

        var history = s.Steps;
        var requests = 0;
        var renders = 0;
        var saves = 0;
        var starts = 0;
        var ends = 0;
        s.Vm.ImageService.Previews.RenderStarted += () => Interlocked.Increment(ref renders);
        s.Vm.ImageService.Previews.SourceWorkGateAsync = () =>
        {
            Interlocked.Increment(ref requests);

            return Task.CompletedTask;
        };
        s.Catalog.EditHistoryWriteGateAsync = () =>
        {
            Interlocked.Increment(ref saves);

            return Task.CompletedTask;
        };
        slider.DragStarted += (_, _) => starts++;
        slider.DragCompleted += (_, _) => ends++;

        for (var press = 0; press < 5; press++)
        {
            PressValueKey(s, Key.Up);
        }

        Assert.Equal(5, slider.Value);
        Assert.Equal((0, 0, 0, 0), (s.Steps - history, requests, renders, saves));
        s.Clock.Advance(TimeSpan.FromMilliseconds(149));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((0, 0, 0, 0), (s.Steps - history, requests, renders, saves));
        s.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Dispatcher.UIThread.RunJobs();

        if (s.Vm.PendingPreviewDebounceTask is { } preview)
        {
            await preview.WaitAsync(TestWaits.Condition);
        }

        if (s.Vm.PendingHistoryCommitTask is { } commit)
        {
            await commit.WaitAsync(TestWaits.Condition);
        }

        var saved = await s.Catalog.LoadEditHistoryAsync(s.Vm.SelectedImage!.CatalogId);
        var grouping = string.Join(";", saved.Entries.OrderBy(entry => entry.Sequence)
            .Select(entry => $"{entry.Label}:{entry.Settings.Contrast}"));
        if (field) PressValueKey(s, Key.Escape);

        return (s.Steps - history, requests, renders, saves, starts, ends, grouping);
    }

    private static void AssertOpenField(CompactSlider slider)
    {
        Assert.True(slider.IsEditingValue, slider.Label);
        var entry = slider.FindControl<NumericEntryBox>("ValueEntry")!;
        Assert.True(entry.IsFocused);
        Assert.Equal(entry.Text, entry.SelectedText);
    }

    private static void TypeValue(Session s, string text)
    {
        PressValueKey(s, Key.Delete);

        foreach (var symbol in text)
        {
            NumericEntryKeyInput.Type(s.Window, NumericEntryKeyInput.KeyFor(symbol), symbol.ToString());
        }
    }

    private static void PressValueKey(Session s, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        s.Window.KeyPress(key, modifiers, PhysicalKey.None, null);
        s.Window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }
}
