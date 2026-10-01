using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class CompactSliderEntryTests(ITestOutputHelper baselineOutput)
{
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task BaselineFocusedArrowBurst(int run)
    {
        RequireKeyboardBaseline();
        await using var s = await Session.Create();
        var slider = s.Slider("Contrast");
        s.Reveal(slider);
        await s.Drain();
        Assert.True(slider.Focus());
        var history = s.Steps;
        var generation = s.Vm.LatestPreviewOutcomeGeneration;
        var renders = 0;
        var requests = 0;
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

        void Report(string stage) => baselineOutput.WriteLine(
            $"G3b run={run} stage={stage} value={slider.Value} history={s.Steps - history} " +
            $"reservations={s.Vm.LatestPreviewOutcomeGeneration - generation} requests={requests} renders={renders} " +
            $"saves={saves} starts={starts} ends={ends}");

        for (var press = 0; press < 5; press++)
        {
            BaselinePress(s, Key.Up);
        }

        Report("0ms");
        s.Clock.Advance(TimeSpan.FromMilliseconds(149));
        Dispatcher.UIThread.RunJobs();
        Report("149ms");
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

        Report("150ms-drained");
        var saved = await s.Catalog.LoadEditHistoryAsync(s.Vm.SelectedImage!.CatalogId);
        baselineOutput.WriteLine(
            $"G3b run={run} saved-history=" + string.Join(";", saved.Entries.OrderBy(e => e.Sequence)
                .Select(e => $"{e.Sequence}:{e.Label}:Contrast={e.Settings.Contrast}")));
        Assert.Equal(5, slider.Value);
        Assert.Equal(5, saved.Entries.OrderBy(e => e.Sequence).Last().Settings.Contrast);
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task BaselineOpenFieldKeys(int run)
    {
        RequireKeyboardBaseline();

        foreach (var key in new[] { Key.Up, Key.Down, Key.Tab })
        {
            foreach (var modifiers in new[] { RawInputModifiers.None, RawInputModifiers.Shift })
            {
                await using var s = await Session.Create();
                var slider = s.Slider("Contrast");
                var entry = s.Open(slider);
                var history = s.Steps;
                NumericEntryKeyInput.Type(s.Window, Key.D3, "3");
                NumericEntryKeyInput.Type(s.Window, Key.D5, "5");
                Assert.Equal("35", entry.Text);
                BaselinePress(s, key, modifiers);
                await s.Drain();
                var focused = s.Window.FocusManager!.GetFocusedElement();
                var focus = focused is CompactSlider next ? $"CompactSlider[{next.Label}]"
                    : focused is Control control ? $"{control.GetType().Name}[{control.Name}]"
                    : focused?.GetType().Name ?? "none";
                var open = s.Window.GetVisualDescendants().OfType<CompactSlider>()
                    .Where(control => control.IsEditingValue).Select(control => control.Label);
                baselineOutput.WriteLine(
                    $"FIELD run={run} key={modifiers}+{key} text={entry.Text} value={slider.Value} " +
                    $"history={s.Steps - history} editing={slider.IsEditingValue} focus={focus} " +
                    $"open=[{string.Join(",", open)}] selection={entry.SelectionStart}:{entry.SelectionEnd}");

                if (slider.IsEditingValue)
                {
                    BaselinePress(s, Key.Escape);
                    await s.Drain();
                    baselineOutput.WriteLine(
                        $"FIELD run={run} after-Escape key={modifiers}+{key} value={slider.Value} " +
                        $"history={s.Steps - history} editing={slider.IsEditingValue}");
                }
            }
        }
    }

    private static void BaselinePress(Session s, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        s.Window.KeyPress(key, modifiers, PhysicalKey.None, null);
        s.Window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static void RequireKeyboardBaseline() => Assert.SkipUnless(
        Environment.GetEnvironmentVariable("HAPPY_PHOTON_SLIDER_KEYBOARD_BASELINE") == "1",
        "Set HAPPY_PHOTON_SLIDER_KEYBOARD_BASELINE=1 to measure pre-WP2 keyboard behavior.");
}
