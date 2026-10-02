using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

// STRAIGHTEN-WP2 G2 measurements after bringing the Crop section into view.
public sealed class CropAutoLayoutMeasurementTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases =>
        from width in new[] { 250, 200 }
        from gray in new[] { false, true }
        from sample in new[] { 1, 2, 3 }
        select new object[] { width, gray, sample };

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public async Task MeasureCropAutoLayout(int width, bool gray, int sample)
    {
        await DevelopToolsBaselineTests.WithScene("crop", 1440, 900, async (vm, scope) =>
        {
            using var theme = new TestUiScope(theme: gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark);
            vm.WhiteBalanceGroup.IsExpanded = true;
            // test-teardown-policy: allow - WithScene owns and disposes the supplied MainWindow scope.
            scope.Show();
            var window = scope.Window!;
            Settle(window);

            if (!vm.IsCropMode)
            {
                await vm.ToggleCropModeCommand.ExecuteAsync(null);
            }

            var panel = window.GetVisualDescendants().OfType<DevelopEditPanel>().Single();
            var workspace = panel.GetVisualAncestors().OfType<Grid>()
                .Single(grid => grid.ColumnDefinitions.Count == 5);
            workspace.ColumnDefinitions[4].Width = new GridLength(width);
            Settle(window);
            Assert.True(vm.IsDevelopMode && vm.IsCropMode && vm.SelectedImage is not null);
            Assert.Equal(width, panel.Bounds.Width);
            var section = panel.GetVisualDescendants().OfType<CropEditSection>().Single();
            section.BringIntoView();
            Settle(window);
            var horizon = section.GetVisualDescendants().OfType<CompactSlider>()
                .Single(slider => slider.Label == "Horizon");
            var layout = horizon.FindControl<Grid>("LayoutGrid")!;
            var auto = section.GetVisualDescendants().OfType<Button>()
                .SingleOrDefault(button => button.Content?.ToString() == "Auto");
            var wbAuto = panel.FindControl<Button>("WhiteBalanceAutoButton")!;
            Assert.Contains("quiet-button", wbAuto.Classes);
            Assert.True(wbAuto.Bounds.Height > 0);
            var scroll = panel.FindControl<ScrollViewer>("DevelopControlsScrollViewer")!;
            var viewport = scroll.GetVisualDescendants().OfType<ScrollContentPresenter>()
                .Single(presenter => presenter.TemplatedParent == scroll);
            var autoInsideViewport = auto is null ? (bool?)null :
                new Rect(viewport.Bounds.Size).Contains(BoundsIn(auto, viewport));

            if (auto != null)
            {
                Assert.True(autoInsideViewport);
            }

            var autoBounds = auto is null ? (Rect?)null : BoundsIn(auto, section);
            var overlaps = auto is null ? Array.Empty<string>() : section.GetVisualDescendants()
                .OfType<Button>().Where(button => button != auto && button.IsEffectivelyVisible
                    && BoundsIn(button, section).Intersects(autoBounds!.Value))
                .Select(button => button.Content?.ToString() ?? button.GetType().Name).ToArray();
            output.WriteLine("CROP_AUTO_LAYOUT " + JsonSerializer.Serialize(new
            {
                sample,
                theme = gray ? "MidGray" : "Dark",
                paneWidth = panel.Bounds.Width,
                trackColumnWidth = layout.ColumnDefinitions[1].ActualWidth,
                autoExists = auto is not null,
                autoHeight = auto?.Bounds.Height,
                whiteBalanceAutoHeight = wbAuto.Bounds.Height,
                autoVisible = auto?.IsEffectivelyVisible,
                autoInsideSection = autoBounds.HasValue ? new Rect(section.Bounds.Size).Contains(autoBounds.Value) : (bool?)null,
                autoInsideViewport,
                overlaps
            }));
            await Task.CompletedTask;
        });
    }

    private static Rect BoundsIn(Control control, Control parent) =>
        new(control.TranslatePoint(default, parent)!.Value, control.Bounds.Size);

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
