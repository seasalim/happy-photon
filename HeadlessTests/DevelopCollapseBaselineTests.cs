using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DevelopCollapseBaselineTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task ClickSpaceAndEnterToggleOnlyTheFocusedGroup()
    {
        await WithWindow((vm, window, groups, scroll) =>
        {
            foreach (var group in groups)
            {
                foreach (var input in new[] { "click", "Space", "Enter" })
                {
                    var before = vm.DevelopGroupList.Select(item => item.IsExpanded).ToArray();
                    var header = Header(group);
                    header.BringIntoView();
                    Settle(window);

                    if (input == "click") Click(window, header);
                    else
                    {
                        Assert.True(header.Focus());
                        Press(window, input == "Space" ? Key.Space : Key.Enter);
                    }

                    Settle(window);
                    Assert.Equal(!before[Array.IndexOf(groups, group)], group.IsExpanded);
                    Assert.Equal(group.IsExpanded, vm.DevelopGroupList[Array.IndexOf(groups, group)].IsExpanded);
                    Assert.Single(groups.Where((item, i) => item.IsExpanded != before[i]));
                    output.WriteLine($"C2 {group.Header}: {input} toggles exactly one group");
                }
            }

            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task AltClickSolosOnceAndAnchorsOrClamps()
    {
        await WithWindow((vm, window, groups, scroll) =>
        {
            var saves = 0;
            vm.PersistAppSettingsAsync = () => { saves++; return Task.CompletedTask; };

            foreach (var group in groups)
            {
                vm.RestoreDevelopGroups(new Dictionary<string, bool>());
                scroll.Offset = default;
                Settle(window);
                var header = Header(group);
                var origin = header.TranslatePoint(default, scroll)!.Value.Y;
                scroll.Offset = new Vector(0, origin - scroll.Viewport.Height / 2);
                Settle(window);
                var before = header.TranslatePoint(default, scroll)!.Value.Y;
                var beforeSaves = saves;
                Click(window, header, RawInputModifiers.Alt);
                Settle(window);
                Assert.All(groups, item => Assert.Equal(item == group, item.IsExpanded));
                Assert.Equal(beforeSaves + 1, saves);
                var after = header.TranslatePoint(default, scroll)!.Value.Y;
                var max = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
                var required = scroll.Offset.Y + after - before;
                Assert.Equal(Math.Clamp(required, 0, max), scroll.Offset.Y, 1);
                Assert.True(Math.Abs(after - before) <= 1 || required < 0 || required > max);
                output.WriteLine($"C3/C4 {group.Header}: Y {before} -> {after}; offset {scroll.Offset.Y}; max {max}");
            }

            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task ToolsKeepMixedStateAndDisableHeaderInput()
    {
        await WithWindow(async (vm, window, groups, scroll) =>
        {
            vm.RestoreDevelopGroups(vm.DevelopGroupList.Select((item, i) => (item.Name, Expanded: i % 2 == 0))
                .ToDictionary(item => item.Name, item => item.Expanded));
            var expected = vm.CaptureDevelopGroups();

            foreach (var tool in new[] { "crop", "locals", "spots" })
            {
                if (tool == "crop") await vm.ToggleCropModeCommand.ExecuteAsync(null);
                else if (tool == "locals") await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
                else await vm.ToggleSpotsModeCommand.ExecuteAsync(null);

                Settle(window);

                foreach (var group in groups)
                {
                    var header = Header(group);
                    Assert.False(header.IsEffectivelyEnabled);
                    Assert.False(header.Focus());
                    header.BringIntoView();
                    Settle(window);
                    Click(window, header);
                    Click(window, header, RawInputModifiers.Alt);
                    header.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
                    Assert.Equal(expected, vm.CaptureDevelopGroups());
                }

                if (tool == "crop") await vm.CancelCropCommand.ExecuteAsync(null);
                else if (tool == "locals") vm.CloseLocalsCommand.Execute(null);
                else vm.CloseSpotsCommand.Execute(null);

                Settle(window);
                Assert.Equal(expected, vm.CaptureDevelopGroups());
                Assert.All(groups, group => Assert.True(Header(group).IsEffectivelyEnabled));
            }
        });
    }

    [AvaloniaFact]
    public async Task EyedropperButtonAndWExpandWhiteBalance()
    {
        await WithWindow((vm, window, groups, scroll) =>
        {
            vm.WhiteBalanceGroup.IsExpanded = false;
            window.FocusManager!.Focus(null);
            Press(window, Key.W);
            Assert.True(vm.IsWhiteBalancePicking);
            Assert.True(vm.WhiteBalanceGroup.IsExpanded);
            vm.IsWhiteBalancePicking = false;
            vm.WhiteBalanceGroup.IsExpanded = false;
            var button = window.GetVisualDescendants().OfType<ToggleButton>()
                .Single(item => item.Name == "WhiteBalancePickerButton");
            Assert.Same(vm.ToggleWhiteBalancePickerCommand, button.Command);
            button.Command!.Execute(null);
            Assert.True(vm.IsWhiteBalancePicking);
            Assert.True(vm.WhiteBalanceGroup.IsExpanded);

            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task CollapsedGroupsSkipTheirControlsInTabOrder()
    {
        await WithWindow((vm, window, groups, scroll) =>
        {
            vm.RestoreDevelopGroups(vm.DevelopGroupList.ToDictionary(item => item.Name, _ => false));
            Settle(window);
            Assert.True(Header(groups[0]).Focus());

            foreach (var group in groups.Skip(1))
            {
                Press(window, Key.Tab);
                Assert.Same(Header(group), window.FocusManager!.GetFocusedElement());
            }

            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task MonochromeContentDoesNotDisableGroupHeaders()
    {
        await WithWindow((vm, window, groups, scroll) =>
        {
            using var pixels = new ImageMagick.MagickImage(ImageMagick.MagickColors.Gray, 16, 12);
            vm.ApplyPreviewRefresh(vm.SelectedImage!,
                HappyPhoton.Services.BitmapConversionService.ConvertToBitmap(pixels)!,
                new HappyPhoton.Models.HistogramData(), hasHistogram: true, rawHistogram: null,
                vm.LatestPreviewOutcomeGeneration, isRawSource: true, isMonochrome: true);
            Settle(window);
            Assert.False(vm.IsColorEditingEnabled);

            foreach (var group in groups.Take(2))
            {
                Assert.False(((Control)group.Content!).IsEffectivelyEnabled);
                var header = Header(group);
                Assert.True(header.IsEffectivelyEnabled);
                header.BringIntoView();
                Settle(window);
                Click(window, header);
                Settle(window);
                Assert.False(group.IsExpanded);
                Assert.True(header.Focus());
                Press(window, Key.Space);
                Assert.True(group.IsExpanded);
            }

            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task AltClickSolosEachHeaderFromMixedStates()
    {
        await WithWindow((vm, window, groups, scroll) =>
        {
            foreach (var group in groups)
            {
                vm.RestoreDevelopGroups(vm.DevelopGroupList.Select((item, i) => (item.Name, Expanded: i % 2 == 0))
                    .ToDictionary(item => item.Name, item => item.Expanded));
                Settle(window);
                var header = Header(group);
                header.BringIntoView();
                Settle(window);
                Click(window, header, RawInputModifiers.Alt);
                Settle(window);
                Assert.All(groups, item => Assert.Equal(item == group, item.IsExpanded));
            }

            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task EnterWithoutHeaderFocusKeepsBrowseLoupeDevelopLadder()
    {
        await WithWindow((vm, window, groups, scroll) =>
        {
            var expected = vm.CaptureDevelopGroups();
            vm.IsDevelopMode = false;
            window.FocusManager!.Focus(null);
            Press(window, Key.Enter);
            Settle(window);
            Assert.True(vm.IsLoupeMode);
            Press(window, Key.Enter);
            Settle(window);
            Assert.True(vm.IsDevelopMode);
            Assert.False(vm.IsLoupeMode);
            Assert.Equal(expected, vm.CaptureDevelopGroups());

            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task HeaderPaddingAndBleedUseOneHoverAndPressedFill()
    {
        await WithWindow((vm, window, groups, scroll) =>
        {
            var group = groups[0];
            var header = Header(group);
            var borders = header.GetVisualDescendants().OfType<Border>().ToArray();
            var fill = borders.Single(border => border.Name == "ToggleButtonBackground");
            var chevron = borders.Single(border => border.Name == "ExpandCollapseChevronBorder");
            // Isolate the row from the window's outer 12 px resize grip.
            window.CanResize = false;
            var points = new[]
            {
                new Point(20, 4), new Point(20, 28),
                new Point(3, 25), new Point(header.Bounds.Width - 3, 25)
            };

            foreach (var point in points)
            {
                header.BringIntoView();
                Settle(window);
                var location = header.TranslatePoint(point, window)!.Value;
                window.MouseMove(location);
                Settle(window);
                Assert.True(header.IsPointerOver, $"Header must receive pointer at {point}");
                Assert.Equal(ThemeResourceTests.Brush("ControlHover", window.ActualThemeVariant).Color,
                    Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(fill.Background).Color);
                Assert.Equal(Avalonia.Media.Colors.Transparent,
                    Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(chevron.Background).Color);
                var before = group.IsExpanded;
                window.MouseDown(location, MouseButton.Left);
                Settle(window);
                Assert.True(header.IsPressed);
                Assert.Equal(ThemeResourceTests.Brush("SurfaceHighest", window.ActualThemeVariant).Color,
                    Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(fill.Background).Color);
                Assert.Equal(Avalonia.Media.Colors.Transparent,
                    Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(chevron.Background).Color);
                window.MouseUp(location, MouseButton.Left);
                Settle(window);
                Assert.Equal(!before, group.IsExpanded);
                Assert.Equal(new Size(250, 32), fill.Bounds.Size);
                Assert.Equal(new CornerRadius(0), header.CornerRadius);
            }

            return Task.CompletedTask;
        });
    }

    internal static Task WithWindow(Func<MainWindowViewModel, Window, DevelopGroup[], ScrollViewer, Task> run) =>
        DevelopToolsBaselineTests.WithScene("normal", 1200, 700, async (vm, scope) =>
        {
            using var theme = new TestUiScope(theme: ThemeVariant.Dark);
            // test-teardown-policy: allow - WithScene owns and disposes this MainWindow scope.
            scope.Show();
            var window = scope.Window!;
            Settle(window);
            var panel = window.GetVisualDescendants().OfType<DevelopEditPanel>().Single();
            var scroll = panel.FindControl<ScrollViewer>("DevelopControlsScrollViewer")!;
            var groups = panel.GetVisualDescendants().OfType<DevelopGroup>().ToArray();
            Assert.Equal(10, groups.Length);

            await run(vm, window, groups, scroll);
        });

    internal static ToggleButton Header(DevelopGroup group) =>
        group.GetVisualDescendants().OfType<ToggleButton>().Single(c => c.Name == "ExpanderHeader");

    internal static void Click(Window window, Control header, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var point = header.TranslatePoint(new Point(20, 8), window)!.Value;
        window.MouseDown(point, MouseButton.Left, modifiers);
        window.MouseUp(point, MouseButton.Left, modifiers);
    }

    internal static void Press(Window window, Key key)
    {
        var physical = key switch
        {
            Key.Space => PhysicalKey.Space,
            Key.Enter => PhysicalKey.Enter,
            Key.Tab => PhysicalKey.Tab,
            _ => PhysicalKey.W
        };
        window.KeyPress(key, RawInputModifiers.None, physical, null);
        window.KeyRelease(key, RawInputModifiers.None, physical, null);
    }

    internal static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
    }
}
