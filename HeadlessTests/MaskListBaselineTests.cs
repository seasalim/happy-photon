using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
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

namespace HappyPhoton.Tests;

public sealed class MaskListBaselineTests(ITestOutputHelper output)
{
    private static List<LocalAdjustment> Locals(bool mixed) =>
    [
        new() { Id = "00000000000000000000000000000001", Type = "linear", Ordinal = 1 },
        new() { Id = "00000000000000000000000000000002", Type = "radial", Ordinal = 2 },
        new() { Id = "00000000000000000000000000000003", Type = "brush", Ordinal = 3,
            Strokes = [new() { Points = [new(6000, 7000)] }] },
        new() { Id = "00000000000000000000000000000004", Type = "radial", Ordinal = 4, Enabled = !mixed },
        new() { Id = "00000000000000000000000000000005", Type = "brush", Ordinal = 5,
            Strokes = [new() { Points = [new(8000, 9000)] }],
            Luminance = mixed ? new() { Enabled = true, Lower = .3 } : null },
        new() { Id = "00000000000000000000000000000006", Type = "linear", Ordinal = 6, Enabled = !mixed,
            Luminance = mixed ? new() { Enabled = true, Lower = .3 } : null,
            Hue = mixed ? new() { Enabled = true, Center = 240 } : null }
    ];

    private static async Task WithList(bool mixed, Func<MainWindowViewModel, MainWindow, ListBox, Task> measure, int count = 6)
    {
        using var fixture = new CatalogVmFixture("mask-list-baseline");
        using var catalog = await fixture.CreateCatalogAsync();
        var path = GoldenTestPaths.Asset("srgb-reference.jpg");
        Assert.Equal(SourceAvailability.AvailableLocally, new SourceAvailabilityService().GetAvailability(path));
        await using var vm = fixture.CreateViewModel(catalog, new StandardBaseLoader(),
            _ => Task.CompletedTask, timeProvider: new TestTimeProvider());
        var image = new ImageFile(path) { CatalogId = await catalog.GetOrCreateImageAsync(path) };
        image.EditSettings.Locals = Locals(mixed).Take(count).ToList();
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.Browse.SetImages([image]);
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.SelectedLocal = vm.Locals[0];
        vm.ShowLocalMask = false;
        // test-teardown-policy: allow - using ForMainWindow below owns binding and window cleanup.
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var theme = new TestUiScope(theme: ThemeVariant.Dark);
        using var scope = TestUiScope.ForMainWindow(window, vm);
        Settle();
        window.GetVisualDescendants().OfType<ScrollViewer>()
            .Single(c => c.Name == "DevelopControlsScrollViewer").Offset = default;
        Settle();
        var list = window.GetVisualDescendants().OfType<ListBox>().Single(c => c.Name == "LocalList");
        Assert.Equal(count, list.ItemCount);
        Assert.True(list.IsEffectivelyVisible);

        await measure(vm, window, list);

        if (mixed)
        {
            list.GetVisualDescendants().OfType<ScrollViewer>().Single().Offset = default;
            Settle();
            ShowcaseTestHelper.Capture("develop-locals-list", scope, new PixelSize(1200, 700), ThemeVariant.Dark);
        }
    }

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static ListBoxItem Row(ListBox list, int index)
    {
        list.ScrollIntoView(index);
        Settle();

        return Assert.IsType<ListBoxItem>(list.ContainerFromIndex(index));
    }

    private static TextBlock Name(ListBoxItem item) => item.GetVisualDescendants().OfType<TextBlock>()
        .Single(t => t.Text == ((LocalRowViewModel)item.DataContext!).Name);

    [AvaloniaFact]
    public Task MixedGeometry() => WithList(true, (vm, window, list) =>
    {
        var expectedLabels = new[] { null, null, null, "Disabled", "Luminance", "Disabled · Luminance · Hue" };
        double? nameX = null;

        for (var i = 0; i < 6; i++)
        {
            var item = Row(list, i);
            var row = (LocalRowViewModel)item.DataContext!;
            var texts = item.GetVisualDescendants().OfType<TextBlock>().ToArray();
            var name = Name(item);
            var glyph = texts.Single(t => t.Text == row.Glyph);
            var checkbox = item.GetVisualDescendants().OfType<CheckBox>().Single();
            var delete = item.GetVisualDescendants().OfType<Button>().Single(b => b is not CheckBox);
            var controls = new Control[] { checkbox, glyph, name, delete };
            var centers = controls.Select(c => c.TranslatePoint(new Point(0, c.Bounds.Height / 2), item)!.Value.Y).ToArray();
            var grid = Assert.IsType<Grid>(name.Parent);
            var secondary = grid.Children.OfType<TextBlock>().Except([name, glyph]).ToArray();
            var tallest = controls.Max(c => c.Bounds.Height);
            var currentNameX = name.TranslatePoint(default, list)!.Value.X;
            Assert.InRange(centers.Max() - centers.Min(), 0, 1);
            var checkBoxVisual = checkbox.GetVisualDescendants().OfType<Border>()
                .Single(b => b.Name == "NormalRectangle");
            var checkBoxCenter = checkBoxVisual.TranslatePoint(new Point(0, checkBoxVisual.Bounds.Height / 2), item)!.Value.Y;
            Assert.InRange(Math.Abs(checkBoxCenter - centers[2]), 0, 1);
            nameX ??= currentNameX;
            Assert.InRange(Math.Abs(currentNameX - nameX.Value), 0, .5);
            Assert.Equal(expectedLabels[i], row.SecondaryLabel);

            Assert.Empty(secondary);
            Assert.Empty(grid.RowDefinitions);
            Assert.Equal(expectedLabels[i], ToolTip.GetTip(grid));
            Assert.Equal(24, item.Bounds.Height);
            Assert.True(item.Bounds.Height <= tallest + item.Padding.Top + item.Padding.Bottom);
            Assert.Equal(!row.Enabled, name.Classes.Contains("muted"));

            if (!row.Enabled)
            {
                AssertBrush(name, "TextMuted", name.Foreground);
                AssertBrush(glyph, "TextMuted", glyph.Foreground);
            }

            output.WriteLine($"W1 {row.Name}: item={item.Bounds.Height:F3}; padding={item.Padding}; " +
                $"controls=[{string.Join(",", controls.Select(c => c.Bounds.Height.ToString("F3")))}]; tallest={controls.Max(c => c.Bounds.Height):F3}");
            output.WriteLine($"W2 {row.Name}: centers checkbox/glyph/name/delete=[{string.Join(",", centers.Select(c => c.ToString("F3")))}]; " +
                $"spread={centers.Max() - centers.Min():F3}; nameX-in-list={name.TranslatePoint(default, list)!.Value.X:F3}");
            output.WriteLine($"W3 {row.Name}: secondary-count={secondary.Length}; tooltip='{ToolTip.GetTip(grid)}'; muted={!row.Enabled}");
        }

        return Task.CompletedTask;
    });

    private static Rect BoundsIn(Control control, Visual relativeTo) =>
        new(control.TranslatePoint(default, relativeTo)!.Value, control.Bounds.Size);

    [AvaloniaFact]
    public Task ScrollingGutter() => WithList(false, (vm, window, list) =>
    {
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
        var viewport = scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().Single();
        var bar = scroll.GetVisualDescendants().OfType<ScrollBar>()
            .Single(b => b.Orientation == Avalonia.Layout.Orientation.Vertical);
        Assert.True(bar.IsEffectivelyVisible);
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        var barBounds = BoundsIn(bar, list);
        Assert.InRange(barBounds.Width, 1, 8);
        Assert.Empty(bar.GetVisualDescendants().OfType<RepeatButton>());
        var overlaps = new List<double>();
        output.WriteLine($"W6 scrollbar={barBounds}; repeat-buttons={bar.GetVisualDescendants().OfType<RepeatButton>().Count()}");

        foreach (var item in list.GetVisualDescendants().OfType<ListBoxItem>())
        {
            var itemBounds = BoundsIn(item, viewport);

            if (itemBounds.Bottom <= 0 || itemBounds.Top >= viewport.Bounds.Height) continue;

            var delete = item.GetVisualDescendants().OfType<Button>().Single(b => b is not CheckBox);
            var highlight = item.GetVisualDescendants().OfType<ContentPresenter>()
                .Single(p => p.Name == "PART_ContentPresenter" && ReferenceEquals(p.TemplatedParent, item));

            foreach (var control in new Control[] { item, delete, highlight })
            {
                var bounds = BoundsIn(control, list);
                var intersection = bounds.Intersect(barBounds);
                var overlap = intersection.Width * intersection.Height;
                overlaps.Add(overlap);
                output.WriteLine($"W6 {item.DataContext}/{control.GetType().Name}: bounds={bounds}; overlap={overlap:F3}");
            }
        }

        Assert.True(overlaps.Count >= 15);
        Assert.All(overlaps, overlap => Assert.Equal(0, overlap));
        var first = Row(list, 0);
        Assert.True(first.IsSelected);
        var button = first.GetVisualDescendants().OfType<Button>().Single(b => b is not CheckBox);
        var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        var hitsButton = window.InputHitTest(center) is Visual hit &&
            (ReferenceEquals(hit, button) || hit.GetVisualAncestors().Contains(button));
        output.WriteLine($"W6 delete-center-hits-button={hitsButton}");
        Assert.True(hitsButton);
        var deleted = ((LocalRowViewModel)first.DataContext!).Local;
        window.MouseMove(center);
        window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        Settle();
        Assert.True(button.IsPressed);
        window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
        Settle();
        Assert.Equal(5, vm.Locals.Count);
        Assert.DoesNotContain(deleted, vm.Locals);
        Assert.False(bar.IsEffectivelyVisible);
        Assert.Equal(220, Row(list, 0).Bounds.Width);

        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task ScrollInput() => WithList(false, (vm, window, list) =>
    {
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
        var bar = scroll.GetVisualDescendants().OfType<ScrollBar>()
            .Single(b => b.Orientation == Avalonia.Layout.Orientation.Vertical);
        var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
        var thumbBorder = Assert.Single(thumb.GetVisualDescendants().OfType<Border>());
        AssertBrush(thumbBorder, "TextMuted", thumbBorder.Background);
        Assert.Equal(new CornerRadius(0), thumbBorder.CornerRadius);
        var point = list.TranslatePoint(new Point(100, 60), window)!.Value;
        window.MouseMove(point);
        window.MouseWheel(point, new Vector(0, -1), RawInputModifiers.None);
        Settle();
        output.WriteLine($"W6 wheel offset={scroll.Offset.Y:F3}");
        Assert.True(scroll.Offset.Y > 0);
        window.MouseWheel(point, new Vector(0, 1), RawInputModifiers.None);
        Settle();
        Assert.Equal(0, scroll.Offset.Y);

        var start = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
        window.MouseMove(start);
        Settle();
        output.WriteLine($"W6 hover width={bar.Bounds.Width:F3}; thumb={thumb.Bounds}");
        Assert.InRange(bar.Bounds.Width, 1, 8);
        Assert.InRange(thumb.Bounds.Width, 1, 8);
        AssertBrush(thumbBorder, "TextSecondary", thumbBorder.Background);
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        var end = start + new Vector(0, 15);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        Settle();
        output.WriteLine($"W6 drag offset={scroll.Offset.Y:F3}; width={bar.Bounds.Width:F3}");
        Assert.True(scroll.Offset.Y > 0);
        Assert.InRange(bar.Bounds.Width, 1, 8);
        Assert.InRange(thumb.Bounds.Width, 1, 8);
        AssertBrush(thumbBorder, "TextSecondary", thumbBorder.Background);
        window.MouseUp(end, MouseButton.Left, RawInputModifiers.None);
        Settle();

        Assert.True(Row(list, 0).Focus());

        for (var i = 0; i < 5; i++)
        {
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            Settle();
        }

        output.WriteLine($"W6 keyboard offset={scroll.Offset.Y:F3}; selected={list.SelectedIndex}");
        Assert.Equal(5, list.SelectedIndex);
        Assert.True(scroll.Offset.Y > 0);

        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task NonScrollingWidth() => WithList(false, (vm, window, list) =>
    {
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
        var bar = scroll.GetVisualDescendants().OfType<ScrollBar>()
            .Single(b => b.Orientation == Avalonia.Layout.Orientation.Vertical);
        Assert.False(bar.IsEffectivelyVisible);
        Assert.True(scroll.Extent.Height <= scroll.Viewport.Height);
        var item = Row(list, 0);
        output.WriteLine($"W6 non-scrolling row-width={item.Bounds.Width:F3}; list-width={list.Bounds.Width:F3}");
        // Frozen on 30fa455 before the scrollbar gutter change.
        Assert.Equal(220, item.Bounds.Width);
        Assert.Equal(list.Bounds.Width, item.Bounds.Width);

        return Task.CompletedTask;
    }, count: 3);

    private static void AssertBrush(Control control, string resource, IBrush? actual)
    {
        Assert.True(control.TryFindResource(resource, control.ActualThemeVariant, out var expected));
        Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(expected).Color,
            Assert.IsAssignableFrom<ISolidColorBrush>(actual).Color);
    }

    [AvaloniaFact]
    public Task DeleteButtonStates() => WithList(false, (vm, window, list) =>
    {
        var delete = Row(list, 0).GetVisualDescendants().OfType<Button>().Single(b => b is not CheckBox);
        var presenter = delete.GetVisualDescendants().OfType<ContentPresenter>().Single();
        var glyph = delete.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single();
        Assert.Equal(new Size(20, 20), delete.Bounds.Size);
        Assert.Equal(default, presenter.BorderThickness);
        Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color.A);
        AssertBrush(glyph, "TextMuted", glyph.Stroke);
        var center = glyph.TranslatePoint(glyph.Data!.Bounds.Center, delete)!.Value;
        Assert.InRange(Math.Abs(center.X - 10), 0, .5);
        Assert.InRange(Math.Abs(center.Y - 10), 0, .5);
        output.WriteLine($"R1-2 hit-area={delete.Bounds.Size}; glyph-center={center}");

        // The scrollbar gutter leaves the center of the hit area unobstructed.
        var point = delete.TranslatePoint(new Point(10, 10), window)!.Value;
        window.MouseMove(point);
        Settle();
        Assert.True(delete.IsPointerOver);
        AssertBrush(presenter, "ControlHover", presenter.Background);
        AssertBrush(glyph, "TextPrimary", glyph.Stroke);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        Settle();
        Assert.True(delete.IsPressed);
        AssertBrush(presenter, "ButtonBackgroundPressed", presenter.Background);
        window.MouseMove(new Point(0, 0));
        window.MouseUp(new Point(0, 0), MouseButton.Left, RawInputModifiers.None);
        Settle();
        Assert.Equal(6, vm.Locals.Count);
        Assert.True(Row(list, 0).Focus());
        Assert.True(delete.Focus(NavigationMethod.Tab));
        Settle();
        Assert.True(delete.IsKeyboardFocusWithin);
        Assert.Contains(":focus-visible", delete.Classes);
        var layer = Assert.IsType<AdornerLayer>(AdornerLayer.GetAdornerLayer(delete));
        var focus = Assert.Single(layer.Children, c => AdornerLayer.GetAdornedElement(c) == delete);
        Assert.True(focus.IsEffectivelyVisible);
        Assert.True(focus.Bounds.Width >= delete.Bounds.Width && focus.Bounds.Height >= delete.Bounds.Height);
        output.WriteLine("R1-2 hover/pressed brushes and keyboard focus passed");

        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task UnrangedViewport() => WithList(false, (vm, window, list) =>
    {
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
        var viewport = scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().Single();
        Assert.Equal(default, scroll.Offset);
        var items = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
        var fullyVisible = items.Count(item =>
        {
            var origin = item.TranslatePoint(default, viewport)!.Value;

            return item.IsEffectivelyVisible && origin.Y >= 0 && origin.Y + item.Bounds.Height <= viewport.Bounds.Height;
        });
        Assert.True(fullyVisible >= 5, $"Only {fullyVisible} rows fit in the initial viewport.");
        output.WriteLine($"W4 full={fullyVisible}; list={list.Bounds}; viewport={viewport.Bounds}; " +
            $"offset={scroll.Offset}; extent={scroll.Extent}; realized={items.Length}; " +
            $"items=[{string.Join(";", items.Select(i => $"{i.DataContext}:{i.TranslatePoint(default, viewport)}:{i.Bounds.Height}"))}]");

        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task RealInputOutcomes() => WithList(false, (vm, window, list) =>
    {
        // Frozen on 7e376986 through native headless input, not command invocation.
        var expected = new[]
        {
            "1/111111//True/", "2/111111/ListBoxItem/True/", "2/101111//True/",
            "2/101111//False/", "1/101111/ListBoxItem/False/",
            "2/101111/ListBoxItem/False/", "3/101111/ListBoxItem/False/",
            "2/101111/ListBoxItem/False/", "1/101111/ListBoxItem/False/",
            "1/101111/ListBoxItem/False/", "1/101111/ListBoxItem/False/",
            "1/101111/ListBoxItem/False/", "2/01111//False/",
            "2/01111//False/Move to Trash", "2/01111/ListBoxItem/False/"
        };
        var step = 0;

        void State(string action)
        {
            var focus = window.FocusManager!.GetFocusedElement()?.GetType().Name;
            var dialogs = string.Join(",", window.OwnedWindows.Select(w => w.Title));
            var enabled = string.Concat(vm.Locals.Select(l => l.Enabled ? "1" : "0"));
            output.WriteLine($"W5 {action}: selected={vm.SelectedLocal?.Name}; " +
                $"index={list.SelectedIndex}; locals={string.Join(",", vm.Locals.Select(l => $"{l.Name}:{l.Enabled}"))}; " +
                $"focus={focus}; fit={vm.IsZoomFitMode}; zoom={vm.ZoomLevel:F3}; dialogs={dialogs}");
            Assert.Equal(expected[step++], $"{vm.SelectedLocal?.Ordinal}/{enabled}/{focus}/{vm.IsZoomFitMode}/{dialogs}");

            foreach (var item in list.GetVisualDescendants().OfType<ListBoxItem>())
            {
                var row = Assert.IsType<LocalRowViewModel>(item.DataContext);
                var name = Name(item);
                var grid = Assert.IsType<Grid>(name.Parent);
                Assert.Equal(row.Enabled ? null : "Disabled", ToolTip.GetTip(grid));
                Assert.Equal(2, grid.Children.OfType<TextBlock>().Count());
                Assert.Equal(!row.Enabled, name.Classes.Contains("muted"));

                if (!row.Enabled)
                {
                    AssertBrush(name, "TextMuted", name.Foreground);
                }
            }
        }

        void Click(Control control, Window? target = null)
        {
            target ??= window;
            var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), target)!.Value;
            Assert.True(target.InputHitTest(point) is Visual hit &&
                (ReferenceEquals(hit, control) || hit.GetVisualAncestors().Contains(control)),
                $"{control.GetType().Name} is obstructed at {point}.");

            target.MouseMove(point);
            target.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
            target.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
            Settle();
        }

        void KeyInput(Key key)
        {
            window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
            Settle();
            State(key.ToString());
        }

        State("initial");
        Click(Name(Row(list, 1)));
        State("click-Radial-2");
        Click(Row(list, 1).GetVisualDescendants().OfType<CheckBox>().Single());
        State("click-checkbox-Radial-2");
        KeyInput(Key.Space);
        Click(Name(Row(list, 0)));
        State("click-Linear-1");
        KeyInput(Key.Down);
        KeyInput(Key.Down);
        KeyInput(Key.Up);
        KeyInput(Key.Up);
        KeyInput(Key.Right);
        KeyInput(Key.Left);
        KeyInput(Key.Space);
        // Keep the frozen outcomes while clicking the now-unobstructed center.
        Click(Row(list, 0).GetVisualDescendants().OfType<Button>().Single(b => b is not CheckBox));
        State("click-delete-first-remaining");
        Click(Name(Row(list, 0)));
        KeyInput(Key.Delete);
        var dialog = Assert.Single(window.OwnedWindows);

        try
        {
            output.WriteLine($"W5 dialog: title={dialog.Title}; text=" +
                string.Join(" | ", dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)));
            var cancel = dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "No"));
            Click(cancel, dialog);
            State("cancel-delete-dialog");
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close(false);
        }

        Assert.Equal(expected.Length, step);

        return Task.CompletedTask;
    });
}
