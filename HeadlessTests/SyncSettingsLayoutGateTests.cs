using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

// SYNCSETTINGS-WP2 G2: with 12 photos selected, the Sync button and
// the "⋯" button are not clipped, and the review pane's details still scroll to
// the Selection card's last line above any footer.
public sealed class SyncSettingsLayoutGateTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(800, 500, false, false)]
    [InlineData(800, 500, true, false)]
    [InlineData(1200, 700, false, false)]
    [InlineData(1200, 700, true, false)]
    [InlineData(800, 500, false, true)]
    [InlineData(800, 500, true, true)]
    [InlineData(1200, 700, false, true)]
    [InlineData(1200, 700, true, true)]
    public async Task G2_SyncButtonAndSelectionCardFit(int width, int height, bool gray, bool loupe)
    {
        await DevelopToolsBaselineTests.WithScene("normal", width, height, async (vm, scope) =>
        {
            var theme = gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark;
            using var themeScope = new TestUiScope(theme: theme);
            vm.RestoreAppTheme(gray ? AppTheme.MidGray : AppTheme.Dark);
            var first = vm.SelectedImage!;
            var images = Enumerable.Range(1, 11)
                .Select(index => new ImageFile($"layout-{index:D2}.jpg") { MetadataLoaded = true })
                .Prepend(first)
                .ToArray();
            vm.Browse.SetImages(images);
            vm.SelectedImage = first;
            vm.SwitchToBrowseCommand.Execute(null);
            vm.SelectAllCommand.Execute(null);
            Assert.Equal(12, vm.SelectedCount);

            if (loupe)
            {
                vm.EnterLoupeCommand.Execute(null);
                Assert.True(vm.IsLoupeMode);
            }

            await Task.CompletedTask;
            var window = scope.Window!;
            // test-teardown-policy: allow - WithScene owns this MainWindow scope.
            scope.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var label = FormattableString.Invariant(
                $"{width}x{height} {(gray ? "gray" : "dark")} {(loupe ? "loupe" : "grid")}");
            var actions = Named<Button>(window, "BrowseActionsButton");
            var sync = Named<Button>(window, "SyncSettingsButton");
            var actionsClip = actions is { IsEffectivelyVisible: true } ? Clipped(window, actions) : (double?)null;
            var syncClip = sync is { IsEffectivelyVisible: true } ? Clipped(window, sync) : (double?)null;
            var (reachable, cardBottom, viewportBottom) = SelectionCardReachable(window);

            output.WriteLine(FormattableString.Invariant(
                $"SYNCSETTINGS G2 {label}: actions clipped={Describe(actionsClip)} sync clipped={Describe(syncClip)} selectionCard reachable={reachable} cardBottom={cardBottom:F1} viewportBottom={viewportBottom:F1}"));

            if (!loupe)
            {
                Assert.Equal(0, actionsClip);
            }

            Assert.NotNull(sync);
            Assert.Equal(0, syncClip);
            Assert.True(reachable, $"{label}: the Selection card's last line is not reachable.");
            Assert.True(viewportBottom <= sync.TranslatePoint(default, window)!.Value.Y,
                $"{label}: the details viewport overlaps the pinned footer.");
        });
    }

    private static T? Named<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == name);

    private static string Describe(double? clipped) =>
        clipped is { } value ? value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) : "absent";

    // Pixels of the control's box hidden by the window or a clipping ancestor
    // (sum of hidden width and hidden height).
    private static double Clipped(Window window, Control control)
    {
        var origin = control.TranslatePoint(default, window)!.Value;
        var box = new Rect(origin, control.Bounds.Size);
        var visible = box.Intersect(new Rect(window.ClientSize));

        foreach (var ancestor in control.GetVisualAncestors().OfType<Control>())
        {
            if (!ancestor.ClipToBounds || ReferenceEquals(ancestor, window)) continue;

            var ancestorOrigin = ancestor.TranslatePoint(default, window)!.Value;
            visible = visible.Intersect(new Rect(ancestorOrigin, ancestor.Bounds.Size));
        }

        return Math.Round(box.Width - visible.Width + box.Height - visible.Height, 1);
    }

    private static (bool Reachable, double CardBottom, double ViewportBottom) SelectionCardReachable(Window window)
    {
        var card = Named<Border>(window, "SelectionSummaryPanel")
            ?? throw new InvalidOperationException("SelectionSummaryPanel not found.");
        Assert.True(card.IsEffectivelyVisible, "The Selection card must show with 12 selected.");
        var scroll = card.GetVisualAncestors().OfType<ScrollViewer>().First();
        scroll.Offset = new Vector(0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var cardBottom = card.TranslatePoint(new Point(0, card.Bounds.Height), window)!.Value.Y;
        var viewportBottom = scroll.TranslatePoint(new Point(0, scroll.Viewport.Height), window)!.Value.Y;
        var windowBottom = window.ClientSize.Height;

        return (cardBottom <= viewportBottom + 0.5 && cardBottom <= windowBottom + 0.5, cardBottom, viewportBottom);
    }
}
