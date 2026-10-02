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

public sealed partial class DevelopControlBarTests
{
    private static readonly string[] MenuButtons =
    [
        "ZoomFitButton", "ActualSizeButton", "ColorAssessmentButton",
        "FullScreenButton", "BeforeAfterSplitButton", "RawJpegSwitchButton"
    ];

    [AvaloniaFact]
    public async Task MenuMatchesCommandsChecksEnabledStatesAndPrimaryGestures()
    {
        await WithWindow(800, (window, vm) =>
        {
            var pane = window.FindControl<DevelopViewerPane>("DevelopViewerPane")!;
            var button = pane.FindControl<Button>("DevelopViewActionsButton")!;
            var menu = Assert.IsType<MenuFlyout>(button.Flyout);
            Click(button);
            Settle(window);

            try
            {
                Assert.True(menu.IsOpen);
                var items = menu.Items.Cast<MenuItem>().ToArray();
                Assert.Equal(new[] { "Fit", "1:1", "Assess", "Fullscreen", "Before | After", "Switch JPEG / RAW" },
                    items.Select(item => item.Header).Cast<string>().ToArray());
                Assert.Null(items[0].InputGesture);
                Assert.Equal(new[] { "Space", "L", "F", "Y", "Shift+R" },
                    items.Skip(1).Select(item => item.InputGesture!.ToString()).ToArray());

                for (var i = 0; i < items.Length; i++)
                {
                    var original = pane.FindControl<Button>(MenuButtons[i])!;
                    Assert.Same(original.Command, items[i].Command);

                    if (items[i].InputGesture is { } gesture)
                    {
                        var tooltip = Assert.IsType<string>(ToolTip.GetTip(original));
                        Assert.StartsWith(gesture.ToString(), tooltip[(tooltip.IndexOf('(') + 1)..]);
                    }
                }

                var pairedJpeg = vm.SelectedImage;
                Action[] changes =
                [
                    () => { },
                    () => vm.ToggleActualSizeCommand.Execute(null),
                    () => vm.ZoomFitCommand!.Execute(null),
                    () => vm.ToggleColorAssessmentModeCommand.Execute(null),
                    () => vm.ToggleColorAssessmentModeCommand.Execute(null),
                    () => vm.ToggleBeforeAfterSplitCommand.Execute(null),
                    () => vm.ToggleBeforeAfterSplitCommand.Execute(null),
                    () => vm.SwitchCaptureMemberCommand.Execute(null),
                    () => vm.SwitchCaptureMemberCommand.Execute(null),
                    () => vm.SelectedImage = vm.Browse.AllImages.Single(image => image.FileName == "other.jpg"),
                    () => vm.SelectedImage = pairedJpeg,
                    () => vm.IsCropMode = true,
                    () => vm.IsCropMode = false
                ];

                foreach (var change in changes)
                {
                    change();
                    Settle(window);

                    for (var i = 0; i < items.Length; i++)
                    {
                        var original = pane.FindControl<Button>(MenuButtons[i])!;
                        Assert.Equal(original.IsEffectivelyEnabled, items[i].IsEffectivelyEnabled);

                        if (original is ToggleButton toggle)
                        {
                            Assert.Equal(MenuItemToggleType.CheckBox, items[i].ToggleType);
                            Assert.Equal(toggle.IsChecked == true, items[i].IsChecked);
                        }
                    }

                    Assert.Equal(!vm.IsCropMode, items[3].IsEnabled);
                    Assert.Equal(vm.CanSwitchCaptureMember, items[5].IsEnabled);
                }
            }
            finally
            {
                menu.Hide();
            }

            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task DismissalAndChoosingAnItemReturnViewerShortcuts()
    {
        await WithWindow(800, (window, vm) =>
        {
            var pane = window.FindControl<DevelopViewerPane>("DevelopViewerPane")!;
            var button = pane.FindControl<Button>("DevelopViewActionsButton")!;
            var menu = Assert.IsType<MenuFlyout>(button.Flyout);

            try
            {
                pane.Focus();
                Click(button);
                Settle(window);
                Assert.True(menu.IsOpen);
                var first = menu.Items.Cast<MenuItem>().First();
                Assert.True(first.Focus());
                Press(window, Key.Escape);
                Settle(window);
                Assert.False(menu.IsOpen);
                Assert.True(vm.IsDevelopMode);
                AssertViewerShortcuts(window, pane, vm);

                Click(button);
                Settle(window);
                var before = vm.IsColorAssessmentMode;
                Assert.True(menu.Items.Cast<MenuItem>().Single(item => Equals(item.Header, "Assess")).Focus());
                Press(window, Key.Enter);
                Settle(window);
                Assert.Equal(!before, vm.IsColorAssessmentMode);
                Assert.False(menu.IsOpen);
                AssertViewerShortcuts(window, pane, vm);
                Press(window, Key.Escape);
                Assert.False(vm.IsDevelopMode);
            }
            finally
            {
                menu.Hide();
            }

            return Task.CompletedTask;
        });
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FullscreenKeepsViewerShortcutsAfterClick(bool overflow, bool keyboard)
    {
        await WithWindow(overflow ? 800 : 1692, (window, vm) =>
        {
            var pane = window.FindControl<DevelopViewerPane>("DevelopViewerPane")!;

            if (overflow)
            {
                var button = pane.FindControl<Button>("DevelopViewActionsButton")!;
                var menu = Assert.IsType<MenuFlyout>(button.Flyout);
                Click(button);
                Settle(window);

                try
                {
                    var item = menu.Items.Cast<MenuItem>().Single(item => Equals(item.Header, "Fullscreen"));

                    if (keyboard)
                    {
                        Assert.True(item.Focus());
                        Press(window, Key.Enter);
                    }
                    else
                    {
                        Click(item);
                    }

                    Assert.False(menu.IsOpen);
                }
                finally
                {
                    menu.Hide();
                }
            }
            else
            {
                Click(pane.FindControl<Button>("FullScreenButton")!);
            }

            Settle(window);
            Assert.True(vm.IsFullScreenMode);
            Assert.False(pane.IsEffectivelyVisible);

            if (overflow)
            {
                var viewer = window.FindControl<ZoomPanControl>("FullScreenZoomPanControl")!;
                Assert.True(viewer.IsEffectivelyVisible);
                Assert.Same(viewer, window.FocusManager!.GetFocusedElement());
            }

            var fit = vm.IsZoomFitMode;
            Press(window, Key.Space);
            Assert.Equal(!fit, vm.IsZoomFitMode);
            Press(window, Key.Z);
            Assert.Equal(fit, vm.IsZoomFitMode);
            var selected = vm.SelectedImage;
            Press(window, Key.Right);
            Assert.NotSame(selected, vm.SelectedImage);
            Press(window, Key.Left);
            Assert.Same(selected, vm.SelectedImage);

            return Task.CompletedTask;
        });
    }

    [AvaloniaTheory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    public async Task ChoosingEachItemInvokesItsViewCommand(int index, bool keyboard)
    {
        await WithWindow(800, (window, vm) =>
        {
            var pane = window.FindControl<DevelopViewerPane>("DevelopViewerPane")!;
            var button = pane.FindControl<Button>("DevelopViewActionsButton")!;
            var menu = Assert.IsType<MenuFlyout>(button.Flyout);
            vm.IsZoomFitMode = index != 0;
            Click(button);
            Settle(window);

            try
            {
                Assert.True(menu.IsOpen);
                var item = menu.Items.Cast<MenuItem>().ElementAt(index);

                if (keyboard)
                {
                    Assert.True(item.Focus());
                    Press(window, Key.Enter);
                }
                else
                {
                    Click(item);
                }

                Settle(window);
                Assert.False(menu.IsOpen);
                Assert.True(index switch
                {
                    0 => vm.IsZoomFitMode,
                    1 => !vm.IsZoomFitMode,
                    2 => vm.IsColorAssessmentMode,
                    3 => vm.IsFullScreenMode,
                    4 => vm.IsBeforeAfterSplit,
                    5 => vm.IsViewingPairedRaw,
                    _ => false
                });
            }
            finally
            {
                menu.Hide();
            }

            return Task.CompletedTask;
        });
    }

    private static void AssertViewerShortcuts(MainWindow window, DevelopViewerPane pane, MainWindowViewModel vm)
    {
        Assert.Same(pane, window.FocusManager!.GetFocusedElement());
        var fit = vm.IsZoomFitMode;
        Press(window, Key.Space);
        Assert.Equal(!fit, vm.IsZoomFitMode);
        Press(window, Key.Z);
        Assert.Equal(fit, vm.IsZoomFitMode);
        var selected = vm.SelectedImage;
        Press(window, Key.Right);
        Assert.NotSame(selected, vm.SelectedImage);
        Press(window, Key.Left);
        Assert.Same(selected, vm.SelectedImage);
    }

    private static void Click(Control control)
    {
        var root = TopLevel.GetTopLevel(control)!;
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
        root.MouseMove(point);
        root.MouseDown(point, MouseButton.Left);
        root.MouseUp(point, MouseButton.Left);
    }

    private static void Press(TopLevel root, Key key)
    {
        root.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        root.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
    }
}
