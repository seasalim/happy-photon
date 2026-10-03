using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PresenceShowcaseTests
{
    [AvaloniaFact]
    public async Task DevelopAdjustmentsPresence()
    {
        await DevelopEditDotBaselineTests.WithSceneScope(new EditSettings(), (vm, scope) =>
        {
            vm.RestoreDevelopGroups(vm.DevelopGroupList.ToDictionary(group => group.Name, _ => true));
            ShowcaseTestHelper.Capture("develop-adjustments-presence", scope,
                new PixelSize(1600, 1000), ThemeVariant.Dark,
                window =>
                {
                    var panel = window.GetVisualDescendants().OfType<DevelopEditPanel>().Single();
                    panel.FindControl<ScrollViewer>("DevelopControlsScrollViewer")!.Offset = default;
                    DevelopCollapseBaselineTests.Settle(window);
                });

            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task PresencePanel()
    {
        using var fixture = new CatalogVmFixture("presence-showcase");
        using var catalog = await fixture.CreateCatalogAsync();
        var path = GoldenTestPaths.Asset("srgb-reference.jpg");
        GoldenTestPaths.RequireReadableFixture(path);
        await using var vm = fixture.CreateViewModel(catalog, new StandardBaseLoader(), _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var image = new ImageFile(path)
        {
            CatalogId = await catalog.GetOrCreateImageAsync(path),
            EditSettings = new() { Texture = 40, Clarity = 40 }
        };

        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        vm.Browse.SetImages([image]);
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);

        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        ShowcaseTestHelper.Capture("presence-panel", scope, new PixelSize(1200, 700), ThemeVariant.Dark,
            host =>
            {
                var group = host.GetVisualDescendants().OfType<PresenceEditGroup>().Single();
                var wrapper = Assert.IsType<DevelopGroup>(group.Parent);
                var stack = Assert.IsType<StackPanel>(wrapper.Parent);
                var index = stack.Children.IndexOf(wrapper);
                var adjustments = Assert.IsType<DevelopGroup>(stack.Children[index - 1]);
                var adjustmentControls = Assert.IsType<StackPanel>(adjustments.Content);
                Assert.Equal("HighlightHandlingRow", Assert.IsType<Grid>(adjustmentControls.Children.Last()).Name);
                var curve = Assert.IsType<DevelopGroup>(stack.Children[index + 1]);
                Assert.Equal("ToneCurveView", Assert.IsType<CurveView>(curve.Content).Name);
                var sliders = group.GetVisualDescendants().OfType<CompactSlider>().ToArray();
                Assert.Equal(["Texture", "Clarity", "Vibrance", "Saturation"], sliders.Select(slider => slider.Label));
                Assert.All(sliders, slider =>
                {
                    Assert.Equal(slider.Label is "Texture" or "Clarity" ? 40 : 0, slider.Value);
                    Assert.Equal(-100, slider.Minimum);
                    Assert.Equal(100, slider.Maximum);
                    Assert.True(slider.EnableDoubleClickReset);
                    Assert.True(slider.IsEffectivelyEnabled);
                });

                group.BringIntoView();
            });
    }
}
