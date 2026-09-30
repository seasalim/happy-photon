using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PasteSettingsDialogTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public void EveryGroupAllNoneAndDefaultsUpdateThePasteButton()
    {
        var model = new PasteSettingsViewModel("IMG_0412.CR2", 24, new Dictionary<string, bool>());
        var dialog = new PasteSettingsDialog(model);
        using var scope = new TestUiScope(dialog);
        var paste = dialog.FindControl<Button>("PasteButton")!;
        var boxes = dialog.GetLogicalDescendants().OfType<CheckBox>().ToArray();
        Assert.Equal(PasteSettingsViewModel.AvailableGroups.Select(group => group.Name),
            boxes.Select(box => box.Content));
        Assert.Contains(boxes, box => Equals(box.Content, "Presence"));

        foreach (var box in boxes)
        {
            ClickLink(dialog, "None");
            Assert.False(paste.IsEnabled);
            box.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(paste.IsEnabled);
            Assert.Equal(box.Content, Assert.Single(model.Groups, group => group.IsSelected).Group.Name);
        }

        ClickLink(dialog, "All");
        Assert.All(boxes, box => Assert.True(box.IsChecked));
        ClickLink(dialog, "None");
        Assert.False(paste.IsEnabled);
        dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        Assert.True(dialog.IsVisible);
        ClickLink(dialog, "Defaults");
        Assert.Equal(EditSettingsTransfer.LookGroups.Where(group => group.IsDefault).Select(group => group.Name),
            model.Groups.Where(group => group.IsSelected).Select(group => group.Group.Name));
        Assert.True(paste.IsEnabled);
    }

    [AvaloniaTheory]
    [InlineData("cancel", false)]
    [InlineData("escape", false)]
    [InlineData("enter", true)]
    [InlineData("paste", true)]
    public async Task DialogReturnsOnlyConfirmedChoice(string action, bool expected)
    {
        using var owner = new TestUiScope(new Window());
        var model = new PasteSettingsViewModel("source.jpg", 1, new Dictionary<string, bool>(), currentPhoto: true);
        var dialog = new PasteSettingsDialog(model);
        var result = dialog.ShowDialog<bool>(owner.Window!);

        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("From source.jpg to this photo", model.Summary);

            if (action is "enter" or "escape")
            {
                dialog.KeyPress(action == "enter" ? Key.Enter : Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            }
            else
            {
                dialog.FindControl<Button>(action == "paste" ? "PasteButton" : "CancelButton")!
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }

            Assert.Equal(expected, await result.WaitAsync(TestWaits.Condition));
        }
        finally
        {
            dialog.Close(false);
        }
    }

    [AvaloniaFact]
    public void MissingNamesUseDefaultsAndPhotoSpecificChoicesAreRemembered()
    {
        var model = new PasteSettingsViewModel("source.jpg", 1,
            new Dictionary<string, bool> { ["White Balance"] = false, ["Geometry"] = true, ["Future"] = true });
        Assert.False(model.Groups[0].IsSelected);
        Assert.True(Assert.Single(model.Groups, group => group.Group.Name == "Geometry").IsSelected);
        Assert.All(model.Groups.Where(group => group.Group.Name is not ("White Balance" or "Geometry")),
            group => Assert.Equal(group.Group.IsDefault, group.IsSelected));
        Assert.False(Assert.Single(model.Groups, group => group.Group.Name == "Spot Removal").IsSelected);
        Assert.DoesNotContain("Future", model.CaptureChoice().Keys);
    }

    [AvaloniaTheory]
    [InlineData("paste-settings-default", false)]
    [InlineData("paste-settings-none", true)]
    [InlineData("paste-settings-photo-groups", false)]
    [InlineData("paste-settings-profiles", false)]
    [InlineData("paste-settings-spots", false)]
    public void Showcase(string scene, bool none)
    {
        var targets = Enumerable.Range(0, 24).Select(index => new EditSettings
        {
            RawProfile = index < 6 ? new RawProfileSelection { Source = RawProfileSource.UserFile } : null,
            Lens = new() { ProfileOverride = index < 8 ? "Canon EF 50mm f/1.8 MkII" : null },
            Crop = index < 5 ? new CropRegion { Left = .1 } : null,
            Repairs = index < 9 ? [new Repair()] : null,
            Locals = index < 3 ? [new LocalAdjustment()] : null
        }).ToArray();
        var choice = new Dictionary<string, bool>();

        if (scene == "paste-settings-photo-groups")
        {
            choice["Crop & Straighten"] = true;
            choice["Locals"] = true;
        }

        if (scene == "paste-settings-profiles")
        {
            choice["Camera Profile"] = true;
            choice["Lens Profile"] = true;
        }

        if (scene == "paste-settings-spots")
        {
            choice["Spot Removal"] = true;
        }

        var model = new PasteSettingsViewModel("IMG_0412.CR2", 24, choice, targets: targets, reframeCount: _ => 3);
        if (none) model.NoneCommand.Execute(null);

        if (scene == "paste-settings-spots")
        {
            Assert.Equal(6, model.PhotoGroups.Count());
            var spots = Assert.Single(model.Groups, group => group.Group.Name == "Spot Removal");
            Assert.True(spots.IsSelected);
            Assert.Equal("replaces own on 9 of 24\nother camera bodies keep their own", spots.Note);
        }

        var dialog = new PasteSettingsDialog(model);
        ShowcaseTestHelper.Capture(scene, dialog, new PixelSize((int)dialog.Width, (int)dialog.Height), ThemeVariant.Dark,
            shown =>
            {
                Assert.Equal(!none, shown.FindControl<Button>("PasteButton")!.IsEnabled);

                if (scene == "paste-settings-default")
                {
                    AssertLookMatchesOriginalLayout(shown);
                }
            });
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void LookNotesAppearOnlyForGroupsWithOwnValues(bool edited)
    {
        var target = edited
            ? new EditSettings { Texture = 12, Detail = new() { LuminanceNr = 15 } }
            : new EditSettings();
        var model = new PasteSettingsViewModel("source.jpg", 3, new Dictionary<string, bool>(),
            targets: [target, target.Clone(), new()]);
        var dialog = new PasteSettingsDialog(model);
        using var scope = new TestUiScope(dialog);
        var notes = dialog.GetLogicalDescendants().OfType<TextBlock>()
            .Where(text => text.DataContext is PasteSettingsGroupViewModel group && text.Text == group.Note)
            .ToArray();
        var lookNotes = notes.Where(text =>
            ((PasteSettingsGroupViewModel)text.DataContext!).Group.Kind == EditSettingsGroupKind.Look).ToArray();
        var visible = lookNotes.Where(text => text.IsEffectivelyVisible).ToArray();
        output.WriteLine($"Look notes visible: {visible.Length}; expected: {(edited ? 2 : 0)}");
        Assert.Equal(edited ? new[] { "Presence", "Detail" } : [],
            visible.Select(text => ((PasteSettingsGroupViewModel)text.DataContext!).Group.Name));
        Assert.All(visible, text => Assert.Equal("replaces own on 2 of 3", text.Text));
        Assert.All(lookNotes.Except(visible), text => Assert.Equal(default, text.Bounds.Size));
        Assert.Equal(6, notes.Except(lookNotes).Count(text => text.IsEffectivelyVisible));

        if (!edited)
        {
            AssertLookMatchesOriginalLayout(dialog);
        }
    }

    private void AssertLookMatchesOriginalLayout(Window dialog)
    {
        var look = Assert.Single(dialog.GetLogicalDescendants().OfType<ItemsControl>(),
            control => control.ItemsSource?.Cast<object>().FirstOrDefault() is PasteSettingsGroupViewModel
                { Group.Kind: EditSettingsGroupKind.Look });
        var template = look.ItemTemplate;
        var actual = Bounds();

        try
        {
            // The Look template at 8d9e712 contains only this checkbox.
            look.ItemTemplate = new FuncDataTemplate<PasteSettingsGroupViewModel>((group, _) =>
                new CheckBox { Content = group!.Group.Name, IsChecked = group.IsSelected });
            Dispatcher.UIThread.RunJobs();
            dialog.UpdateLayout();
            var original = Bounds();
            output.WriteLine($"Look original/current last checkbox: {original[7]} / {actual[7]}");
            Assert.Equal(original, actual);
        }
        finally
        {
            look.ItemTemplate = template;
            Dispatcher.UIThread.RunJobs();
            dialog.UpdateLayout();
        }

        Rect[] Bounds() => look.GetLogicalDescendants().OfType<CheckBox>().Cast<Control>()
            .Concat(dialog.GetLogicalDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("choice-link")))
            .Select(control => new Rect(control.TranslatePoint(default, dialog)!.Value, control.Bounds.Size))
            .ToArray();
    }

    [AvaloniaFact]
    public async Task ConfirmedChoiceSurvivesCatalogReopenAndPreferenceSaves()
    {
        using var fixture = new CatalogVmFixture("paste-choice-restart");
        Dictionary<string, bool> chosen;

        using (var catalog = await fixture.CreateCatalogAsync())
        {
            await using var vm = fixture.CreateViewModel(catalog, new PasteGateLoader(),
                _ => Task.CompletedTask,
                new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
                timeProvider: new TestTimeProvider());
            var saves = 0;
            vm.PersistAppSettingsAsync = () =>
            {
                saves++;

                return new AppSettingsService(catalog).SaveAsync(new AppSettings
                {
                    PasteGroups = vm.CapturePasteGroups()
                });
            };
            var source = new ImageFile(fixture.Path("source.dng"));
            vm.SelectedImage = source;
            vm.CopyEditSettingsCommand.Execute(null);
            vm.ShowPasteSettingsAsync = model =>
            {
                model.NoneCommand.Execute(null);
                model.Groups[0].IsSelected = true;

                return Task.FromResult(true);
            };
            await vm.PasteEditSettingsCommand.ExecuteAsync(null);
            Assert.Equal(1, saves);
            chosen = vm.CapturePasteGroups();
            Assert.True(chosen["White Balance"]);
            Assert.All(chosen.Where(item => item.Key != "White Balance"), item => Assert.False(item.Value));
        }

        using var reopened = await fixture.CreateCatalogAsync();
        var service = new AppSettingsService(reopened);
        var settings = await service.LoadAsync();
        Assert.Equal(chosen.OrderBy(pair => pair.Key), settings.PasteGroups.OrderBy(pair => pair.Key));
        await service.SavePreferencesAsync(settings);
        await service.SaveAsync(settings);
        var restored = new PasteSettingsViewModel("source.dng", 1, (await service.LoadAsync()).PasteGroups);
        Assert.Equal("White Balance", Assert.Single(restored.Groups, group => group.IsSelected).Group.Name);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void LongSourceNameKeepsActionsInsideWindow(bool edited)
    {
        var target = edited ? DevelopEditDotBaselineTests.AllGroupsEdited() : new EditSettings();
        target.Crop = new() { Left = .1 };
        var model = new PasteSettingsViewModel(new string('W', 240) + ".CR2", 24,
            new Dictionary<string, bool>(), targets: [target],
            reframeCount: _ => 3);
        var dialog = new PasteSettingsDialog(model);
        using var scope = new TestUiScope(dialog);

        Dispatcher.UIThread.RunJobs();
        dialog.UpdateLayout();
        var buttons = dialog.GetLogicalDescendants().OfType<Button>()
            .Where(button => button.Content is "Paste" or "Cancel" or "All" or "None" or "Defaults").ToArray();
        Assert.Equal(5, buttons.Length);
        var footerTop = dialog.FindControl<Button>("PasteButton")!
            .TranslatePoint(new Point(), dialog)!.Value.Y;

        foreach (var button in buttons)
        {
            var origin = button.TranslatePoint(new Point(), dialog)!.Value;
            var bounds = new Rect(origin, button.Bounds.Size);
            Assert.True(new Rect(dialog.ClientSize).Contains(bounds), $"{button.Content}: {bounds}");

            if (button.Content is "All" or "None" or "Defaults")
            {
                Assert.True(bounds.Bottom <= footerTop, $"{button.Content} overlaps the footer.");
            }
        }

        var boxes = dialog.GetLogicalDescendants().OfType<CheckBox>().ToArray();
        Assert.Equal(model.Groups.Count, boxes.Length);
        var notes = dialog.GetLogicalDescendants().OfType<TextBlock>()
            .Where(text => text.DataContext is PasteSettingsGroupViewModel group && text.Text == group.Note)
            .ToArray();
        notes = notes.Where(text => text.IsEffectivelyVisible).ToArray();
        Assert.Equal(edited ? 14 : 6, notes.Length);
        var lastBottom = 0d;

        foreach (var control in boxes.Cast<Control>().Concat(notes))
        {
            var origin = control.TranslatePoint(new Point(), dialog)!.Value;
            var bounds = new Rect(origin, control.Bounds.Size);
            Assert.True(new Rect(dialog.ClientSize).Contains(bounds), $"{control.DataContext}: {bounds}");
            lastBottom = Math.Max(lastBottom, bounds.Bottom);
        }

        var gap = footerTop - lastBottom;
        output.WriteLine($"Group rows={notes.Length}; footer clearance={gap:F1}px");
        Assert.True(gap >= 18, $"Last group is {gap:F1}px above the footer; expected at least 18px.");

        var summary = Assert.Single(dialog.GetLogicalDescendants().OfType<TextBlock>(),
            text => text.Text == model.Summary);
        Assert.Equal(Avalonia.Media.TextWrapping.NoWrap, summary.TextWrapping);
        Assert.Equal(Avalonia.Media.TextTrimming.CharacterEllipsis, summary.TextTrimming);
        Assert.Equal(model.Summary, ToolTip.GetTip(summary));
    }

    private static void ClickLink(Window dialog, string name)
    {
        var button = Assert.Single(dialog.GetLogicalDescendants().OfType<Button>(),
            button => Equals(button.Content, name));
        button.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
    }
}
