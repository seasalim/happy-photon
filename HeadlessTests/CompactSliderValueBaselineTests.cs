using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CompactSliderValueBaselineTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData("Contrast")]
    [InlineData("Exposure")]
    [InlineData("Temperature")]
    public async Task MeasureReadoutAndStationaryClick(string label)
    {
        using var fixture = new CatalogVmFixture("slider-value-baseline");
        using var catalog = await fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = fixture.CreateViewModel(catalog, new LocalTestLoader(),
            _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            timeProvider: clock);
        var image = new ImageFile(fixture.Path("photo.jpg"));
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        vm.IsDevelopMode = true;
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null);
        using var theme = new TestUiScope(theme: ThemeVariant.Dark);
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var slider = window.GetVisualDescendants().OfType<CompactSlider>().Single(s =>
            s.Label == label && s.IsEffectivelyVisible &&
            !s.GetVisualAncestors().OfType<LocalsEditSection>().Any());
        var text = slider.FindControl<TextBlock>("ValueText")!;
        Dispatcher.UIThread.RunJobs();
        slider.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        output.WriteLine($"HIT {label}: point={Center(text, window)}; target={window.InputHitTest(Center(text, window))}");
        Measure(slider, text, window);
        var starts = 0;
        var ends = 0;
        slider.DragStarted += (_, _) => starts++;
        slider.DragCompleted += (_, _) => ends++;
        var before = slider.Value;
        var history = vm.HistoryEntries.Count;
        window.FocusManager!.Focus(null);
        var point = Center(text, window);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        var focusedOnDown = slider.IsFocused;
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        clock.Advance(TimeSpan.FromMilliseconds(200));

        if (vm.PendingPreviewDebounceTask is { } preview)
        {
            await preview.WaitAsync(TestWaits.Condition);
        }

        if (vm.PendingHistoryCommitTask is { } commit)
        {
            await commit.WaitAsync(TestWaits.Condition);
        }

        Dispatcher.UIThread.RunJobs();
        output.WriteLine(
            $"CLICK {label}: downFocus={focusedOnDown}; upFocus={slider.IsFocused}; " +
            $"before={before:R}; after={slider.Value:R}; historyBefore={history}; " +
            $"historyAfter={vm.HistoryEntries.Count}; starts={starts}; ends={ends}; " +
            $"editors={slider.GetVisualDescendants().OfType<TextBox>().Count()}");
    }

    private void Measure(CompactSlider slider, TextBlock text, Window window)
    {
        var grid = slider.FindControl<Grid>("LayoutGrid")!;
        var origin = text.TranslatePoint(default, slider)!.Value;
        var bounds = new Rect(origin, text.Bounds.Size);
        var layout = text.TextLayout;
        var digitRects = Enumerable.Range(0, text.Text!.Length)
            .Where(index => char.IsDigit(text.Text[index]))
            .SelectMany(index => layout.HitTestTextRange(index, 1))
            .Select(rect => rect.Translate(new Vector(origin.X, origin.Y)));
        output.WriteLine(
            $"LAYOUT {slider.Label}: culture={CultureInfo.CurrentCulture.Name}; " +
            $"scale={window.RenderScaling}; row={slider.Bounds.Size}; " +
            $"valueColumn={grid.ColumnDefinitions[2].ActualWidth:R}; text='{text.Text}'; " +
            $"textBounds={bounds}; baseline={origin.Y + layout.Baseline:R}; " +
            $"digitAdvanceBounds=[{string.Join(";", digitRects)}]");

        foreach (var run in layout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>())
        {
            var ink = run.GlyphRun.BuildGeometry().Bounds;
            output.WriteLine($"INK {slider.Label}: run='{run.Text}'; " +
                $"bounds={ink.Translate(new Vector(origin.X, origin.Y))}");
        }
    }

    private static Point Center(Control control, Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
}
