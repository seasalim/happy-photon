using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

// WP8 G2: effective pointer targets, including the filtered-empty overlay.
// No image content is needed: metadata and decode are replaced by test-only loaders.
public sealed class BrowseShellExportHitTargetBaselineTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task RatingLastMatchingPhotoOutOfFilter_KeepsLoupePointerInput()
    {
        using var fixture = new CatalogVmFixture("wp8-filtered-loupe");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(
            catalog, new NullBaseLoader(), _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var image = new ImageFile(fixture.Path("last-match.jpg")) { Rating = 1, MetadataLoaded = true };
        vm.Browse.SetImages([image]);
        vm.Browse.MinimumRating = 1;
        vm.SelectedImage = image;
        var window = new MainWindow { Width = 1440, Height = 900 };
        using var windowScope = TestUiScope.ForMainWindow(window, vm);
        vm.EnterLoupeCommand.Execute(null);
        await vm.SetRatingCommand.ExecuteAsync(0);
        Settle(window);

        Assert.Empty(vm.Browse.VisibleImages);
        Assert.True(vm.IsLoupeMode);
        var browse = window.FindControl<BrowseGridView>("BrowseGridView")!;
        var clear = browse.FindControl<Button>("FilteredEmptyClearButton")!;
        var viewer = browse.FindControl<LoupeView>("LoupeView")!
            .FindControl<ZoomPanControl>("LoupeZoomPanControl")!;
        var point = clear.TranslatePoint(new Point(clear.Bounds.Width / 2, clear.Bounds.Height / 2), window)!.Value;
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point));
        Assert.True(hit == viewer || hit.GetVisualAncestors().Contains(viewer));
        var presses = 0;
        viewer.AddHandler(InputElement.PointerPressedEvent, (_, _) => presses++,
            Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        Assert.Equal(1, presses);
        Assert.Equal(1, vm.Browse.MinimumRating);
    }

    [AvaloniaTheory]
    [MemberData(nameof(ThemeResourceTests.Variants), MemberType = typeof(ThemeResourceTests))]
    public async Task G2_EffectiveHitDimensionsMeetMinimum(ThemeVariant theme)
    {
        var samples = new Dictionary<string, List<double>>();

        for (var run = 1; run <= 3; run++)
        {
            using var fixture = new CatalogVmFixture("wp8-hit-target-baseline");
            using var catalog = await fixture.CreateCatalogAsync();
            await using var vm = fixture.CreateViewModel(
                catalog, new NullBaseLoader(), _ => Task.CompletedTask);
            vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
            var image = new ImageFile(fixture.Path("unloaded-gps.jpg"))
            {
                GpsLatitude = 51.5074,
                GpsLongitude = -0.1278,
                MetadataLoaded = true
            };
            vm.Browse.SetImages([image]);
            // test-teardown-policy: allow - the following ForMainWindow scope owns binding and closure.
            var window = new MainWindow { Width = 1440, Height = 900 };
            using var themeScope = new TestUiScope(theme: theme);
            using var windowScope = TestUiScope.ForMainWindow(window, vm);
            vm.Browse.MinimumRating = 1;
            Settle(window);
            var browse = window.FindControl<BrowseGridView>("BrowseGridView")!;
            Report(window, browse.FindControl<Button>("FilteredEmptyClearButton")!,
                theme, run, "Browse filtered-empty state", samples);
            var rating = browse.FindControl<BrowseRatingFilter>("RatingFilter")!;
            Report(window, rating.FindControl<Button>("RatingFilter1Button")!,
                theme, run, "Browse filter bar", samples);

            var clear = browse.FindControl<Button>("FilteredEmptyClearButton")!;
            var clearPoint = clear.TranslatePoint(new Point(clear.Bounds.Width / 2, clear.Bounds.Height / 2), window)!.Value;
            window.MouseDown(clearPoint, MouseButton.Left);
            window.MouseUp(clearPoint, MouseButton.Left);
            Assert.Equal(0, vm.Browse.MinimumRating);
            Assert.Single(vm.Browse.VisibleImages);
            vm.SelectedImage = image;
            Settle(window);
            var review = window.FindControl<BrowseReviewPane>("BrowseReviewPane")!;
            Report(window, review.FindControl<Button>("ReviewMapLink")!,
                theme, run, "Browse review pane with GPS metadata", samples);

            vm.ExportSettings.ShowProof = true;
            vm.SwitchToExportCommand.Execute(null);
            Settle(window);
            var export = window.FindControl<ExportPreviewPane>("ExportPreviewPane")!;
            Report(window, export.FindControl<ToggleButton>("ExportProofToggle")!,
                theme, run, "Export preview pane", samples);
            Report(window, export.FindControl<ComboBox>("ExportProofSizeChooser")!,
                theme, run, "Export preview pane (proof enabled)", samples);
        }

        foreach (var (name, values) in samples)
        {
            var sorted = values.Order().ToArray();
            output.WriteLine(FormattableString.Invariant(
                $"G2 theme={theme} element={name} runs={sorted.Length} median={sorted[sorted.Length / 2]:F3}px range={sorted[0]:F3}..{sorted[^1]:F3}px"));
        }
    }

    private void Report(Window window, Control target, ThemeVariant theme, int run,
        string scene, Dictionary<string, List<double>> samples)
    {
        // Structural assertions only: a hidden or disabled control is not a sample.
        Assert.True(target.IsEffectivelyVisible);
        Assert.True(target.IsEffectivelyEnabled);
        Assert.True(target.IsHitTestVisible);
        Assert.True(target.Bounds.Width > 0 && target.Bounds.Height > 0);
        var origin = target.TranslatePoint(default, window)!.Value;
        var end = target.TranslatePoint(new Point(target.Bounds.Width, target.Bounds.Height), window)!.Value;
        var width = (end.X - origin.X) * window.RenderScaling;
        var height = (end.Y - origin.Y) * window.RenderScaling;

        // Check center, edges and corners just inside the transformed layout rectangle.
        // Hit testing verifies the clickable control, rather than measuring its glyph.
        var hits = 0;
        var blockers = new HashSet<string>();

        foreach (var x in new[] { 0.5, target.Bounds.Width / 2, target.Bounds.Width - 0.5 })
        {
            foreach (var y in new[] { 0.5, target.Bounds.Height / 2, target.Bounds.Height - 0.5 })
            {
                var point = target.TranslatePoint(new Point(x, y), window)!.Value;
                var hit = window.InputHitTest(point) as Visual;
                if (hit == target || hit?.GetVisualAncestors().Contains(target) == true)
                {
                    hits++;
                }
                else
                {
                    blockers.Add($"{hit?.GetType().Name ?? "nothing"}#{(hit as Control)?.Name}");
                }
            }
        }

        var minimum = Math.Min(width, height);
        Record(samples, target.Name + " (layout)", minimum);
        output.WriteLine(FormattableString.Invariant(
            $"G2 theme={theme} run={run} element={target.Name} scene={scene} layout={width:F3}x{height:F3}px layoutSmallest={minimum:F3}px renderScaling={window.RenderScaling:F3} hitPoints={hits}/9"));

        bool Accepts(double x, double y)
        {
            var point = target.TranslatePoint(new Point(x, y), window)!.Value;
            var hit = window.InputHitTest(point) as Visual;

            return hit == target || hit?.GetVisualAncestors().Contains(target) == true;
        }

        var horizontal = HitSpan(target.Bounds.Width, x => Accepts(x, target.Bounds.Height / 2));
        var vertical = HitSpan(target.Bounds.Height, y => Accepts(target.Bounds.Width / 2, y));

        if (horizontal is null || vertical is null)
        {
            output.WriteLine($"G2 UNMEASURABLE theme={theme} run={run} element={target.Name}: " +
                $"no hit span across the control center; blockers={string.Join(", ", blockers)}");

            Assert.Fail("Target has no effective hit rectangle.");
        }

        var effectiveWidth = (horizontal.Value.End - horizontal.Value.Start) * window.RenderScaling;
        var effectiveHeight = (vertical.Value.End - vertical.Value.Start) * window.RenderScaling;
        var horizontalPoints = new[]
        {
            horizontal.Value.Start + 0.01,
            (horizontal.Value.Start + horizontal.Value.End) / 2,
            horizontal.Value.End - 0.01
        };
        var verticalPoints = new[]
        {
            vertical.Value.Start + 0.01,
            (vertical.Value.Start + vertical.Value.End) / 2,
            vertical.Value.End - 0.01
        };

        if (!horizontalPoints.All(x => verticalPoints.All(y => Accepts(x, y))))
        {
            output.WriteLine($"G2 UNMEASURABLE theme={theme} run={run} element={target.Name}: " +
                "center hit spans do not describe a rectangular target.");

            Assert.Fail("Target hit rectangle is obstructed.");
        }

        Assert.True(Math.Min(effectiveWidth, effectiveHeight) >= 19.999,
            $"{target.Name}: effective hit {effectiveWidth:F3}x{effectiveHeight:F3} must be at least 20px.");
        Record(samples, target.Name + " (effective rectangle)", Math.Min(effectiveWidth, effectiveHeight));
        output.WriteLine(FormattableString.Invariant(
            $"G2 theme={theme} run={run} element={target.Name} effectiveHit={effectiveWidth:F3}x{effectiveHeight:F3}px smallest={Math.Min(effectiveWidth, effectiveHeight):F3}px interiorHitPoints=9/9"));
    }

    // Probe at quarter-pixel spacing, then locate each hit/no-hit boundary within 0.001 DIP.
    private static (double Start, double End)? HitSpan(double limit, Func<double, bool> accepts)
    {
        var positions = Enumerable.Range(0, (int)Math.Ceiling(limit * 4))
            .Select(index => Math.Min(limit, index / 4.0 + 0.125))
            .Where(accepts).ToArray();
        if (positions.Length == 0) return null;

        double Boundary(double outside, double inside)
        {
            while (Math.Abs(inside - outside) > 0.001)
            {
                var middle = (inside + outside) / 2;

                if (accepts(middle))
                {
                    inside = middle;
                }
                else
                {
                    outside = middle;
                }
            }

            return (inside + outside) / 2;
        }

        var start = accepts(0) ? 0 : Boundary(Math.Max(0, positions[0] - 0.25), positions[0]);
        var end = accepts(limit) ? limit : Boundary(Math.Min(limit, positions[^1] + 0.25), positions[^1]);

        return (start, end);
    }

    private static void Record(Dictionary<string, List<double>> samples, string name, double value)
    {
        if (!samples.TryGetValue(name, out var values))
        {
            values = [];
            samples.Add(name, values);
        }

        values.Add(value);
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
}
