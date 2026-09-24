using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsShowcaseTests
{
    [AvaloniaFact]
    public async Task RenderEightDisabledScene()
    {
        using var fixture = new CatalogVmFixture("locals-eight-shots");
        using var catalog = await fixture.CreateCatalogAsync();
        var path = GoldenTestPaths.Asset("srgb-reference.jpg");
        Assert.Equal(0, (int)File.GetAttributes(path) & (0x1000 | 0x40000 | 0x400000));
        await using var vm = fixture.CreateViewModel(catalog, new StandardBaseLoader(), _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var image = new ImageFile(path) { EditSettings = new() { Locals = Enumerable.Range(0, 8)
            .Select(i => new LocalAdjustment
            {
                Ordinal = i + 1, Type = i % 2 == 0 ? "radial" : "linear", Enabled = i != 7,
                Cu = .2 * (i % 4 + 1), Cv = (i / 4 + 1) / 3d, Rx = .16, Ry = .11,
                Exposure = -.5, Temperature = 15, Tint = -10, Saturation = 20,
                Luminance = i % 3 == 0 ? new() { Enabled = true, Lower = .3 } : null,
                Hue = i % 3 == 1 ? new() { Enabled = true, Center = 240 } : null
            }).ToList() } };
        image.CatalogId = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        vm.Browse.SetImages([image]); vm.IsDevelopMode = true; vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.SelectedLocal = vm.Locals[^1];
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        ShowcaseTestHelper.Capture("develop-locals-eight-disabled", scope, new PixelSize(1200, 700), HappyPhotonThemes.MidGray,
            shown =>
            {
                var section = shown.GetVisualDescendants().OfType<LocalsEditSection>().Single();
                var list = section.FindControl<ListBox>("LocalList")!;
                var panel = shown.GetVisualDescendants().OfType<ScrollViewer>()
                    .Single(control => control.Name == "DevelopControlsScrollViewer");
                var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
                Assert.Equal(8, list.ItemCount);
                Assert.Equal(120, list.MaxHeight);
                Assert.True(list.Bounds.Height <= 120);
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
                list.ScrollIntoView(vm.SelectedLocalRow!);
                Dispatcher.UIThread.RunJobs();
                Assert.True(scroll.Offset.Y > 0);
                var selected = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(7));
                Assert.True(selected.IsSelected);
                Assert.False(vm.SelectedLocal!.Enabled);
                AssertVisibleWithin(selected, scroll);
                Assert.True(selected.Focus(NavigationMethod.Tab));
                Assert.Same(selected, shown.FocusManager!.GetFocusedElement());
                var sliders = section.GetVisualDescendants().OfType<CompactSlider>()
                    .Where(slider => slider.Label is "Exposure" or "Temperature" or "Tint" or "Saturation").ToArray();
                Assert.Equal(4, sliders.Length);
                var reached = new HashSet<CompactSlider>();
                for (var i = 0; i < 100 && reached.Count < sliders.Length; i++)
                {
                    shown.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                    shown.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                    if (shown.FocusManager.GetFocusedElement() is CompactSlider slider && sliders.Contains(slider)) reached.Add(slider);
                }
                Assert.Equal(sliders.Length, reached.Count);
                panel.Offset = default;
                list.ScrollIntoView(vm.SelectedLocalRow!);
                Dispatcher.UIThread.RunJobs();
                Assert.All(sliders, slider => AssertVisibleWithin(slider, panel));
                AssertVisibleWithin(selected, scroll);
            });
    }

    [AvaloniaFact]
    public async Task RenderUnavailableScene()
    {
        using var fixture = new CatalogVmFixture("locals-unavailable-shots");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.RequiresHydration));
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var image = new ImageFile(fixture.Path("cloud-only.jpg"), SourceAvailability.RequiresHydration)
        { EditSettings = new() { Locals = [new() { Exposure = -1 }] } };
        vm.Browse.SetImages([image]); vm.SelectedImage = image; vm.IsDevelopMode = true;
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        ShowcaseTestHelper.Capture("develop-locals-unavailable", scope, new PixelSize(1440, 900), ThemeVariant.Dark,
            shown =>
            {
                var section = shown.GetVisualDescendants().OfType<LocalsEditSection>().Single();
                var panel = shown.GetVisualDescendants().OfType<ScrollViewer>()
                    .Single(control => control.Name == "DevelopControlsScrollViewer");
                panel.Offset = default;
                Dispatcher.UIThread.RunJobs();
                Assert.True(section.IsEffectivelyVisible);
                AssertVisibleWithin(section.FindControl<ListBox>("LocalList")!, panel);
                Assert.False(vm.CanEditLocals);
                Assert.False(shown.FindControl<DevelopEditPanel>("DevelopEditPanel")!.IsEffectivelyEnabled);
                Assert.All(section.GetVisualDescendants().OfType<CompactSlider>(), slider => Assert.False(slider.IsEffectivelyEnabled));
                Assert.Contains(shown.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.IsEffectivelyVisible && text.Text == "This original is online-only.");
                Assert.True(shown.FindControl<Button>("DownloadAndOpenButton")!.IsEffectivelyVisible);
                Assert.Null(vm.PreviewImage);
            });
    }

    private static void AssertVisibleWithin(Control control, Control viewport)
    {
        Assert.True(control.IsEffectivelyVisible);
        var origin = control.TranslatePoint(default, viewport)!.Value;
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
        Assert.InRange(origin.X, 0, viewport.Bounds.Width - control.Bounds.Width);
        Assert.InRange(origin.Y, 0, viewport.Bounds.Height - control.Bounds.Height);
    }
}
