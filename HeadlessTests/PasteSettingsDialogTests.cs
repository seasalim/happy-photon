using Avalonia;
using Avalonia.Controls;
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

public sealed class PasteSettingsDialogTests
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
        Assert.DoesNotContain(model.Groups, group => group.Group.Name is "Camera Profile" or "Lens Profile" or "Spot Removal");
        Assert.DoesNotContain("Future", model.CaptureChoice().Keys);
    }

    [AvaloniaTheory]
    [InlineData("paste-settings-default", false)]
    [InlineData("paste-settings-none", true)]
    [InlineData("paste-settings-photo-groups", false)]
    public void Showcase(string scene, bool none)
    {
        var targets = Enumerable.Range(0, 24).Select(index => new EditSettings
        {
            Crop = index < 5 ? new CropRegion { Left = .1 } : null,
            Locals = index < 3 ? [new LocalAdjustment()] : null
        }).ToArray();
        var choice = new Dictionary<string, bool>();

        if (scene == "paste-settings-photo-groups")
        {
            choice["Crop & Straighten"] = true;
            choice["Locals"] = true;
        }

        var model = new PasteSettingsViewModel("IMG_0412.CR2", 24, choice, targets: targets, reframeCount: _ => 3);
        if (none) model.NoneCommand.Execute(null);

        var dialog = new PasteSettingsDialog(model);
        ShowcaseTestHelper.Capture(scene, dialog, new PixelSize(660, 490), ThemeVariant.Dark,
            shown => Assert.Equal(!none, shown.FindControl<Button>("PasteButton")!.IsEnabled));
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

    [AvaloniaFact]
    public void LongSourceNameKeepsActionsInsideWindow()
    {
        var model = new PasteSettingsViewModel(new string('W', 240) + ".CR2", 24,
            new Dictionary<string, bool>());
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
