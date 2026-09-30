using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

// Observation-only WP3 before-values. Keep until the owner reviews the baseline.
public sealed class DevelopEditDotBaselineTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task MeasureDeterministicBaseline()
    {
        MeasurePasteNotes();

        foreach (var edited in new[] { false, true })
        {
            await WithScene(edited ? AllGroupsEdited() : new EditSettings(), (vm, window) =>
            {
                foreach (var expanded in new[] { true, false })
                {
                    vm.RestoreDevelopGroups(vm.DevelopGroupList.ToDictionary(g => g.Name, _ => expanded));
                    DevelopCollapseBaselineTests.Settle(window);
                    var groups = window.GetVisualDescendants().OfType<DevelopGroup>().ToArray();
                    Assert.Equal(10, groups.Length);

                    foreach (var group in groups)
                    {
                        var header = DevelopCollapseBaselineTests.Header(group);
                        var dot = header.GetVisualDescendants().OfType<Ellipse>().Single();
                        var title = header.GetVisualDescendants().OfType<TextBlock>()
                            .Single(text => text.Classes.Contains("section-label"));
                        var peer = ControlAutomationPeer.CreatePeerForElement(header)!;
                        output.WriteLine($"D2 {group.Header}: expanded={expanded}; edited={edited}; visible={dot.IsVisible}; effective={dot.IsEffectivelyVisible}");
                        output.WriteLine($"D5 {group.Header}: expanded={expanded}; edited={edited}; name={JsonSerializer.Serialize(peer.GetName())}; help={JsonSerializer.Serialize(peer.GetHelpText())}; attachedHelp={JsonSerializer.Serialize(AutomationProperties.GetHelpText(header))}; tooltip={JsonSerializer.Serialize(ToolTip.GetTip(header)?.ToString())}");

                        if (!edited && expanded)
                        {
                            output.WriteLine($"D6 {group.Header}: header={header.Bounds}; titleInHeader={In(title, header)}; dotLocal={dot.Bounds}; dotInHeader={In(dot, header)}; desired={dot.DesiredSize}; declared={dot.Width}x{dot.Height}; spacing={((StackPanel)dot.Parent!).Spacing}");
                        }
                    }
                }

                return Task.CompletedTask;
            });
        }

        // Reuse the WP2 all-expanded layout contract without duplicating its controls.
        await new DevelopHeaderBaselineTests(output).MeasureUnifiedLayout();
    }

    [AvaloniaFact]
    public async Task MeasureUpdateCanResetContext()
    {
        await WithScene(AllGroupsEdited(), (vm, window) =>
        {
            Assert.True(vm.CanEditSelectedImage);
            Assert.True(vm.IsDevelopMode);
            Assert.Equal(10, window.GetVisualDescendants().OfType<DevelopGroup>().Count());
            var update = typeof(MainWindowViewModel)
                .GetMethod("UpdateCanReset", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<Action>(vm);

            for (var i = 0; i < 1000; i++)
            {
                update();
            }

            var ticks = new long[10000];
            var allocated = GC.GetAllocatedBytesForCurrentThread();

            for (var i = 0; i < ticks.Length; i++)
            {
                var start = Stopwatch.GetTimestamp();
                update();
                ticks[i] = Stopwatch.GetTimestamp() - start;
            }

            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Array.Sort(ticks);
            var medianUs = (ticks[4999] + ticks[5000]) * .5 * 1_000_000 / Stopwatch.Frequency;
            output.WriteLine(FormattableString.Invariant(
                $"D7 medianUs={medianUs:R}; bytesPerCall={allocated / 10000d:R}; calls=10000; warmup=1000; frequency={Stopwatch.Frequency}; canReset={vm.CanReset}"));

            return Task.CompletedTask;
        });
    }

    private void MeasurePasteNotes()
    {
        var first = AllGroupsEdited();
        first.Crop = new CropRegion { Left = .1, Top = .1, Right = .9, Bottom = .9 };
        first.Locals = [new() { Id = "dot-baseline-local", Type = "linear", Exposure = -1 }];
        first.Repairs = [new() { Id = "dot-baseline-repair", Type = "clone", Su = .3 }];
        first.Lens.ProfileOverride = "Dot baseline lens";
        EditSettings[] targets = [first, first.Clone(), new()];
        var paste = new PasteSettingsViewModel("baseline", targets.Length,
            new Dictionary<string, bool>(), targets: targets);

        foreach (var row in paste.Groups)
        {
            var own = targets.Select(target => PasteSettingsViewModel.HasOwn(target, row.Group.Name));
            output.WriteLine($"D4 {row.Group.Name}: own=[{string.Join(",", own)}]; note={JsonSerializer.Serialize(row.Note)}");
        }
    }

    internal static EditSettings AllGroupsEdited()
    {
        var settings = SyncTransferParityCorpus.CreateLook();
        settings.Texture = 12;
        settings.Clarity = 8;
        settings.Geometry = new GeometrySettings { Vertical = 17 };
        settings.RawProfile = new RawProfileSelection
        {
            Source = RawProfileSource.Embedded,
            ContentHash = new string('a', 64)
        };

        return settings;
    }

    internal static Task WithScene(EditSettings settings, Func<MainWindowViewModel, Window, Task> run) =>
        WithSceneScope(settings, (vm, scope) => run(vm, scope.Window!));

    internal static async Task WithSceneScope(EditSettings settings, Func<MainWindowViewModel, TestUiScope, Task> run)
    {
        using var fixture = new CatalogVmFixture("develop-dot-baseline");
        using var catalog = await fixture.CreateCatalogAsync();
        var path = GoldenTestPaths.Asset("srgb-reference.jpg");
        Assert.Equal(0, (int)File.GetAttributes(path) & (0x1000 | 0x40000 | 0x400000));
        await using var vm = fixture.CreateViewModel(catalog, new StandardBaseLoader(), _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var expected = settings.Clone();
        var image = new ImageFile(path) { EditSettings = settings };
        image.CatalogId = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveEditSettingsAsync(image.CatalogId, settings);
        vm.Browse.SetImages([image]);
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);

        if (vm.PendingPreviewDebounceTask is { } pending)
        {
            await pending.WaitAsync(TestWaits.Condition);
        }

        await TestWaits.UntilAsync(() => vm.CaptureBackgroundActivitySnapshot().PreviewCount == 0
            && !vm.IsBackgroundActivityStatusVisible);
        using var theme = new TestUiScope(theme: ThemeVariant.Dark);
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        scope.Show();
        DevelopCollapseBaselineTests.Settle(window);
        Assert.True(image.EditSettings.HasSameEdits(expected));
        await run(vm, scope);
    }

    private static Rect In(Control control, Visual relative) =>
        new(control.TranslatePoint(default, relative)!.Value, control.Bounds.Size);
}
