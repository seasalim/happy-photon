using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class VisualsRadiusGateTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [MemberData(nameof(ThemeResourceTests.Variants), MemberType = typeof(ThemeResourceTests))]
    public async Task ObserveRealizedSurfaceRadii(ThemeVariant theme)
    {
        await DevelopToolsBaselineTests.WithScene("normal", 1440, 900, async (vm, scope) =>
        {
            using var themeScope = new TestUiScope(theme: theme);
            vm.AppTheme = theme == HappyPhotonThemes.MidGray ? AppTheme.MidGray : AppTheme.Dark;
            var window = scope.Window!;
            var first = vm.SelectedImage!;
            first.ColorLabel = ColorLabel.Red;
            first.HasEdits = true;
            var second = new ImageFile(GoldenTestPaths.Asset("display-p3-reference.jpg"))
            {
                ColorLabel = ColorLabel.Blue,
                Rating = 3
            };
            Assert.Equal(0, (int)File.GetAttributes(second.FilePath) & (0x1000 | 0x40000 | 0x400000));
            vm.Browse.SetImages([first, second]);
            vm.SwitchToBrowseCommand.Execute(null);
            vm.ToggleImageSelection(first);
            vm.ToggleImageSelection(second);
            // test-teardown-policy: allow - WithScene owns and disposes the supplied scope.
            scope.Show();
            Drain(window);
            Assert.True(first.IsSelected && first.IsActive);
            Assert.True(second.IsSelected);
            var browse = window.FindControl<BrowseGridView>("BrowseGridView")!;
            AssertRealized(browse);
            var tiles = browse.GetVisualDescendants().OfType<Border>()
                .Where(border => border.Name == "ThumbnailTile").ToArray();
            Assert.Equal(2, tiles.Length);
            Assert.Contains(browse.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("color-label-marker"));
            Assert.NotEmpty(browse.GetVisualDescendants().OfType<BrowseColorLabelFilter>());
            var total = Observe(theme, "Browse", window);

            var tile = tiles.Single(border => ReferenceEquals(border.DataContext, first));
            var menu = tile.ContextMenu!;

            try
            {
                menu.Open(tile);
                Drain(window);
                Assert.True(menu.IsOpen);
                AssertRealized(menu);
                total += Observe(theme, "TileContextMenu", PopupRootFor(menu));
            }
            finally
            {
                menu.Close();
            }

            try
            {
                ToolTip.SetIsOpen(tile, true);
                Drain(window);
                Assert.True(ToolTip.GetIsOpen(tile));
                var tip = window.GetVisualDescendants().OfType<ToolTip>().Single();
                AssertRealized(tip);
                total += Observe(theme, "Tooltip", PopupRootFor(tip));
            }
            finally
            {
                ToolTip.SetIsOpen(tile, false);
            }

            vm.ToggleLoupeCommand.Execute(null);
            Drain(window);
            Assert.True(vm.IsLoupeMode);
            AssertRealized(window.GetVisualDescendants().OfType<LoupeView>().Single());
            total += Observe(theme, "Loupe", window);
            vm.ExitLoupeCommand.Execute(null);
            vm.ToggleCompareCommand.Execute(null);
            Drain(window);
            Assert.True(vm.IsCompareMode);
            AssertRealized(window.GetVisualDescendants().OfType<CompareView>().Single());
            total += Observe(theme, "Compare", window);
            vm.ToggleCompareCommand.Execute(null);
            vm.SwitchToDevelopCommand.Execute(null);
            Drain(window);

            foreach (var group in window.GetVisualDescendants().OfType<DevelopGroup>().ToArray())
            {
                group.IsExpanded = true;
            }

            Drain(window);
            AssertRealized(window.GetVisualDescendants().OfType<MixerEditGroup>().Single());
            total += Observe(theme, "Develop", window);
            await vm.ToggleCropModeCommand.ExecuteAsync(null);
            Drain(window);
            Assert.True(vm.IsCropMode);
            AssertRealized(window.GetVisualDescendants().OfType<CropOverlayControl>()
                .Single(control => control.IsEffectivelyVisible));
            total += Observe(theme, "DevelopCrop", window);
            await vm.CancelCropCommand.ExecuteAsync(null);
            await vm.ToggleSpotsModeCommand.ExecuteAsync(null);
            await TestWaits.UntilAsync(() => vm.CanEditSpots);
            Drain(window);
            Assert.True(vm.IsSpotsMode);
            var spotsButton = window.GetVisualDescendants().OfType<ToggleButton>()
                .Single(control => control.Name == "SpotsModeButton");
            AssertRealized(spotsButton);
            Assert.True(spotsButton.IsChecked);
            Assert.True(window.GetVisualDescendants().OfType<ZoomPanControl>()
                .Single(control => control.Name == "ZoomPanControl").IsSpotsMode);
            total += Observe(theme, "DevelopSpots", window);
            await vm.ToggleSpotsModeCommand.ExecuteAsync(null);
            await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
            await TestWaits.UntilAsync(() => vm.CanEditLocals);
            Drain(window);
            Assert.True(vm.IsLocalsMode);
            AssertRealized(window.GetVisualDescendants().OfType<LocalsEditSection>().Single());
            AssertRealized(window.GetVisualDescendants().OfType<LocalsOverlayControl>()
                .Single(control => control.IsEffectivelyVisible));
            total += Observe(theme, "DevelopLocals", window);
            await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
            await vm.SwitchToExportCommand.ExecuteAsync(null);
            Drain(window);
            Assert.True(vm.IsExportMode);
            var export = window.GetVisualDescendants().OfType<ExportSettingsPane>().Single();
            AssertRealized(export);
            total += Observe(theme, "Export", window);
            var combo = export.FindControl<ComboBox>("ExportFormatBox")!;

            try
            {
                combo.IsDropDownOpen = true;
                Drain(window);
                Assert.True(combo.IsDropDownOpen);
                var popup = combo.GetTemplateDescendants().OfType<Popup>().Single(popup => popup.IsOpen);
                AssertRealized(popup.Child!);
                total += Observe(theme, "Dropdown", PopupRootFor(popup.Child!));
            }
            finally
            {
                combo.IsDropDownOpen = false;
            }

            var settings = new SettingsDialog(vm);
            using var settingsScope = new TestUiScope(settings, theme, window);
            Drain(settings);
            AssertRealized(settings);
            total += Observe(theme, "Settings", settings);
            output.WriteLine($"G1 theme={theme.Key} total={total}");
            output.WriteLine("Allowlist: mixer-band Button; its direct swatch Border and " +
                "PART_ContentPresenter templated by that Button. Edits/clipping dots and curve points " +
                "use Ellipse/drawing geometry and have none of the measured radius properties.");
            Assert.Equal(0, total);
        });
    }

    private int Observe(ThemeVariant theme, string surface, Visual root)
    {
        var visuals = Walk(root).ToArray();
        Assert.Equal(theme, ((Control)root).ActualThemeVariant);
        Assert.NotEmpty(visuals);
        var offences = visuals.Where(visual => !IsSemanticCircle(visual) && HasRadius(visual)).ToArray();
        output.WriteLine($"G1 theme={theme.Key} surface={surface} realized={visuals.Length} offences={offences.Length}");

        foreach (var group in offences.GroupBy(Describe).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"  {group.Key} count={group.Count()}");
        }

        return offences.Length;
    }

    private static IEnumerable<Visual> Walk(Visual root)
    {
        var pending = new Queue<Visual>();
        var seen = new HashSet<Visual>(ReferenceEqualityComparer.Instance);
        pending.Enqueue(root);

        while (pending.TryDequeue(out var current))
        {
            if (!seen.Add(current)) continue;

            yield return current;

            foreach (var child in current.GetVisualChildren())
            {
                pending.Enqueue(child);
            }

            if (current is Control control)
            {
                foreach (var popup in control.GetLogicalChildren().OfType<Popup>().Where(popup => popup.IsOpen))
                {
                    if (popup.Child is { } child) pending.Enqueue(PopupRootFor(child));
                }
            }

            if (current is TemplatedControl templated)
            {
                foreach (var popup in templated.GetTemplateDescendants().OfType<Popup>().Where(popup => popup.IsOpen))
                {
                    if (popup.Child is { } child) pending.Enqueue(PopupRootFor(child));
                }
            }
        }
    }

    private static bool HasRadius(Visual visual) => visual switch
    {
        Border border => border.CornerRadius != default,
        ContentPresenter presenter => presenter.CornerRadius != default,
        TemplatedControl control => control.CornerRadius != default,
        Rectangle rectangle => rectangle.RadiusX != 0 || rectangle.RadiusY != 0,
        _ => false
    };

    private static Visual PopupRootFor(Control control) =>
        control.GetVisualAncestors().FirstOrDefault(ancestor => ancestor is PopupRoot or OverlayPopupHost) ?? control;

    private static bool IsSemanticCircle(Visual visual) => visual switch
    {
        Button button => button.Classes.Contains("mixer-band"),
        Border border => border.Parent is Button button && button.Classes.Contains("mixer-band"),
        ContentPresenter presenter => presenter.TemplatedParent is Button button &&
            button.Classes.Contains("mixer-band"),
        _ => false
    };

    private static string Describe(Visual visual) =>
        $"{visual.GetType().Name} name={(visual as Control)?.Name ?? "<unnamed>"} " +
        $"classes={string.Join(',', (visual as Control)?.Classes ?? [])} radius=" + visual switch
        {
            Border border => border.CornerRadius.ToString(),
            ContentPresenter presenter => presenter.CornerRadius.ToString(),
            TemplatedControl control => control.CornerRadius.ToString(),
            Rectangle rectangle => $"{rectangle.RadiusX},{rectangle.RadiusY}",
            _ => "?"
        };

    private static void AssertRealized(Control control)
    {
        Assert.True(control.IsEffectivelyVisible, $"{control.GetType().Name} is hidden.");
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
        Assert.NotNull(TopLevel.GetTopLevel(control));
        Assert.NotEmpty(Walk(control));
    }

    private static void Drain(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
