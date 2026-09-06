using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DevelopToolsBaselineTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData("develop-tools-normal", "normal", false, 1200, 700)]
    [InlineData("develop-tools-crop", "crop", false, 1200, 700)]
    [InlineData("develop-tools-locals-linear", "locals", false, 1200, 700)]
    [InlineData("develop-tools-crop-gray", "crop", true, 800, 500)]
    public async Task RenderScene(string scene, string mode, bool gray, int width, int height)
    {
        await WithScene(mode, width, height, async (vm, scope) =>
        {
            ShowcaseTestHelper.Capture(scene, scope, new PixelSize(width, height),
                gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark, window =>
                {
                    window.GetVisualDescendants().OfType<ScrollViewer>()
                        .Single(control => control.Name == "DevelopControlsScrollViewer").Offset = default;
                    Assert.True(vm.PreviewImage is not null);
                    Assert.True(window.Bounds.Width > 0 && window.Bounds.Height > 0);
                    if (mode == "crop")
                    {
                        var overlay = window.GetVisualDescendants().OfType<CropOverlayControl>().Single();
                        Assert.True(overlay.IsEffectivelyVisible);
                        Assert.True(overlay.Bounds.Width > 0 && overlay.Bounds.Height > 0);
                    }
                    if (mode == "locals")
                    {
                        var overlay = window.GetVisualDescendants().OfType<LocalsOverlayControl>().Single();
                        Assert.True(overlay.IsEffectivelyVisible);
                        Assert.True(overlay.Bounds.Width > 0 && overlay.Bounds.Height > 0);
                        Assert.True(vm.HasSelectedLocal);
                    }
                });
            await Task.CompletedTask;
        });
    }

    // Observation only: no acceptance threshold or dependency on current placement.
    [AvaloniaFact]
    public async Task CensusAndScrollExtent()
    {
        foreach (var size in new[] { new PixelSize(1200, 700), new PixelSize(800, 500) })
        foreach (var mode in new[] { "normal", "crop", "locals" })
        {
            await WithScene(mode, size.Width, size.Height, async (vm, scope) =>
            {
                using var theme = new TestUiScope(theme: ThemeVariant.Dark);
                // test-teardown-policy: allow - WithScene owns and disposes the supplied MainWindow scope.
                scope.Show();
                Dispatcher.UIThread.RunJobs();
                using var frame = scope.Window!.CaptureRenderedFrame();
                Assert.NotNull(frame);
                var window = scope.Window!;
                var viewer = window.GetVisualDescendants().OfType<DevelopViewerPane>().Single();
                var panel = window.GetVisualDescendants().OfType<DevelopEditPanel>().Single();
                output.WriteLine($"FRAME {size.Width}x{size.Height} {mode} Dark");
                foreach (var pane in new Control[] { viewer, panel })
                {
                    var names = pane.GetLogicalDescendants().OfType<Control>()
                        .Select(control => ToolIdentity(control, vm)).OfType<string>().Order().ToArray();
                    output.WriteLine($"{pane.GetType().Name}: tool-entry/crop count={names.Length}; {string.Join(", ", names)}");
                }
                var locals = panel.GetLogicalDescendants().OfType<LocalsEditSection>().Single();
                var localControls = locals.GetLogicalDescendants().OfType<Control>()
                    .Where(control => control is Button or CompactSlider or ListBox)
                    .Select(Describe).Order().ToArray();
                output.WriteLine($"LocalsEditSection logical interactive controls (including hidden): count={localControls.Length}; {string.Join(", ", localControls)}");
                var scroll = panel.GetVisualDescendants().OfType<ScrollViewer>()
                    .Single(control => control.Name == "DevelopControlsScrollViewer");
                output.WriteLine(FormattableString.Invariant(
                    $"DevelopControlsScrollViewer: extent={scroll.Extent.Width:F2}x{scroll.Extent.Height:F2} DIP; viewport={scroll.Viewport.Width:F2}x{scroll.Viewport.Height:F2} DIP; vertical-range={Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height):F2} DIP"));
                await Task.CompletedTask;
            });
        }
    }

    private static string? ToolIdentity(Control control, MainWindowViewModel vm)
    {
        if (control is CompactSlider { Label: "Horizon" }) return Describe(control);
        if (control is ToggleButton && ToolTip.GetTip(control)?.ToString() == "Lock Aspect Ratio")
            return "aspect lock (unnamed ToggleButton)";
        if (control is not Button button) return null;
        var command = button.Command;
        if (command == vm.ToggleCropModeCommand || command == vm.ToggleLocalsModeCommand ||
            command == vm.ApplyCropCommand || command == vm.CancelCropCommand || command == vm.ResetCropCommand)
            return Describe(control);
        return null;
    }

    private static string Describe(Control control) => control switch
    {
        CompactSlider slider => $"{slider.Label} (CompactSlider, name={slider.Name ?? "none"})",
        _ when control.Name is not null => control.Name,
        Button button => $"{AutomationProperties.GetName(button) ?? button.Content?.ToString()} (unnamed {button.GetType().Name})",
        _ => $"unnamed {control.GetType().Name}"
    };

    internal static async Task WithScene(string mode, int width, int height,
        Func<MainWindowViewModel, TestUiScope, Task> measure)
    {
        using var fixture = new CatalogVmFixture("develop-tools-baseline");
        using var catalog = await fixture.CreateCatalogAsync();
        var path = GoldenTestPaths.Asset("srgb-reference.jpg");
        Assert.Equal(0, (int)File.GetAttributes(path) & (0x1000 | 0x40000 | 0x400000));
        await using var vm = fixture.CreateViewModel(catalog, new StandardBaseLoader(), _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var image = new ImageFile(path)
        {
            EditSettings = new EditSettings
            {
                Crop = new CropRegion { Left = .1, Top = .1, Right = .9, Bottom = .9 },
                Locals = [new() { Type = mode == "radial" ? "radial" : "linear", Exposure = -1, Temperature = 30, Tint = -20, Saturation = 40 }]
            }
        };
        image.CatalogId = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        vm.Browse.SetImages([image]);
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
        if (mode == "crop") await vm.ToggleCropModeCommand.ExecuteAsync(null);
        if (mode is "locals" or "radial") await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.ShowLocalMask = false;
        vm.IsLocalGeometryExpanded = false;
        if (vm.PendingPreviewDebounceTask is { } pending)
            await pending.WaitAsync(TestWaits.Condition);
        await TestWaits.UntilAsync(() => vm.CaptureBackgroundActivitySnapshot().PreviewCount == 0
            && !vm.IsBackgroundActivityStatusVisible);
        var window = new MainWindow { Width = width, Height = height };
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        await measure(vm, scope);
    }
}
