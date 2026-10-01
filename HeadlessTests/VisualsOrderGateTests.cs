using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

// Owner-approved VISUALS-WP2 targets replace the reproduced before-value probes.
public sealed class VisualsOrderGateTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task VisualsOrderGateG1RowOrder()
    {
        await DevelopEditDotBaselineTests.WithScene(new EditSettings(), (vm, window) =>
        {
            Assert.False(vm.SelectedImage!.IsRaw);
            Assert.True(vm.IsColorEditingEnabled);
            vm.RestoreDevelopGroups(vm.DevelopGroupList.ToDictionary(group => group.Name, _ => true));
            DevelopCollapseBaselineTests.Settle(window);

            foreach (var name in new[] { "Adjustments", "Presence" })
            {
                var group = window.GetVisualDescendants().OfType<DevelopGroup>()
                    .Single(control => Equals(control.Header, name));
                var rows = group.GetVisualDescendants().OfType<Control>()
                    .Where(control => control is CompactSlider || control.Name == "HighlightHandlingRow")
                    .Select(control => new
                    {
                        Label = control is CompactSlider slider ? slider.Label : control
                            .GetVisualDescendants().OfType<TextBlock>()
                            .Single(text => text.Classes.Contains("edit-control-label")).Text,
                        Y = control.TranslatePoint(default, group)!.Value.Y
                    })
                    .OrderBy(row => row.Y).ToArray();
                string[] expected = name == "Adjustments"
                    ? ["Exposure", "Brightness", "Contrast", "Highlights", "Shadows", "Whites", "Blacks", "Recovery"]
                    : ["Texture", "Clarity", "Vibrance", "Saturation"];
                Assert.Equal(expected, rows.Select(row => row.Label));
                output.WriteLine($"G1 {name}: {string.Join(", ", rows.Select(row => row.Label))}");
            }

            return Task.CompletedTask;
        });
    }

    [Fact]
    public void VisualsOrderGateG2PasteMembership()
    {
        var source = new EditSettings { Vibrance = 31, Saturation = 19, Texture = 12, Clarity = 8 };

        foreach (var name in new[] { "Presence", "Adjustments" })
        {
            var target = new EditSettings { Vibrance = -7, Saturation = -11, Texture = -3, Clarity = -5 };
            var choice = EditSettingsTransfer.Groups.ToDictionary(group => group.Name, group => group.Name == name);
            var paste = new PasteSettingsViewModel("source.jpg", 1, choice, targets: [target]);
            var selected = paste.Groups.Where(group => group.IsSelected).Select(group => group.Group).ToArray();
            Assert.Single(selected);
            EditSettingsTransfer.ApplyGroups(source, target, selected);
            Assert.Equal(name == "Presence" ? (31d, 19d, 12d, 8d) : (-7d, -11d, -3d, -5d),
                (target.Vibrance, target.Saturation, target.Texture, target.Clarity));
            output.WriteLine($"G2 {name}-only: Vibrance={target.Vibrance}; Saturation={target.Saturation}; " +
                "source=(31,19); target-before=(-7,-11)");
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VisualsOrderGateG3CollapsedDots(bool vibrance)
    {
        await DevelopEditDotBaselineTests.WithScene(
            vibrance ? new EditSettings { Vibrance = 31 } : new EditSettings { Saturation = 19 }, (vm, window) =>
        {
            vm.RestoreDevelopGroups(vm.DevelopGroupList.ToDictionary(group => group.Name, _ => false));
            vm.RefreshDevelopGroupEdits();
            DevelopCollapseBaselineTests.Settle(window);
            var groups = window.GetVisualDescendants().OfType<DevelopGroup>().ToArray();
            Assert.NotEmpty(groups);

            foreach (var group in groups)
            {
                Assert.False(group.IsExpanded);
                var dot = DevelopCollapseBaselineTests.Header(group)
                    .GetVisualDescendants().OfType<Ellipse>().Single();
                Assert.Equal(Equals(group.Header, "Presence"), dot.IsVisible);
                output.WriteLine($"G3 {group.Header}: dot-lit={dot.IsVisible}");
            }

            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task VisualsOrderGateG4StoredPasteChoice()
    {
        using var fixture = new CatalogVmFixture("visuals-order-g4");
        using var catalog = await fixture.CreateCatalogAsync();
        await catalog.SetAppSettingAsync("PasteGroups", "{\"Adjustments\":true,\"Presence\":false}");
        var loaded = await new AppSettingsService(catalog).LoadAsync();

        foreach (var name in new[] { "Adjustments", "Presence" })
        {
            var group = EditSettingsTransfer.Groups.Single(group => group.Name == name);
            Assert.True(loaded.PasteGroups.GetValueOrDefault(name, group.IsDefault));
            output.WriteLine($"G4 {name}: loaded={loaded.PasteGroups.GetValueOrDefault(name, group.IsDefault)}; " +
                $"key-present={loaded.PasteGroups.ContainsKey(name)}");
        }
    }
}
