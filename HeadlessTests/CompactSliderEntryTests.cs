using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;
using static HappyPhoton.Tests.CompactSliderEntryTestSupport;

namespace HappyPhoton.Tests;

public sealed partial class CompactSliderEntryTests
{
    [AvaloniaTheory]
    [InlineData("Contrast", "35", 35)]
    [InlineData("Contrast", "+15", 15)]
    [InlineData("Exposure", "+0.35", .35)]
    [InlineData("Exposure", "0.37", .35)]
    [InlineData("Contrast", "+250", 100)]
    [InlineData("Contrast", "−20", -20)]
    [InlineData("Contrast", "-20%", -20)]
    [InlineData("Exposure", "+0.37 EV", .35)]
    [InlineData("Temperature", "5600", 5600)]
    [InlineData("Temperature", "5600K", 5600)]
    [InlineData("Temperature", "5314", 5300)]
    [InlineData("Temperature", "11950", 11950)]
    public async Task TypedValueCommitsOnceInDisplayedUnits(string label, string text, double expected)
    {
        await using var s = await Session.Create();
        var slider = s.Slider(label);
        var history = s.Steps;
        var before = slider.Value;
        var starts = 0;
        var ends = 0;
        slider.DragStarted += (_, _) => starts++;
        slider.DragCompleted += (_, _) => ends++;
        var entry = s.Open(slider);
        Assert.Equal(entry.Text, entry.SelectedText);
        Assert.Equal(label + " value", AutomationProperties.GetName(entry));
        s.Window.KeyTextInput(text);
        Assert.Equal(before, slider.Value);
        Assert.Equal(0, starts);
        s.Key(Key.Enter);
        await s.Drain();

        Assert.False(slider.IsEditingValue);
        Assert.Equal(expected, slider.ValueToDisplay?.Invoke(slider.Value) ?? slider.Value, 7);
        Assert.Equal(history + 1, s.Steps);
        Assert.Equal(1, starts);
        Assert.Equal(1, ends);
        Assert.Null(s.Window.FocusManager!.GetFocusedElement());
        if (label == "Contrast") Assert.Contains("Contrast", s.Vm.HistoryEntries[0].Label);
        if (label == "Temperature") Assert.Equal($"{expected:0}K", slider.DisplayText);

        var displayed = slider.FindControl<TextBlock>("ValueText")!.Text!;
        entry = s.Open(slider);
        Assert.Equal(displayed.TrimEnd('K'), entry.Text);
        s.Key(Key.Enter);
        await s.Drain();
        Assert.Equal(expected, slider.ValueToDisplay?.Invoke(slider.Value) ?? slider.Value, 7);
        Assert.Equal(history + 1, s.Steps);
    }

    [AvaloniaTheory]
    [InlineData("abc", false)]
    [InlineData("", false)]
    [InlineData("NaN", false)]
    [InlineData("35", true)]
    public async Task InvalidAndCancelledTextMakeNoChange(string text, bool escape)
    {
        await using var s = await Session.Create();
        var slider = s.Slider("Contrast");
        var count = s.Steps;
        s.Open(slider);
        s.Key(Key.Delete);
        s.Window.KeyTextInput(text);
        s.Key(escape ? Key.Escape : Key.Enter);
        await s.Drain();

        Assert.Equal(0, slider.Value);
        Assert.Equal(count, s.Steps);
    }

    [AvaloniaFact]
    public async Task KelvinEscapePreservesAsShot()
    {
        await using var s = await Session.Create();
        var count = s.Steps;
        s.Open(s.Slider("Temperature"));
        s.Window.KeyTextInput("5600");
        s.Key(Key.Escape);
        Assert.Equal("As Shot", s.Vm.SelectedWhiteBalanceMode);
        Assert.Equal(count, s.Steps);
    }

    [AvaloniaFact]
    public async Task CurrentCultureIsUsedForDecimalInput()
    {
        var previous = CultureInfo.CurrentCulture;
        var previousDefault = CultureInfo.DefaultThreadCurrentCulture;
        var culture = CultureInfo.GetCultureInfo("fr-FR");

        try
        {
            // Headless input drains dispatcher jobs whose captured execution contexts
            // can predate this test. Give those contexts the same culture as the input.
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.CurrentCulture = culture;
            await using var s = await Session.Create();
            var slider = s.Slider("Exposure");
            var entry = s.Open(slider);
            Assert.Equal(culture, CultureInfo.CurrentCulture);
            s.Window.KeyTextInput("0,37");
            Assert.Equal("0,37", entry.Text);
            Assert.Equal(culture, CultureInfo.CurrentCulture);
            s.Key(Key.Enter);
            Assert.Equal(culture, CultureInfo.CurrentCulture);
            Assert.Equal(.35, s.Vm.Exposure, 8);
            await s.Drain();
            Assert.Equal(.35, s.Vm.Exposure, 8);
            Assert.Equal(1, s.Steps);
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentCulture = previousDefault;
            CultureInfo.CurrentCulture = previous;
        }
    }

    [AvaloniaFact]
    public async Task ValueDragScrubsAndDoubleClickEditsWhileTrackDoubleClickResets()
    {
        await using var s = await Session.Create();
        var slider = s.Slider("Contrast");
        var point = s.Reveal(slider);
        s.Window.MouseDown(point, MouseButton.Left);
        s.Window.MouseMove(point + new Vector(-10, 0), RawInputModifiers.LeftMouseButton);
        s.Window.MouseUp(point + new Vector(-10, 0), MouseButton.Left);
        await s.Drain();
        Assert.False(slider.IsEditingValue);
        Assert.True(slider.Value < 0);
        var value = slider.Value;
        s.Open(slider);
        s.Click(s.Center(slider.FindControl<NumericEntryBox>("ValueEntry")!));
        Assert.True(slider.IsEditingValue);
        Assert.Equal(value, slider.Value);
        s.Key(Key.Escape);
        point = slider.TranslatePoint(new Point(100, 10), s.Window)!.Value;
        s.Click(point);
        s.Click(point);
        Assert.Equal(slider.DefaultValue, slider.Value);
    }

    [AvaloniaTheory]
    [InlineData("Contrast")]
    [InlineData("Exposure")]
    [InlineData("Temperature")]
    public async Task EditorAndHoverPreserveRowAndDigitPositions(string label)
    {
        await using var s = await Session.Create();
        var slider = s.Slider(label);
        s.Reveal(slider);
        using var frame = s.Window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var text = slider.FindControl<TextBlock>("ValueText")!;
        var origin = text.TranslatePoint(default, slider)!.Value;
        var digits = Enumerable.Range(0, text.Text!.Length).Where(i => char.IsDigit(text.Text[i])).ToArray();
        var before = digits.Select(i => text.TextLayout.HitTestTextRange(i, 1).Single().X + origin.X).ToArray();
        var cell = slider.FindControl<Border>("ValueCell")!;
        s.Window.MouseMove(slider.TranslatePoint(new Point(100, 10), s.Window)!.Value);
        Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(cell.Background).Color);
        s.Window.MouseMove(s.Center(cell));
        Assert.True(cell.TryFindResource("ControlHover", cell.ActualThemeVariant, out var hover));
        Assert.Equal(hover, cell.Background);
        var entry = s.Open(slider);
        var border = entry.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_BorderElement");
        Assert.Equal(new Thickness(1), border.BorderThickness);
        Assert.True(entry.TryFindResource("Outline", entry.ActualThemeVariant, out var outline));
        Assert.Equal(outline, border.BorderBrush);
        Assert.True(entry.TryFindResource("SurfaceHigh", entry.ActualThemeVariant, out var surface));
        Assert.Equal(surface, border.Background);
        var presenter = entry.GetVisualDescendants().OfType<TextPresenter>().Single();
        var afterOrigin = presenter.TranslatePoint(default, slider)!.Value;
        var after = digits.Select(i => presenter.TextLayout.HitTestTextRange(i, 1).Single().X + afterOrigin.X).ToArray();

        Assert.Equal(new Size(220, 20), slider.Bounds.Size);
        Assert.Equal(40, slider.FindControl<Grid>("LayoutGrid")!.ColumnDefinitions[2].ActualWidth);
        Assert.InRange(entry.Bounds.Width, 0, 49);
        Assert.InRange(entry.Bounds.Height, 0, 18);
        Assert.Equal(new Size(49, 18), entry.Bounds.Size);
        Assert.Equal(new Rect(entry.Bounds.Size), VisibleBounds(entry));
        Assert.Equal(before.Length, after.Length);

        for (var i = 0; i < before.Length; i++)
        {
            Assert.InRange(Math.Abs(after[i] - before[i]), 0, .5);
        }

        Assert.InRange(Math.Abs(afterOrigin.Y + presenter.TextLayout.Baseline - origin.Y - text.TextLayout.Baseline), 0, .5);
    }

    [AvaloniaTheory]
    [InlineData("slider-value-entry-rest")]
    [InlineData("slider-value-entry-hover")]
    [InlineData("slider-value-entry-editing")]
    public async Task Showcase(string scene)
    {
        await using var s = await Session.Create();
        var slider = s.Slider("Contrast");
        s.Reveal(slider);
        s.Window.MouseMove(new Point(600, 100));

        if (scene.EndsWith("hover")) s.Window.MouseMove(s.Center(slider.FindControl<Border>("ValueCell")!));

        if (scene.EndsWith("editing"))
        {
            s.Open(slider);
            s.Window.KeyTextInput("35");
        }

        var group = slider.GetVisualAncestors().OfType<DevelopGroup>().First();
        var scroll = slider.GetVisualAncestors().OfType<ScrollViewer>().First();
        scroll.Offset += new Vector(0, group.TranslatePoint(default, scroll)!.Value.Y);
        Dispatcher.UIThread.RunJobs();
        if (scene.EndsWith("hover")) s.Window.MouseMove(s.Center(slider.FindControl<Border>("ValueCell")!));
        else s.Window.MouseMove(new Point(600, 100));

        Assert.Equal(scene.EndsWith("editing"), slider.IsEditingValue);
        Assert.Equal(0, s.Vm.Contrast);
        var cell = slider.FindControl<Border>("ValueCell")!;

        if (scene.EndsWith("hover"))
        {
            Assert.True(cell.TryFindResource("ControlHover", ThemeVariant.Dark, out var hover));
            Assert.Equal(hover, cell.Background);
        }

        if (scene.EndsWith("editing"))
        {
            var entry = slider.FindControl<NumericEntryBox>("ValueEntry")!;
            Assert.Equal("35", entry.Text);
            Assert.True(entry.IsFocused);
        }

        ShowcaseTestHelper.Capture(scene, s.Scope, new PixelSize(1200, 700), ThemeVariant.Dark);
    }
}
