using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

// Approved G3/G4 workloads, measured before the arrowhead change and enforced here.
public sealed class VisualsThumbGateTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task MarkCentres()
    {
        await WithPane(200, async (vm, panel, window, clock) =>
        {
            Assert.Equal(200, panel.Bounds.Width);
            var global = panel.GetVisualDescendants().OfType<StackPanel>()
                .Single(control => control.Classes.Contains("global-edits"));

            foreach (var label in new[] { "Exposure", "Kelvin", "Sharpen" })
            {
                var slider = global.GetVisualDescendants().OfType<CompactSlider>()
                    .Single(control => control.Label == label);
                slider.BringIntoView();
                Settle(window);

                foreach (var fraction in new[] { 0d, .5, 1d })
                {
                    var requested = slider.Minimum + fraction * (slider.Maximum - slider.Minimum);
                    slider.SetCurrentValue(CompactSlider.ValueProperty, requested);
                    Settle(window);
                    Assert.Equal(requested, slider.Value, 8);
                    var track = slider.FindControl<Grid>("TrackGrid")!;
                    var mark = slider.FindControl<Avalonia.Controls.Shapes.Path>("ThumbMark")!;
                    var normalized = (slider.Value - slider.Minimum) / (slider.Maximum - slider.Minimum);
                    var valueX = track.TranslatePoint(default, slider)!.Value.X + normalized * track.Bounds.Width;
                    var markX = mark.TranslatePoint(new Point(mark.Bounds.Width / 2, 0), slider)!.Value.X;
                    Assert.InRange(Math.Abs(markX - valueX), 0, .5);
                    output.WriteLine(FormattableString.Invariant(
                        $"G3 CompactSlider {label} fraction={fraction:R} value={slider.Value:R} trackWidth={track.Bounds.Width:R} valueX={valueX:R} markX={markX:R} distance={Math.Abs(markX - valueX):R}px rowHeight={slider.Bounds.Height:R}px"));
                }
            }

            await OpenRange(vm);
            Settle(window);
            var range = panel.GetVisualDescendants().OfType<DualRangeTrack>().Single();
            range.BringIntoView();
            Settle(window);
            var canvas = range.GetVisualDescendants().OfType<Canvas>().Single();
            var marks = canvas.Children.OfType<Avalonia.Controls.Shapes.Path>().ToArray();
            Assert.Equal(2, marks.Length);

            foreach (var value in new[] { 0d, 50d, 100d })
            {
                vm.LocalLuminanceUpper = 100;
                vm.LocalLuminanceLower = value;
                vm.LocalLuminanceUpper = value;
                Settle(window);
                Assert.Equal(value, range.Lower);
                Assert.Equal(value, range.Upper);

                for (var endpoint = 0; endpoint < marks.Length; endpoint++)
                {
                    var valueX = 6 + value / 100 * (canvas.Bounds.Width - 12);
                    var markX = marks[endpoint].TranslatePoint(new Point(marks[endpoint].Bounds.Width / 2, 0), canvas)!.Value.X;
                    Assert.InRange(Math.Abs(markX - valueX), 0, .5);
                    output.WriteLine(FormattableString.Invariant(
                        $"G3 DualRangeTrack endpoint={endpoint} value={value:R} trackWidth={canvas.Bounds.Width:R} valueX={valueX:R} markX={markX:R} distance={Math.Abs(markX - valueX):R}px"));
                }
            }
        });
    }

    [AvaloniaFact]
    public async Task RangeGestures()
    {
        await WithPane(260, async (vm, panel, window, clock) =>
        {
            await OpenRange(vm);
            Settle(window);
            var range = panel.GetVisualDescendants().OfType<DualRangeTrack>().Single();
            range.Width = 200;
            range.BringIntoView();
            Settle(window);
            var canvas = range.GetVisualDescendants().OfType<Canvas>().Single();
            Assert.Equal(200, canvas.Bounds.Width);
            Assert.True(range.IsEffectivelyEnabled && range.IsEffectivelyVisible);
            await SetRange(30, 70);
            var before = vm.HistoryEntries.Count(entry => entry.Label == "Luminance Range");
            Drag(6 + 30d / 100 * 188 - 8, -20);
            await Drain(vm, clock);
            var added = vm.HistoryEntries.Count(entry => entry.Label == "Luminance Range") - before;
            var moved = (range.Lower != 30, range.Upper != 70) switch
            {
                (false, false) => "neither",
                (true, false) => "Lower",
                (false, true) => "Upper",
                _ => "both"
            };
            output.WriteLine(FormattableString.Invariant(
                $"G4a moved={moved} Lower={range.Lower:R} Upper={range.Upper:R} Luminance Range steps added={added}"));
            Assert.Equal("Lower", moved);
            Assert.Equal(1, added);
            await SetRange(50, 50);
            Drag(100, -20);
            await Drain(vm, clock);
            output.WriteLine(FormattableString.Invariant($"G4b Lower={range.Lower:R} Upper={range.Upper:R}"));

            Assert.True(range.Lower < 50);
            Assert.Equal(50, range.Upper);

            var enable = panel.GetVisualDescendants().OfType<CheckBox>()
                .Single(control => control.Name == "LocalLuminanceEnabled");
            enable.BringIntoView();
            Settle(window);
            Assert.True(enable.Focus());
            var endpointStops = 0;
            var entered = false;

            for (var tab = 0; tab < 20; tab++)
            {
                window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                Settle(window);
                var focused = window.FocusManager!.GetFocusedElement() as Control;
                var inside = focused is not null && focused.GetVisualAncestors().Contains(range);

                if (!inside && entered) break;

                entered |= inside;
                var name = focused is null ? "null" : AutomationProperties.GetName(focused);
                output.WriteLine($"G4c tab={tab + 1} control={focused?.GetType().Name} name={name} insideRange={inside}");

                if (inside && name is "Luminance lower limit" or "Luminance upper limit")
                {
                    endpointStops++;
                    var lower = range.Lower;
                    var upper = range.Upper;
                    window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
                    window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
                    Assert.Equal(lower + (name == "Luminance lower limit" ? 1 : 0), range.Lower, 8);
                    Assert.Equal(upper + (name == "Luminance upper limit" ? 1 : 0), range.Upper, 8);
                }
            }

            Assert.True(entered, "Tab traversal must enter the range control to measure its focus stops.");
            Assert.Equal(2, endpointStops);
            output.WriteLine($"G4c endpointFocusStops={endpointStops}");

            async Task SetRange(double lower, double upper)
            {
                vm.OnSliderEditStarted();
                vm.LocalLuminanceUpper = 100;
                vm.LocalLuminanceLower = lower;
                vm.LocalLuminanceUpper = upper;
                vm.OnSliderEditCompleted("Luminance Range");

                await Drain(vm, clock);
                Settle(window);
                Assert.Equal(lower, range.Lower);
                Assert.Equal(upper, range.Upper);
            }

            void Drag(double x, double delta)
            {
                range.BringIntoView();
                Settle(window);
                var start = canvas.TranslatePoint(new Point(x, 12), window)!.Value;
                var end = start + new Vector(delta, 0);

                window.MouseMove(start);
                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(end, RawInputModifiers.LeftMouseButton);
                window.MouseUp(end, MouseButton.Left);
                Settle(window);
            }
        });
    }

    private static async Task WithPane(double width,
        Func<MainWindowViewModel, DevelopEditPanel, Window, TestTimeProvider, Task> observe)
    {
        using var fixture = new CatalogVmFixture("visuals-thumb-gate");
        using var catalog = await fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = fixture.CreateViewModel(catalog, new LocalTestLoader(raw: true),
            _ => Task.CompletedTask, new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            timeProvider: clock);

        vm.IsDevelopMode = true;
        var image = new ImageFile(fixture.Path("synthetic.dng"));
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;

        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);

        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window { Width = width, Height = 2400, Content = panel, CanResize = false };
        using var scope = new TestUiScope(window, ThemeVariant.Dark);
        Settle(window);
        await observe(vm, panel, window, clock);
    }

    private static async Task OpenRange(MainWindowViewModel vm)
    {
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        await vm.ToggleLocalLuminanceCommand.ExecuteAsync(null);
        vm.IsLocalLuminanceExpanded = true;
    }

    private static async Task Drain(MainWindowViewModel vm, TestTimeProvider clock)
    {
        clock.Advance(TimeSpan.FromMilliseconds(200));

        if (vm.PendingPreviewDebounceTask is { } preview)
        {
            await preview.WaitAsync(TestWaits.Condition);
        }

        if (vm.PendingHistoryCommitTask is { } history)
        {
            await history.WaitAsync(TestWaits.Condition);
        }
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
    }
}
