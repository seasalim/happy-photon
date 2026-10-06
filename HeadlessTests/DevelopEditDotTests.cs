using System.Diagnostics;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DevelopEditDotTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task D2D5D6TruthTableAccessibilityAndBounds()
    {
        await DevelopEditDotBaselineTests.WithScene(new EditSettings(), (vm, window) =>
        {
            var groups = window.GetVisualDescendants().OfType<DevelopGroup>().ToArray();
            Assert.Equal(10, groups.Length);
            var heights = groups.Select(group => DevelopCollapseBaselineTests.Header(group).Bounds.Height).ToArray();
            var edited = DevelopEditDotBaselineTests.AllGroupsEdited();

            foreach (var hasEdits in new[] { false, true })
            {
                vm.SelectedImage!.EditSettings = hasEdits ? edited : new EditSettings();
                vm.RefreshDevelopGroupEdits();

                foreach (var expanded in new[] { false, true })
                {
                    vm.RestoreDevelopGroups(vm.DevelopGroupList.ToDictionary(group => group.Name, _ => expanded));
                    DevelopCollapseBaselineTests.Settle(window);

                    for (var i = 0; i < groups.Length; i++)
                    {
                        var header = DevelopCollapseBaselineTests.Header(groups[i]);
                        var dot = header.GetVisualDescendants().OfType<Ellipse>().Single();
                        var title = header.GetVisualDescendants().OfType<TextBlock>()
                            .Single(text => text.Classes.Contains("section-label"));
                        var expected = hasEdits && !expanded;
                        var peer = ControlAutomationPeer.CreatePeerForElement(header)!;
                        Assert.Equal(hasEdits, vm.DevelopGroupList[i].HasEdits);
                        Assert.Equal(expected, vm.DevelopGroupList[i].ShowsEditDot);
                        Assert.Equal(expected, dot.IsVisible);
                        Assert.Equal(expected, peer.GetHelpText() == "has edits");
                        Assert.Equal(vm.DevelopGroupList[i].Name, peer.GetName());
                        Assert.InRange(header.Bounds.Height, heights[i] - .5, heights[i] + .5);

                        if (expected)
                        {
                            var dotBounds = new Rect(dot.TranslatePoint(default, header)!.Value, dot.Bounds.Size);
                            var titleBounds = new Rect(title.TranslatePoint(default, header)!.Value, title.Bounds.Size);
                            Assert.Equal(new Size(5, 5), dotBounds.Size);
                            Assert.InRange(dotBounds.Left - titleBounds.Right, 5.5, 6.5);
                            Assert.InRange(dotBounds.Center.Y - titleBounds.Center.Y, -.5, .5);
                            Assert.True(dot.TryFindResource("TextSecondary", dot.ActualThemeVariant, out var brush));
                            Assert.Same(brush, dot.Fill);
                        }
                    }
                }
            }

            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task D4LookNotesUseSharedChecks()
    {
        await DevelopEditDotBaselineTests.WithScene(new EditSettings(), (_, _) =>
        {
            var edited = DevelopEditDotBaselineTests.AllGroupsEdited();
            var model = new PasteSettingsViewModel("source.jpg", 3, new Dictionary<string, bool>(),
                targets: [edited, edited.Clone(), new()]);

            foreach (var group in model.LookGroups)
            {
                Assert.Equal("replaces own on 2 of 3", group.Note);
                output.WriteLine($"D4 {group.Group.Name}: none have their own -> {group.Note}");
            }

            Assert.False(PasteSettingsViewModel.HasOwn(edited, "Unknown"));

            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task D7SteadyAndTransitionRefreshCost()
    {
        await DevelopEditDotBaselineTests.WithScene(DevelopEditDotBaselineTests.AllGroupsEdited(), (vm, window) =>
        {
            Assert.True(vm.CanEditSelectedImage);
            Assert.True(vm.IsDevelopMode);
            Assert.Equal(10, window.GetVisualDescendants().OfType<DevelopGroup>().Count());
            vm.RestoreDevelopGroups(vm.DevelopGroupList.ToDictionary(group => group.Name, _ => false));
            vm.RefreshDevelopGroupEdits();
            Assert.All(vm.DevelopGroupList, group => Assert.True(group.HasEdits));
            DevelopCollapseBaselineTests.Settle(window);

            // WP2: all four Presence fields must be neutral for the unedited half.
            vm.SelectedImage!.EditSettings.Vibrance = 0;
            vm.SelectedImage.EditSettings.Saturation = 0;

            foreach (var transition in new[] { false, true })
            {
                void Refresh()
                {
                    if (transition)
                    {
                        var settings = vm.SelectedImage!.EditSettings;
                        settings.Texture = settings.Texture == 0 ? 12 : 0;
                        settings.Clarity = 0;
                    }

                    vm.RefreshDevelopGroupEdits();
                }

                for (var i = 0; i < 1000; i++)
                {
                    var before = vm.PresenceGroup.HasEdits;
                    Refresh();

                    if (transition)
                    {
                        Assert.Equal(!before, vm.PresenceGroup.HasEdits);
                    }
                }

                var ticks = new long[10000];
                var allocated = GC.GetAllocatedBytesForCurrentThread();

                for (var i = 0; i < ticks.Length; i++)
                {
                    var start = Stopwatch.GetTimestamp();
                    Refresh();
                    ticks[i] = Stopwatch.GetTimestamp() - start;
                }

                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                Array.Sort(ticks);
                var medianUs = (ticks[4999] + ticks[5000]) * .5 * 1_000_000 / Stopwatch.Frequency;
                output.WriteLine($"D7 transition={transition}; medianUs={medianUs:R}; bytesPerCall={allocated / 10000d:R}");
                Assert.True(medianUs <= (transition ? 50 : 5), $"Refresh median {medianUs} µs");

                if (!transition)
                {
                    Assert.Equal(0, allocated);
                }
            }

            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task DevelopGroupsEditDotsShowcase()
    {
        await DevelopEditDotBaselineTests.WithSceneScope(new EditSettings
        {
            Texture = 12,
            Detail = new DetailSettings { LuminanceNr = 15 }
        }, (vm, scope) =>
        {
            vm.PresenceGroup.IsExpanded = false;
            vm.ToneCurveGroup.IsExpanded = false;
            vm.DetailGroup.IsExpanded = false;
            ShowcaseTestHelper.Capture("develop-groups-edit-dots", scope,
                new PixelSize(1200, 700), ThemeVariant.Dark, shown =>
                {
                    shown.UpdateLayout();
                    var scroll = shown.GetVisualDescendants().OfType<ScrollViewer>()
                        .Single(control => control.Name == "DevelopControlsScrollViewer");
                    var presence = shown.GetVisualDescendants().OfType<DevelopGroup>()
                        .Single(group => Equals(group.Header, "Presence"));
                    var header = DevelopCollapseBaselineTests.Header(presence);

                    scroll.Offset = new Vector(0, scroll.Offset.Y + header.TranslatePoint(default, scroll)!.Value.Y);
                    shown.UpdateLayout();
                    ShowcaseTestHelper.SettleExpanderChevrons(shown);

                    Assert.InRange(header.TranslatePoint(default, scroll)!.Value.Y, -.5, .5);
                    Assert.Equal(new[] { "Presence", "Detail" },
                        vm.DevelopGroupList.Where(group => group.ShowsEditDot).Select(group => group.Name));
                });

            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public void PasteSettingsOwnNotesShowcase()
    {
        var model = new PasteSettingsViewModel("IMG_0412.CR2", 3, new Dictionary<string, bool>(),
            targets: [new() { Texture = 12 }, new() { Texture = 20 }, new()]);
        Assert.Equal("replaces own on 2 of 3",
            model.Groups.Single(group => group.Group.Name == "Presence").Note);
        var dialog = new PasteSettingsDialog(model);
        ShowcaseTestHelper.Capture("paste-settings-own-notes", dialog,
            new PixelSize((int)dialog.Width, (int)dialog.Height), ThemeVariant.Dark, shown =>
            {
                var note = Assert.Single(shown.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == "replaces own on 2 of 3");
                Assert.True(note.IsEffectivelyVisible);
                var links = shown.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.Classes.Contains("choice-link"));
                var footer = shown.FindControl<Button>("PasteButton")!;
                var footerTop = footer.TranslatePoint(default, shown)!.Value.Y;

                foreach (var link in links)
                {
                    Assert.True(link.TranslatePoint(default, shown)!.Value.Y + link.Bounds.Height <= footerTop);
                }
            });
    }
}
