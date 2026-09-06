using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaFact]
    public async Task ExpandedGeometrySwitchesBetweenRadialAndMinimumWidthLinear()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        var linear = vm.SelectedLocal!;
        linear.Feather = .001;
        vm.AddRadialCommand.Execute(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        var radial = vm.SelectedLocal!;
        vm.IsLocalGeometryExpanded = true;
        var section = new LocalsEditSection { DataContext = vm };
        var window = new Window { Content = section, Width = 250, Height = 800 };
        using var scope = new TestUiScope(window);
        var slider = section.FindControl<CompactSlider>("LocalWidthSlider")!;
        Assert.True(slider.IsEffectivelyVisible);
        Assert.True(slider.FindControl<Grid>("TrackGrid")!.Bounds.Width > 0);
        Assert.Equal((.2, 50d), (slider.Minimum, slider.Value));
        foreach (var local in new[] { linear, radial })
        {
            Assert.Null(Record.Exception(() => vm.SelectedLocal = local));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(local.IsRadial ? (.2, 50d) : (.1, .1), (slider.Minimum, slider.Value));
        }
    }

    [AvaloniaFact]
    public async Task PlacementCaptionFollowsStickyCreationType()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        var section = new LocalsEditSection { DataContext = vm };
        var window = new Window { Content = section, Width = 250, Height = 600 };
        using var scope = new TestUiScope(window);
        var place = section.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Command == vm.PlaceLocalAtCenterCommand);
        Assert.Equal("Place Linear at center", place.Content);
        foreach (var radial in new[] { true, false })
        {
            if (radial) vm.AddRadialCommand.Execute(null);
            else vm.AddLinearCommand.Execute(null);
            var caption = radial ? "Place Radial at center" : "Place Linear at center";
            Assert.Equal(caption, place.Content);
            vm.EscapeLocals();
            Assert.Equal(caption, place.Content);
            await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
            Assert.Equal(radial ? "radial" : "linear", vm.SelectedLocal!.Type);
            Assert.Equal(caption, place.Content);
        }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(6)]
    public void FullyFeatheredRadialCenterRemainsDraggable(double offset)
    {
        var overlay = new LocalsOverlayControl();
        overlay.Measure(new Size(800, 600));
        overlay.Arrange(new Rect(0, 0, 800, 600));
        var frame = new HappyPhoton.Models.LocalsFrame(800, 600, 0, 0, 1, 1);
        var local = new HappyPhoton.Models.LocalAdjustment
            { Type = "radial", Rx = .2, Ry = .1, Feather = 1 };
        Assert.Equal(LocalHandle.Center, overlay.HitHandle(new(400 + offset, 300), local, frame));
    }

    [AvaloniaFact]
    public async Task PolarityButtonsAlwaysKeepExactlyOneStateChecked()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.AddRadialCommand.Execute(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        var section = new LocalsEditSection { DataContext = vm };
        var window = new Window { Content = section, Width = 250, Height = 600 };
        using var scope = new TestUiScope(window);
        var inside = section.GetVisualDescendants().OfType<ToggleButton>().Single(b => Equals(b.Content, "Inside"));
        var outside = section.GetVisualDescendants().OfType<ToggleButton>().Single(b => Equals(b.Content, "Outside"));
        foreach (var button in new[] { inside, outside, outside, inside })
        {
            var count = vm.HistoryEntries.Count;
            var changed = button.IsChecked != true;
            Assert.True(button.Focus());
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            await TestWaits.UntilAsync(() => !vm.SetLocalPolarityCommand.IsRunning);
            Assert.True(button.IsChecked);
            Assert.NotEqual(inside.IsChecked, outside.IsChecked);
            Assert.Equal(count + (changed ? 1 : 0), vm.HistoryEntries.Count);
        }
    }

    [AvaloniaFact]
    public async Task RadialCreationDefaultsOrdinalAndPolarityCommitOnce()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        vm.AddRadialCommand.Execute(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        var local = vm.SelectedLocal!;
        Assert.Equal("Radial 2", local.Name);
        Assert.Equal((.5, .5, .25, .25, 0d, .5, false, 0d),
            (local.Cu, local.Cv, local.Rx, local.Ry, local.Angle, local.Feather, local.Outside, local.Exposure));
        Assert.Equal("Add Radial", vm.HistoryEntries[0].Label);
        Assert.Equal((50d, 50d, 50d), (vm.LocalWidth, vm.LocalHeight, vm.LocalFeather));
        var count = vm.HistoryEntries.Count;
        await vm.SetLocalPolarityCommand.ExecuteAsync("outside");
        Assert.True(vm.IsLocalOutside);
        Assert.False(vm.IsLocalInside);
        Assert.Equal(count + 1, vm.HistoryEntries.Count);
        Assert.Equal("Local polarity", vm.HistoryEntries[0].Label);
        await vm.SetLocalPolarityCommand.ExecuteAsync("outside");
        Assert.Equal(count + 1, vm.HistoryEntries.Count);
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.True(vm.IsLocalInside);
        vm.AddRadialCommand.Execute(null);
        vm.BeginLocalsGesture(LocalHandle.Create, new(.2, .3));
        vm.MoveLocalsGesture(new(.8, .3), 100);
        await vm.CompleteLocalsGestureAsync();
        Assert.Equal(2, vm.Locals.Count);
        vm.AddRadialCommand.Execute(null);
        vm.BeginLocalsGesture(LocalHandle.Create, new(.2, .3));
        vm.MoveLocalsGesture(new(.6, .7), 7);
        Assert.Equal(2, vm.Locals.Count);
        vm.MoveLocalsGesture(new(.6, .7), 100);
        var frame = vm.LocalsFrame!.Value;
        Assert.Equal(.2, vm.SelectedLocal!.Cu);
        Assert.Equal(.3, vm.SelectedLocal.Cv);
        Assert.Equal(.4 * frame.Width / frame.LongEdge, vm.SelectedLocal.Rx, 12);
        Assert.Equal(.4 * frame.Height / frame.LongEdge, vm.SelectedLocal.Ry, 12);
        await vm.CompleteLocalsGestureAsync();
        Assert.Equal("Radial 3", vm.SelectedLocal.Name);
    }

    [AvaloniaTheory]
    [InlineData(LocalHandle.Center)]
    [InlineData(LocalHandle.AxisXPositive)]
    [InlineData(LocalHandle.AxisXNegative)]
    [InlineData(LocalHandle.AxisYPositive)]
    [InlineData(LocalHandle.AxisYNegative)]
    [InlineData(LocalHandle.Rotation)]
    [InlineData(LocalHandle.FeatherRing)]
    public async Task RadialHandlesPreserveGestureHistoryAndCancel(LocalHandle handle)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.AddRadialCommand.Execute(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        var before = vm.SelectedLocal! with { };
        var count = vm.HistoryEntries.Count;
        Assert.True(vm.BeginLocalsGesture(handle, new(.5, .5)));
        vm.MoveLocalsGesture(new(.68, .64), 100);
        Assert.NotEqual(before, vm.SelectedLocal);
        if (handle != LocalHandle.Center) Assert.Equal((before.Cu, before.Cv), (vm.SelectedLocal!.Cu, vm.SelectedLocal.Cv));
        Assert.Equal(count, vm.HistoryEntries.Count);
        vm.EscapeLocals();
        Assert.Equal(before, vm.SelectedLocal);
        vm.BeginLocalsGesture(handle, new(.5, .5));
        vm.MoveLocalsGesture(new(.68, .64), 100);
        await vm.CompleteLocalsGestureAsync();
        Assert.Equal(count + 1, vm.HistoryEntries.Count);
        Assert.Equal("Local geometry", vm.HistoryEntries[0].Label);
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(before, vm.SelectedLocal);
    }

    [AvaloniaTheory]
    [InlineData("Width", -1, .2)]
    [InlineData("Width", 250, 200)]
    [InlineData("Height", -1, .2)]
    [InlineData("Height", 250, 200)]
    [InlineData("Feather", -1, 0)]
    [InlineData("Feather", 150, 100)]
    public async Task RadialNumericBoundsUseGeometryTransaction(string field, double value, double expected)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.AddRadialCommand.Execute(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        var before = vm.SelectedLocal! with { };
        var count = vm.HistoryEntries.Count;
        vm.OnSliderEditStarted();
        var property = typeof(MainWindowViewModel).GetProperty("Local" + field)!;
        property.SetValue(vm, value);
        vm.OnSliderEditCompleted("Local geometry");
        clock.Advance(TimeSpan.FromMilliseconds(200));
        await vm.PendingPreviewDebounceTask!;
        Assert.Equal(expected, property.GetValue(vm));
        Assert.Equal(count + 1, vm.HistoryEntries.Count);
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(before, vm.SelectedLocal);
    }

    [AvaloniaFact]
    public void RadialKnobsWinOverRingAndPins()
    {
        var overlay = new LocalsOverlayControl();
        overlay.Measure(new Size(800, 600));
        overlay.Arrange(new Rect(0, 0, 800, 600));
        var frame = new HappyPhoton.Models.LocalsFrame(800, 600, 0, 0, 1, 1);
        var local = new HappyPhoton.Models.LocalAdjustment { Type = "radial", Angle = 0, Rx = .2, Ry = .1, Feather = 0 };
        Assert.Equal(LocalHandle.AxisXPositive, overlay.HitHandle(new(560, 300), local, frame));
        Assert.Equal(LocalHandle.AxisXNegative, overlay.HitHandle(new(240, 300), local, frame));
        Assert.Equal(LocalHandle.AxisYPositive, overlay.HitHandle(new(400, 380), local, frame));
        Assert.Equal(LocalHandle.AxisYNegative, overlay.HitHandle(new(400, 220), local, frame));
        Assert.Equal(LocalHandle.Rotation, overlay.HitHandle(new(624, 300), local, frame));
        Assert.Equal(LocalHandle.Center, overlay.HitHandle(new(400, 300), local, frame));
        local.Feather = .5;
        Assert.Equal(LocalHandle.FeatherRing, overlay.HitHandle(new(480, 300), local, frame));
    }
}
